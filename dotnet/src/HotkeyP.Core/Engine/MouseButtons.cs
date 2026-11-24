// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using HotkeyP.Input;

namespace HotkeyP.Engine;

/// <summary>
/// Mouse hotkeys store pressed buttons in <c>Hotkey.ScanCode</c>. The current format is a negative number holding up to
/// seven 4-bit button ids (oldest in the high nibbles, unused nibbles are 0xF). Old files used a positive bitmask.
/// </summary>
public static class MouseButtons
{
    public const int Empty = -1;
    const int WheelUpBit = 0x40000000, WheelDownBit = 0x20000000, WheelRightBit = 0x10000000, WheelLeftBit = 0x8000000;

    static int Find(int which, int seq)
    {
        int i;
        for (i = 24; i >= 0; i -= 4)
            if (((seq >> i) & 15) == which) break;
        return i;
    }

    public static bool Contains(int seq, int which) => Find(which, seq) >= 0;

    public static int Up(int which, int seq)
    {
        int i = Find(which, seq);
        if (i < 0) return seq;
        int mask = (1 << i) - 1;
        return (seq & mask) | ((seq >> 4) & ~mask);
    }

    public static int Down(int which, int seq)
    {
        if ((seq >> 28) != -1) return seq; // too many buttons
        seq = Up(which, seq);
        return (seq << 4) | which;
    }

    /// <summary>Buttons in the order they were pressed.</summary>
    public static IEnumerable<int> Enumerate(int seq)
    {
        seq = ToSequence(seq);
        for (int i = 24; i >= 0; i -= 4)
        {
            int b = (seq >> i) & 15;
            if (b != 15) yield return b;
        }
    }

    public static int FromButtons(IEnumerable<MouseButton> buttons)
    {
        int seq = Empty;
        foreach (var b in buttons) seq = Down((int)b, seq);
        return seq;
    }

    public static int ToBitMask(int seq)
    {
        if (seq >= 0) return seq;
        int m = 0;
        for (int i = 24; i >= 0; i -= 4) m |= 1 << ((seq >> i) & 15);
        if ((m & (1 << (int)MouseButton.WheelUp)) != 0) m ^= WheelUpBit | (1 << (int)MouseButton.WheelUp);
        if ((m & (1 << (int)MouseButton.WheelDown)) != 0) m ^= WheelDownBit | (1 << (int)MouseButton.WheelDown);
        if ((m & (1 << (int)MouseButton.WheelRight)) != 0) m ^= WheelRightBit | (1 << (int)MouseButton.WheelRight);
        if ((m & (1 << (int)MouseButton.WheelLeft)) != 0) m ^= WheelLeftBit | (1 << (int)MouseButton.WheelLeft);
        return m & ~(1 << 15);
    }

    public static int ToSequence(int mask)
    {
        if (mask < 0) return mask;
        int a = Empty;
        for (int i = 0; i < 16; i++) if ((mask & (1 << i)) != 0) a = Down(i, a);
        if ((mask & WheelUpBit) != 0) a = Down((int)MouseButton.WheelUp, a);
        if ((mask & WheelDownBit) != 0) a = Down((int)MouseButton.WheelDown, a);
        if ((mask & WheelRightBit) != 0) a = Down((int)MouseButton.WheelRight, a);
        if ((mask & WheelLeftBit) != 0) a = Down((int)MouseButton.WheelLeft, a);
        return a;
    }

    /// <summary>Hotkey value <paramref name="hotkey"/> matches the pressed sequence <paramref name="pressed"/>.</summary>
    public static bool Matches(int hotkey, int pressed) => hotkey >= 0 ? hotkey == ToBitMask(pressed) : hotkey == pressed;

    /// <summary>The pressed buttons are the beginning of the hotkey's buttons, so more buttons may follow.</summary>
    public static bool IsPrefix(int hotkey, int pressed)
    {
        if (hotkey >= 0)
        {
            int m = ToBitMask(pressed);
            return (hotkey | m) == hotkey && hotkey != m;
        }
        for (int c = hotkey >> 4; c != -1; c >>= 4)
            if (c == pressed) return true;
        return false;
    }

    public static string Name(int b) => b switch
    {
        (int)MouseButton.Left => "Left",
        (int)MouseButton.Right => "Right",
        (int)MouseButton.Middle => "Middle",
        (int)MouseButton.X1 => "X1",
        (int)MouseButton.X2 => "X2",
        (int)MouseButton.WheelUp => "Wheel up",
        (int)MouseButton.WheelDown => "Wheel down",
        (int)MouseButton.WheelLeft => "Wheel left",
        (int)MouseButton.WheelRight => "Wheel right",
        _ => b.ToString(),
    };
}
