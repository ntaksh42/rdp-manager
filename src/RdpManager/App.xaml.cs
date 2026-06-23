using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 埋め込み RDP コントロール起因の例外でアプリ全体が落ちないようにする安全網
        DispatcherUnhandledException += OnUnhandled;

        new MainWindow().Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "予期しないエラーが発生しましたが、アプリは継続します。\n\n" + e.Exception.Message,
            "RdpManager", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
