using System.Globalization;
using System.Text;
using System.Windows;

namespace RdpManager.Common;

/// <summary>分割の向き。Horizontal は左右に並べる（縦の境界線）、Vertical は上下に並べる（横の境界線）。</summary>
public enum SplitDirection { Horizontal, Vertical }

/// <summary>レイアウトツリーのノード。葉（<see cref="PaneNode"/>）か分割（<see cref="SplitNode"/>）。</summary>
public abstract class LayoutNode
{
    public SplitNode? Parent { get; internal set; }
}

/// <summary>タブ列を1つ持つペイン（ツリーの葉）。Id はセッション側の TabControl と対応付けるキー。</summary>
public sealed class PaneNode : LayoutNode
{
    public int Id { get; }
    internal PaneNode(int id) => Id = id;
}

/// <summary>2つの子を Direction 方向に Ratio（First 側の割合）で並べる分割ノード。</summary>
public sealed class SplitNode : LayoutNode
{
    public SplitDirection Direction { get; }
    public LayoutNode First { get; internal set; }
    public LayoutNode Second { get; internal set; }
    public double Ratio { get; internal set; }

    internal SplitNode(SplitDirection direction, LayoutNode first, LayoutNode second, double ratio)
    {
        Direction = direction;
        First = first;
        Second = second;
        Ratio = ratio;
        first.Parent = this;
        second.Parent = this;
    }
}

/// <summary>
/// 自由分割レイアウト（VS Code / tmux 風）の純ロジック。ペインを入れ子の2分割ツリーで表し、
/// 表示領域から各ペインとスプリッターの矩形を計算する。WPF 要素は持たない（UI 側は計算結果で配置するだけ）。
/// 矩形計算方式にしているのは、分割構成が変わってもペインの親要素を付け替えずに済ませるため
/// （付け替えると埋め込み RDP の HWND 再作成が走る）。
/// </summary>
public sealed class PaneLayout
{
    /// <summary>Ratio の下限・上限。片側が潰れて操作できなくなるのを防ぐ。</summary>
    public const double MinRatio = 0.05;
    public const double MaxRatio = 0.95;

    private int _nextId;

    public LayoutNode Root { get; private set; }

    public PaneLayout()
    {
        Root = new PaneNode(_nextId++);
    }

    /// <summary>全ペインの ID（左上→右下の順。ペイン巡回とセッション保存の順序に使う）。</summary>
    public IReadOnlyList<int> PaneIds => Panes(Root).Select(p => p.Id).ToList();

    public int PaneCount => Panes(Root).Count();

    private static IEnumerable<PaneNode> Panes(LayoutNode node)
    {
        if (node is PaneNode p) { yield return p; yield break; }
        var s = (SplitNode)node;
        foreach (var x in Panes(s.First)) yield return x;
        foreach (var x in Panes(s.Second)) yield return x;
    }

    private PaneNode? Find(int paneId) => Panes(Root).FirstOrDefault(p => p.Id == paneId);

    public bool Contains(int paneId) => Find(paneId) != null;

    /// <summary>指定ペインを direction 方向に2分割し、後ろ側（右または下）に作った新ペインの ID を返す。
    /// 存在しないペインなら -1。</summary>
    public int Split(int paneId, SplitDirection direction)
    {
        if (Find(paneId) is not { } target) return -1;
        var parent = target.Parent;
        var created = new PaneNode(_nextId++);
        var split = new SplitNode(direction, target, created, 0.5);
        Replace(parent, target, split);
        return created.Id;
    }

    /// <summary>ペインを取り除き、兄弟ノードがその場所を引き継ぐ。最後の1ペインは消せない（false）。</summary>
    public bool Remove(int paneId)
    {
        if (Find(paneId) is not { Parent: { } parent } target) return false;
        var sibling = parent.First == target ? parent.Second : parent.First;
        Replace(parent.Parent, parent, sibling);
        target.Parent = null;
        return true;
    }

    private void Replace(SplitNode? parent, LayoutNode oldNode, LayoutNode newNode)
    {
        newNode.Parent = parent;
        if (parent is null) Root = newNode;
        else if (parent.First == oldNode) parent.First = newNode;
        else parent.Second = newNode;
    }

    /// <summary>ペイン順で delta 個先のペイン ID（端は循環）。存在しないペインなら先頭。</summary>
    public int NextPane(int paneId, int delta)
    {
        var ids = PaneIds;
        int idx = ids.ToList().IndexOf(paneId);
        if (idx < 0) return ids[0];
        int n = ids.Count;
        return ids[((idx + delta) % n + n) % n];
    }

    /// <summary>分割比率を設定する（MinRatio..MaxRatio に丸める）。</summary>
    public static void SetRatio(SplitNode split, double ratio)
        => split.Ratio = Math.Clamp(double.IsFinite(ratio) ? ratio : 0.5, MinRatio, MaxRatio);

    /// <summary>スプリッター1本分の配置。Bounds はその分割ノードが占める領域（ドラッグ量→比率の換算用）。</summary>
    public readonly record struct SplitterRect(SplitNode Split, Rect Rect, Rect Bounds);

    /// <summary>bounds 内の各ペインとスプリッターの矩形を計算する。スプリッターは thickness の幅を占める。</summary>
    public (Dictionary<int, Rect> Panes, List<SplitterRect> Splitters) Compute(Rect bounds, double thickness)
    {
        var panes = new Dictionary<int, Rect>();
        var splitters = new List<SplitterRect>();
        Layout(Root, bounds, thickness, panes, splitters);
        return (panes, splitters);
    }

    private static void Layout(LayoutNode node, Rect r, double t, Dictionary<int, Rect> panes, List<SplitterRect> splitters)
    {
        if (node is PaneNode p) { panes[p.Id] = r; return; }
        var s = (SplitNode)node;
        if (s.Direction == SplitDirection.Horizontal)
        {
            double avail = Math.Max(0, r.Width - t);
            double first = Math.Round(avail * s.Ratio);
            var a = new Rect(r.X, r.Y, first, r.Height);
            var bar = new Rect(r.X + first, r.Y, Math.Min(t, r.Width), r.Height);
            var b = new Rect(bar.Right, r.Y, Math.Max(0, avail - first), r.Height);
            splitters.Add(new SplitterRect(s, bar, r));
            Layout(s.First, a, t, panes, splitters);
            Layout(s.Second, b, t, panes, splitters);
        }
        else
        {
            double avail = Math.Max(0, r.Height - t);
            double first = Math.Round(avail * s.Ratio);
            var a = new Rect(r.X, r.Y, r.Width, first);
            var bar = new Rect(r.X, r.Y + first, r.Width, Math.Min(t, r.Height));
            var b = new Rect(r.X, bar.Bottom, r.Width, Math.Max(0, avail - first));
            splitters.Add(new SplitterRect(s, bar, r));
            Layout(s.First, a, t, panes, splitters);
            Layout(s.Second, b, t, panes, splitters);
        }
    }

    /// <summary>スプリッターを delta（ピクセル）動かしたときの新しい比率。</summary>
    public static double RatioAfterDrag(SplitterRect splitter, double delta, double thickness)
    {
        bool horizontal = splitter.Split.Direction == SplitDirection.Horizontal;
        double avail = (horizontal ? splitter.Bounds.Width : splitter.Bounds.Height) - thickness;
        if (avail <= 0) return splitter.Split.Ratio;
        double first = (horizontal ? splitter.Rect.X - splitter.Bounds.X : splitter.Rect.Y - splitter.Bounds.Y) + delta;
        return Math.Clamp(first / avail, MinRatio, MaxRatio);
    }

    // ── 保存形式 ──
    // 葉は "P"、分割は "H0.5(子,子)" / "V0.5(子,子)"。葉の並びは PaneIds の順で、セッションの保存先と対応する。

    public string Serialize()
    {
        var sb = new StringBuilder();
        Write(Root, sb);
        return sb.ToString();
    }

    private static void Write(LayoutNode node, StringBuilder sb)
    {
        if (node is PaneNode) { sb.Append('P'); return; }
        var s = (SplitNode)node;
        sb.Append(s.Direction == SplitDirection.Horizontal ? 'H' : 'V');
        sb.Append(s.Ratio.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append('(');
        Write(s.First, sb);
        sb.Append(',');
        Write(s.Second, sb);
        sb.Append(')');
    }

    /// <summary>Serialize の文字列から復元する。壊れた入力なら null（呼び出し側は既定の1ペインで続行する）。</summary>
    public static PaneLayout? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var layout = new PaneLayout();
        layout._nextId = 0;
        int pos = 0;
        var root = layout.ParseNode(text, ref pos, depth: 0);
        if (root is null || pos != text.Length) return null;
        root.Parent = null;
        layout.Root = root;
        return layout;
    }

    private LayoutNode? ParseNode(string s, ref int pos, int depth)
    {
        // 異常に深い入力でスタックを使い切らないよう制限する（実用上ここまで分割することはない）
        if (pos >= s.Length || depth > 32) return null;
        char c = s[pos];
        if (c == 'P') { pos++; return new PaneNode(_nextId++); }
        if (c != 'H' && c != 'V') return null;
        pos++;
        int start = pos;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
        if (!double.TryParse(s.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
            return null;
        if (pos >= s.Length || s[pos++] != '(') return null;
        var first = ParseNode(s, ref pos, depth + 1);
        if (first is null || pos >= s.Length || s[pos++] != ',') return null;
        var second = ParseNode(s, ref pos, depth + 1);
        if (second is null || pos >= s.Length || s[pos++] != ')') return null;
        return new SplitNode(c == 'H' ? SplitDirection.Horizontal : SplitDirection.Vertical, first, second,
            Math.Clamp(ratio, MinRatio, MaxRatio));
    }
}
