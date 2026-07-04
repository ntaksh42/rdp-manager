using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace RdpManager.Views;

/// <summary>グローバルホットキーの組み合わせをユーザーに押させてキャプチャする小さなモーダルダイアログ。</summary>
public partial class HotkeyCaptureDialog : Window
{
    // Win32 RegisterHotKey の MOD_* 値
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    /// <summary>確定した修飾キー（MOD_* のビット和）。</summary>
    public uint Modifiers { get; private set; }
    /// <summary>確定した非修飾キーの仮想キーコード。</summary>
    public uint Key { get; private set; }
    /// <summary>表示用文字列（例: "Ctrl+Alt+Home"）。</summary>
    public string DisplayText { get; private set; } = "";

    public HotkeyCaptureDialog()
    {
        InitializeComponent();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == WpfKey.System ? e.SystemKey : e.Key;

        if (key == WpfKey.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        var mods = CurrentModifiers();

        if (IsModifierKey(key))
        {
            // 修飾キーのみの押下ではプレビューだけ更新し、確定はさせない
            PreviewText.Text = mods == 0 ? "…" : string.Join("+", ModifierParts(mods));
            OkButton.IsEnabled = false;
            return;
        }

        if (mods == 0)
        {
            // グローバルホットキーは修飾キーなしでは登録できないため受理しない
            PreviewText.Text = key.ToString();
            OkButton.IsEnabled = false;
            return;
        }

        Modifiers = mods;
        Key = (uint)KeyInterop.VirtualKeyFromKey(key);
        DisplayText = BuildDisplayText(Modifiers, Key);
        PreviewText.Text = DisplayText;
        OkButton.IsEnabled = true;
    }

    private static bool IsModifierKey(WpfKey key) =>
        key is WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt
            or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin or WpfKey.System;

    private static uint CurrentModifiers()
    {
        uint mods = 0;
        var wpfMods = Keyboard.Modifiers;
        if ((wpfMods & ModifierKeys.Control) != 0) mods |= ModControl;
        if ((wpfMods & ModifierKeys.Alt) != 0) mods |= ModAlt;
        if ((wpfMods & ModifierKeys.Shift) != 0) mods |= ModShift;
        if (Keyboard.IsKeyDown(WpfKey.LWin) || Keyboard.IsKeyDown(WpfKey.RWin)) mods |= ModWin;
        return mods;
    }

    private static List<string> ModifierParts(uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        return parts;
    }

    /// <summary>ホットキーの表示文字列（例: "Ctrl+Alt+Home"）を組み立てる。メニューの InputGestureText 表示にも使う。</summary>
    public static string BuildDisplayText(uint modifiers, uint vkKey)
    {
        var parts = ModifierParts(modifiers);
        parts.Add(KeyInterop.KeyFromVirtualKey((int)vkKey).ToString());
        return string.Join("+", parts);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
