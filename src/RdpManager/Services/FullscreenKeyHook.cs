using System.Runtime.InteropServices;
using RdpManager.Common;

namespace RdpManager.Services;

/// <summary>RegisterHotKey と同じ形式（Alt=1 / Ctrl=2 / Shift=4 / Win=8、0x4000=NOREPEAT）のキー割り当て。</summary>
public readonly record struct HotkeyBinding(uint Modifiers, uint Vk);

/// <summary>
/// 全画面中の RDP セッションにフォーカスがあると、コントロールの低レベルキーフックが RegisterHotKey より先に
/// キーを奪い、アプリのホットキー（Ctrl+Alt+0 など）が効かなくなる。それを避けるため、全画面の間だけ
/// アプリ側にも WH_KEYBOARD_LL フックを入れ、登録済みホットキーに一致したら WM_HOTKEY を自分で投げて握りつぶす。
/// 低レベルフックは後から入れたものが先に呼ばれるため、コントロールがフックを入れた後に <see cref="Install"/> し直すこと。
/// </summary>
public sealed class FullscreenKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);

    private readonly IntPtr _hwnd;
    private readonly IReadOnlyDictionary<int, HotkeyBinding> _bindings;
    private readonly HookProc _proc; // GC で回収されないよう保持する
    private readonly HashSet<uint> _swallowed = new(); // 押下を握りつぶし中のキー（対応するキーアップも握りつぶす）
    // 修飾キーの押下状態。後段（RDP コントロール）のフックが修飾キーを握りつぶすと GetAsyncKeyState が
    // 更新されないため、このフックが見たイベントから追跡する
    private readonly ModifierTracker _modifiers = new();
    private IntPtr _hook;

    public FullscreenKeyHook(IntPtr hwnd, IReadOnlyDictionary<int, HotkeyBinding> bindings)
    {
        _hwnd = hwnd;
        _bindings = bindings;
        _proc = OnKey;
    }

    /// <summary>フックを（再）登録する。既に登録済みなら入れ直して、チェーンの先頭に戻す。</summary>
    public void Install()
    {
        Uninstall();
        // 導入前に押された修飾キーはイベントを見ていないため、導入時点の非同期キー状態で初期化する
        _modifiers.Reset(vk => (GetAsyncKeyState((int)vk) & 0x8000) != 0);
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Logger.Warn($"Fullscreen key hook install failed: {Marshal.GetLastWin32Error()}");
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowed.Clear();
    }

    public void Dispose() => Uninstall();

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var vk = (uint)Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            var scanCode = (uint)Marshal.ReadInt32(lParam, 4); // KBDLLHOOKSTRUCT.scanCode
            bool keyUp = msg is WmKeyUp or WmSysKeyUp;
            // 修飾キーは状態を記録するだけで握りつぶさない（リモートへもそのまま流す）
            if (_modifiers.Update(vk, scanCode, isDown: !keyUp))
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            if (keyUp)
            {
                if (_swallowed.Remove(vk)) return (IntPtr)1;
            }
            else if (msg is WmKeyDown or WmSysKeyDown && TryMatch(vk, out var id, out var binding))
            {
                bool repeat = !_swallowed.Add(vk);
                if (!(repeat && (binding.Modifiers & ModNoRepeat) != 0))
                    PostMessage(_hwnd, WmHotkey, (IntPtr)id, (IntPtr)(((int)vk << 16) | (int)(binding.Modifiers & 0xF)));
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool TryMatch(uint vk, out int id, out HotkeyBinding binding)
    {
        uint mods = _modifiers.Current;
        foreach (var (key, b) in _bindings)
        {
            if (b.Vk == vk && (b.Modifiers & 0xF) == mods) { id = key; binding = b; return true; }
        }
        id = 0; binding = default;
        return false;
    }
}
