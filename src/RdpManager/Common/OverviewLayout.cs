namespace RdpManager.Common;

/// <summary>
/// セッション一覧（サムネイル表示）のタイル配置計算と表示用の補助関数。
/// 列数を固定せず、件数と表示領域から「タイルが最も大きくなる」列数を選ぶ。
/// </summary>
public static class OverviewLayout
{
    /// <summary>配置結果。TileWidth はタイル全体（画面部分＋枠）の幅、ScreenWidth は画面部分の幅。</summary>
    public readonly record struct Result(int Columns, int Rows, double TileWidth, double ScreenWidth);

    /// <summary>
    /// 件数 count のタイルを availWidth×availHeight に並べる最適な列数とタイル幅を求める。
    /// 画面部分は aspect（幅/高さ）固定で、タイルは画面部分の周囲に chromeWidth/chromeHeight 分の枠（余白・ラベル）を持つ。
    /// どの配置でも minScreenWidth を下回る場合は、その幅を保てる列数で並べる（縦スクロール前提）。
    /// </summary>
    public static Result Compute(int count, double availWidth, double availHeight, double aspect,
                                 double gap, double chromeWidth, double chromeHeight, double minScreenWidth)
    {
        if (count <= 0 || availWidth <= 0 || availHeight <= 0 || aspect <= 0)
            return new Result(1, 0, 0, 0);

        Result best = default;
        for (int cols = 1; cols <= count; cols++)
        {
            int rows = (count + cols - 1) / cols;
            double cellWidth = (availWidth - gap * (cols - 1)) / cols;
            double cellHeight = (availHeight - gap * (rows - 1)) / rows;
            // 画面部分の幅はセル幅とセル高さ（アスペクト比換算）の小さい方で決まる
            double screen = Math.Min(cellWidth - chromeWidth, (cellHeight - chromeHeight) * aspect);
            // 同じ大きさなら列数の少ない配置を優先する（厳密に大きいときだけ更新）
            if (screen > best.ScreenWidth + 0.5)
                best = new Result(cols, rows, screen + chromeWidth, screen);
        }

        if (best.ScreenWidth >= minScreenWidth) return best;

        // 領域に収まりきらない件数: 最小幅を保てる列数で並べ、縦にはみ出す分はスクロールさせる
        int fitCols = (int)Math.Floor((availWidth + gap) / (minScreenWidth + chromeWidth + gap));
        fitCols = Math.Clamp(fitCols, 1, count);
        double width = Math.Max(minScreenWidth, (availWidth - gap * (fitCols - 1)) / fitCols - chromeWidth);
        return new Result(fitCols, (count + fitCols - 1) / fitCols, width + chromeWidth, width);
    }

    /// <summary>スナップショットの古さを表示用文字列にする（数秒以内は "Live"）。</summary>
    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.FromSeconds(3)) return "Live";
        if (age < TimeSpan.FromMinutes(1)) return $"{(int)age.TotalSeconds}s ago";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h ago";
    }

    /// <summary>絞り込み: いずれかのフィールドがクエリを含めば一致（大文字小文字無視・空白で区切った全語が必要）。</summary>
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!fields.Any(f => f?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
                return false;
        }
        return true;
    }
}
