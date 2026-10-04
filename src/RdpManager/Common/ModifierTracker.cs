namespace RdpManager.Common;

/// <summary>
/// 低レベルキーボードフックが受け取ったキーイベントから修飾キー（RegisterHotKey 形式: Alt=1 / Ctrl=2 / Shift=4 / Win=8）の
/// 押下状態を追跡する。フック内では GetAsyncKeyState が当てにならない（後段のフックが修飾キーを握りつぶすと
/// 非同期キー状態が更新されない）ため、自前で数える。左右のキーは別々に追跡し、片方を離しても他方が押されていれば押下扱い。
/// </summary>
public sealed class ModifierTracker
{
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    // 押下中の左右別修飾キー（VK_LSHIFT 等）
    private readonly HashSet<uint> _down = new();

    /// <summary>現在押されている修飾キー（MOD_* のビット和）。</summary>
    public uint Current
    {
        get
        {
            uint m = 0;
            foreach (var vk in _down) m |= ModOf(vk);
            return m;
        }
    }

    /// <summary>フック導入時など、イベントを見ていない間の状態で初期化する。</summary>
    /// <param name="isDown">左右別の仮想キーコードを受け取り押下中かを返す。</param>
    public void Reset(Func<uint, bool> isDown)
    {
        _down.Clear();
        foreach (var vk in SidedKeys)
            if (isDown(vk)) _down.Add(vk);
    }

    // AltGr 配列で AltGr（右 Alt）を押すと OS が合成する左 Ctrl のスキャンコード。
    // これを Ctrl と数えると AltGr+7 等の文字入力（独語配列の { など）が Ctrl+Alt+7 のホットキーとして奪われる
    private const uint AltGrFakeCtrlScanCode = 0x21D;

    /// <summary>キーイベントを反映する。修飾キーなら true（AltGr が合成した左 Ctrl は状態に数えないが true を返す）。</summary>
    public bool Update(uint vk, uint scanCode, bool isDown)
    {
        vk = Sided(vk);
        if (ModOf(vk) == 0) return false;
        if (vk == 0xA2 && scanCode == AltGrFakeCtrlScanCode) return true;
        if (isDown) _down.Add(vk);
        else _down.Remove(vk);
        return true;
    }

    private static readonly uint[] SidedKeys = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };

    // 左右の区別なし VK（VK_SHIFT 等）は左として扱う（LL フックには通常左右別で届く）
    private static uint Sided(uint vk) => vk switch
    {
        0x10 => 0xA0, // VK_SHIFT → VK_LSHIFT
        0x11 => 0xA2, // VK_CONTROL → VK_LCONTROL
        0x12 => 0xA4, // VK_MENU → VK_LMENU
        _ => vk
    };

    private static uint ModOf(uint vk) => vk switch
    {
        0xA0 or 0xA1 => ModShift,
        0xA2 or 0xA3 => ModControl,
        0xA4 or 0xA5 => ModAlt,
        0x5B or 0x5C => ModWin,
        _ => 0
    };
}
