using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using RdpManager.Services;
using RdpManager.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    // ── 単一インスタンス化 ──
    // 2つ目のインスタンスは RegisterHotKey が全て失敗して F11 等が無言で効かなくなるため、
    // 既存インスタンスのウィンドウをアクティブ化して自分は終了する
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SwRestore = 9;

    private static System.Threading.Mutex? _singleInstanceMutex;

    private static void ActivateExistingInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(current.ProcessName))
        {
            if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
            {
                if (IsIconic(p.MainWindowHandle)) ShowWindowAsync(p.MainWindowHandle, SwRestore);
                SetForegroundWindow(p.MainWindowHandle);
                break;
            }
        }
    }

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

        _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\rdpmanager-single-instance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();
        Logger.Enabled = Settings.EnableLogging;
        Logger.Info("rdpmanager started.");
        ThemeManager.Apply(Settings.DarkMode);

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

    protected override void OnExit(ExitEventArgs e)
    {
        // 30秒の遅延クリーンアップ待たず、外部起動で残った TERMSRV 資格情報・一時 .rdp ファイルを片付ける
        RdpLauncher.CleanupAllPending();
        base.OnExit(e);
    }

    private static readonly TimeSpan ErrorDialogThrottle = TimeSpan.FromSeconds(30);
    private DateTime? _lastErrorShown;

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 表示スロットル中でも痕跡が残るよう、内容は常にログへ記録する
        Logger.Warn(e.Exception.ToString());
        if (_selfTest)
        {
            if (!System.IO.File.Exists(SelfTestLog))
                System.IO.File.WriteAllText(SelfTestLog, $"DISPATCHER: {e.Exception.Message}");
            e.Handled = true;
            Shutdown(10);
            return;
        }
        // 同種の例外が連発してもダイアログを大量に出さない（前回表示から一定時間は抑制するが、以後のエラーは無言で消さない）
        var now = DateTime.UtcNow;
        if (_lastErrorShown is null || now - _lastErrorShown.Value >= ErrorDialogThrottle)
        {
            _lastErrorShown = now;
            MessageBox.Show(
                "An unexpected error occurred, but the app will continue.\n\n" + e.Exception.Message,
                "rdpmanager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }
}
