using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class App : Application
{
    private static readonly string SelfTestLog =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rdpmanager_selftest.txt");
    private bool _selfTest;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 埋め込み RDP コントロール起因の例外でアプリ全体が落ちないようにする安全網
        DispatcherUnhandledException += OnUnhandled;

        if (e.Args.Contains("--selftest"))
        {
            _selfTest = true;
            RunSelfTest();
            return;
        }

        new MainWindow().Show();
    }

    private void RunSelfTest()
    {
        var host = new Controls.RdpClientHost();
        var win = new Window
        {
            Width = 500, Height = 350, Title = "selftest",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new System.Windows.Forms.Integration.WindowsFormsHost { Child = host }
        };
        win.ContentRendered += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                string msg; int code;
                try { msg = host.SelfCheck(); code = msg.StartsWith("OK") ? 0 : 2; }
                catch (Exception ex) { msg = $"NG: {ex.GetType().Name}: {ex.Message}"; code = 3; }
                System.IO.File.WriteAllText(SelfTestLog, msg);
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Shutdown(code)));
            }));
        win.Show();
    }

    private bool _errorShown;

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_selfTest)
        {
            if (!System.IO.File.Exists(SelfTestLog))
                System.IO.File.WriteAllText(SelfTestLog, $"DISPATCHER: {e.Exception.Message}");
            e.Handled = true;
            Shutdown(10);
            return;
        }
        // 同種の例外が連発してもダイアログを大量に出さない（最初の1回だけ通知）
        if (!_errorShown)
        {
            _errorShown = true;
            MessageBox.Show(
                "予期しないエラーが発生しましたが、アプリは継続します。\n\n" + e.Exception.Message,
                "RdpManager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }
}
