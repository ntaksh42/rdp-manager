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
            res["SubtleFg"] = Brush("#8A8A8A");
            res["BarBg"] = Brush("#2D2D30");
            res["SplitterBg"] = Brush("#3F3F46");
            res["ControlBg"] = Brush("#2D2D30");
            res["ControlBorder"] = Brush("#3F3F46");
            res["TabBg"] = Brush("#2D2D30");
            res["TabSelectedBg"] = Brush("#3E3E42");
            res["HoverBg"] = Brush("#33FFFFFF");
            res["PressedBg"] = Brush("#4DFFFFFF");
        }
        else
        {
            res["WindowBg"] = Brush("#FFFFFF");
            res["PanelBg"] = Brush("#FAFAFA");
            res["TextFg"] = Brush("#222222");
            res["SubtleFg"] = Brush("#9E9E9E");
            res["BarBg"] = Brush("#F0F0F0");
            res["SplitterBg"] = Brush("#E0E0E0");
            res["ControlBg"] = Brush("#FFFFFF");
            res["ControlBorder"] = Brush("#CCCCCC");
            res["TabBg"] = Brush("#ECECEC");
            res["TabSelectedBg"] = Brush("#FFFFFF");
            res["HoverBg"] = Brush("#1A000000");
            res["PressedBg"] = Brush("#33000000");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
