using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace RdpManager.Services;

/// <summary>ライト/ダークのテーマ適用。主要サーフェスのブラシリソースを差し替える。</summary>
public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        var res = Application.Current.Resources;
        if (dark)
        {
            res["WindowBg"] = Brush("#1E1E1E");
            res["PanelBg"] = Brush("#252526");
            res["TextFg"] = Brush("#ECECEC");
        }
        else
        {
            res["WindowBg"] = Brush("#FFFFFF");
            res["PanelBg"] = Brush("#FAFAFA");
            res["TextFg"] = Brush("#222222");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
