using System.Windows;
using Point = System.Windows.Point;

namespace RdpManager.Common;

/// <summary>タブをペイン本体へドロップしたときの位置。Center はそのペインへ移動、それ以外はその方向へ分割して置く。</summary>
public enum DockZone { Center, Left, Right, Top, Bottom }

/// <summary>タブのドラッグ＆ドロップによるドッキングの判定（純ロジック）。</summary>
public static class DockHitTest
{
    /// <summary>端からこの割合以内なら分割、それより内側なら中央（移動）とみなす。</summary>
    public const double EdgeFraction = 0.25;

    /// <summary>ペイン本体の矩形 pane 内の点 p がどのゾーンか。範囲外や大きさ0なら null。</summary>
    public static DockZone? ZoneAt(Rect pane, Point p)
    {
        if (pane.Width <= 0 || pane.Height <= 0 || !pane.Contains(p)) return null;
        double left = (p.X - pane.X) / pane.Width, top = (p.Y - pane.Y) / pane.Height;
        double right = 1 - left, bottom = 1 - top;
        double min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (min >= EdgeFraction) return DockZone.Center;
        if (min == left) return DockZone.Left;
        if (min == right) return DockZone.Right;
        return min == top ? DockZone.Top : DockZone.Bottom;
    }

    /// <summary>ドロップ後に新ペインが占める領域（ガイド表示用）。Center はペイン全体。</summary>
    public static Rect PreviewRect(Rect pane, DockZone zone) => zone switch
    {
        DockZone.Left => new Rect(pane.X, pane.Y, pane.Width / 2, pane.Height),
        DockZone.Right => new Rect(pane.X + pane.Width / 2, pane.Y, pane.Width / 2, pane.Height),
        DockZone.Top => new Rect(pane.X, pane.Y, pane.Width, pane.Height / 2),
        DockZone.Bottom => new Rect(pane.X, pane.Y + pane.Height / 2, pane.Width, pane.Height / 2),
        _ => pane
    };

    /// <summary>分割ゾーンに対応する分割方向と、新ペインを前側（左/上）に作るか。Center は null。</summary>
    public static (SplitDirection Direction, bool Before)? SplitOf(DockZone zone) => zone switch
    {
        DockZone.Left => (SplitDirection.Horizontal, true),
        DockZone.Right => (SplitDirection.Horizontal, false),
        DockZone.Top => (SplitDirection.Vertical, true),
        DockZone.Bottom => (SplitDirection.Vertical, false),
        _ => null
    };

    /// <summary>タブ列へドロップしたときの挿入位置。tabCenters はドラッグ中のタブを除いた各タブ中心の X（並び順）。</summary>
    public static int InsertIndex(IReadOnlyList<double> tabCenters, double x)
        => tabCenters.Count(c => c < x);
}
