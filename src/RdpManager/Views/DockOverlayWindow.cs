using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace RdpManager.Views;

/// <summary>
/// タブのドラッグ中にドロップ先のガイドを描く透明ウィンドウ。WPF の要素は埋め込み RDP の HWND より
/// 上に描けない（airspace）ため、セッション一覧と同じく所有ウィンドウを重ねて描く。
/// マウスもアクティブ化も受けない（ドラッグ中のマウスはメインウィンドウ側がキャプチャしている）。
/// </summary>
public sealed class DockOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly Border _guide = new() { CornerRadius = new CornerRadius(3), Opacity = 0.4 };

    public DockOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _guide.SetResourceReference(Border.BackgroundProperty, "AccentBg");
        var canvas = new Canvas();
        canvas.Children.Add(_guide);
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE,
                GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    /// <summary>element の上に重ねる。ガイドの座標は element の左上が原点。</summary>
    public void PlaceOver(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        if (PresentationSource.FromVisual(element)?.CompositionTarget is { } target)
            topLeft = target.TransformFromDevice.Transform(topLeft);
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = element.ActualWidth;
        Height = element.ActualHeight;
    }

    /// <summary>ガイド矩形を表示する（null なら隠す）。</summary>
    public void ShowGuide(Rect? rect)
    {
        if (rect is not { } r) { _guide.Visibility = Visibility.Collapsed; return; }
        Canvas.SetLeft(_guide, r.X);
        Canvas.SetTop(_guide, r.Y);
        _guide.Width = r.Width;
        _guide.Height = r.Height;
        _guide.Visibility = Visibility.Visible;
    }
}
