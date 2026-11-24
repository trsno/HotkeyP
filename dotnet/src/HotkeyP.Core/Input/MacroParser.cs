// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Input;

public enum KeyAction { Press, Up, Down }

public enum MouseButton { Left = 0, Right = 1, Middle = 4, X1 = 5, X2 = 6, WheelLeft = 11, WheelRight = 12, WheelDown = 13, WheelUp = 14 }

public abstract record MacroStep;
/// <summary>Virtual key, written as \NAME or \xHH in the macro.</summary>
public sealed record KeyStep(int VKey, KeyAction Action) : MacroStep;
/// <summary>Plain character; translated with the keyboard layout of the target window when played.</summary>
public sealed record CharStep(char Char, KeyAction Action) : MacroStep;
public sealed record MouseStep(MouseButton Button, KeyAction Action) : MacroStep;
/// <summary>\WAIT: wait until the user releases all keys.</summary>
public sealed record WaitForKeysStep : MacroStep;
/// <summary>\SHOW: bring the target window to the foreground.</summary>
public sealed record ShowStep : MacroStep;
public sealed record SleepStep(int Milliseconds) : MacroStep;
/// <summary>\REP: restore modifier keys the user is still holding after the macro.</summary>
public sealed record RepeatStep : MacroStep;
public sealed record DoubleClickStep : MacroStep;

public sealed class MacroSyntaxException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}

/// <summary>
/// Parser for the HotkeyP macro language. Plain characters are typed; a backslash starts a key name
/// (\ENTER, \F5, \^ for Ctrl, \A, \x41), optionally followed by "up" or "down" and a space or dot separator.
/// A double backslash types a backslash.
/// </summary>
public static class MacroParser
{
    const int Wait = 100000, Show = 100001, Sleep = 100002, Rep = 100003, DoubleClick = 100004, Mouse = 100100;

    /// <summary>Key names in matching order (longer names first where one is a prefix of another).</summary>
    internal static readonly (string Name, int VKey)[] KeyNames =
    [
        ("ESCAPE", 0x1B), ("ESC", 0x1B), ("F10", 0x79), ("F11", 0x7A), ("F12", 0x7B),
        ("F13", 0x7C), ("F14", 0x7D), ("F15", 0x7E), ("F16", 0x7F), ("F17", 0x80),
        ("F18", 0x81), ("F19", 0x82), ("F20", 0x83), ("F21", 0x84), ("F22", 0x85),
        ("F23", 0x86), ("F24", 0x87), ("F1", 0x70), ("F2", 0x71),
        ("F3", 0x72), ("F4", 0x73), ("F5", 0x74), ("F6", 0x75), ("F7", 0x76),
        ("F8", 0x77), ("F9", 0x78), ("PRINTSCREEN", 0x2C),
        ("PRINTSCRN", 0x2C), ("SCROLLLOCK", 0x91),
        ("PAUSE", 0x13), ("BREAK", 0x03), ("CLEAR", 0x0C),
        ("INSERT", 0x2D), ("INS", 0x2D), ("DELETE", 0x2E),
        ("DEL", 0x2E), ("HOME", 0x24), ("END", 0x23),
        ("PAGEUP", 0x21), ("PAGEDOWN", 0x22), ("LEFT", 0x25),
        ("RIGHT", 0x27), ("UP", 0x26), ("DOWN", 0x28), ("TAB", 0x09),
        ("CAPSLOCK", 0x14), ("CAPS", 0x14), ("BACKSPACE", 0x08),
        ("BS", 0x08), ("ENTER", 0x0D), ("SPACE", 0x20),
        ("SHIFT", 0x10), ("CONTROL", 0x11), ("CTRL", 0x11),
        ("RSHIFT", 0xA1), ("RCONTROL", 0xA3), ("RCTRL", 0xA3),
        ("^", 0x11), ("ALT", 0x12), ("RALT", 0xA5), ("MENU", 0x12), ("RMENU", 0xA5),
        ("WIN", 0x5B), ("LWIN", 0x5B), ("RWIN", 0x5C), ("APPS", 0x5D),
        ("NUMLOCK", 0x90), ("DIVIDE", 0x6F), ("MULTIPLY", 0x6A),
        ("SUBTRACT", 0x6D), ("ADD", 0x6B), ("DECIMAL", 0x6E),
        ("NUM0", 0x60), ("NUM1", 0x61), ("NUM2", 0x62),
        ("NUM3", 0x63), ("NUM4", 0x64), ("NUM5", 0x65),
        ("NUM6", 0x66), ("NUM7", 0x67), ("NUM8", 0x68),
        ("NUM9", 0x69), ("BACK", 0xA6),
        ("FORWARD", 0xA7), ("REFRESH", 0xA8),
        ("SEARCH", 0xAA), ("FAVORITES", 0xAB),
        ("BROWSER", 0xAC), ("MUTE", 0xAD),
        ("VOLUME_DOWN", 0xAE), ("VOLUME_UP", 0xAF),
        ("NEXT_TRACK", 0xB0), ("PREV_TRACK", 0xB1),
        ("STOP", 0xB2), ("PLAY_PAUSE", 0xB3), ("MEDIA_SELECT", 0xB5),
        ("MAIL", 0xB4), ("POWER", 0x5F), ("LAUNCH_APP1", 0xB6), ("LAUNCH_APP2", 0xB7),
        ("LBUTTON", Mouse + (int)MouseButton.Left), ("RBUTTON", Mouse + (int)MouseButton.Right),
        ("MBUTTON", Mouse + (int)MouseButton.Middle),
        ("XBUTTON1", Mouse + (int)MouseButton.X1), ("XBUTTON2", Mouse + (int)MouseButton.X2),
        ("WHEELUP", Mouse + (int)MouseButton.WheelUp), ("WHEELDOWN", Mouse + (int)MouseButton.WheelDown),
        ("WHEELRIGHT", Mouse + (int)MouseButton.WheelRight), ("WHEELLEFT", Mouse + (int)MouseButton.WheelLeft),
        ("WAIT", Wait), ("SHOW", Show), ("SLEEP", Sleep), ("REP", Rep),
        ("DOUBLECLICK", DoubleClick),
    ];

    public static List<MacroStep> Parse(string macro)
    {
        var steps = new List<MacroStep>();
        int i = 0;
        while (i < macro.Length)
        {
            if (macro[i] == '\\' && i + 1 < macro.Length && macro[i + 1] != '\\')
            {
                int start = i++;
                int vk = ParseKeyName(macro, ref i, start);
                var action = ParseUpDown(macro, ref i);
                if (vk >= Mouse) steps.Add(new MouseStep((MouseButton)(vk - Mouse), action));
                else switch (vk)
                {
                    case Wait: steps.Add(new WaitForKeysStep()); break;
                    case Show: steps.Add(new ShowStep()); break;
                    case Rep: steps.Add(new RepeatStep()); break;
                    case DoubleClick: steps.Add(new DoubleClickStep()); break;
                    case Sleep: steps.Add(new SleepStep(ParseSleep(macro, ref i))); break;
                    default: steps.Add(new KeyStep(vk, action)); break;
                }
            }
            else
            {
                if (macro[i] == '\\') i++; // "\\" types a backslash
                if (i >= macro.Length) break;
                steps.Add(new CharStep(macro[i++], KeyAction.Press));
            }
        }
        return steps;
    }

    static int ParseKeyName(string s, ref int i, int start)
    {
        if ((s[i] == 'x' || s[i] == 'X') && i + 2 < s.Length && Hex(s[i + 1]) >= 0 && Hex(s[i + 2]) >= 0)
        {
            int vk = Hex(s[i + 1]) * 16 + Hex(s[i + 2]);
            i += 3;
            return vk;
        }
        foreach (var (name, vk) in KeyNames)
        {
            if (string.Compare(s, i, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
                && i + name.Length <= s.Length)
            {
                i += name.Length;
                return vk;
            }
        }
        char c = s[i];
        if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            i++;
            return c;
        }
        int end = i;
        while (end < s.Length && s[end] != ' ' && s[end] != '.' && s[end] != '\\') end++;
        throw new MacroSyntaxException($"Unknown key name: {s[i..end]}", start);
    }

    static KeyAction ParseUpDown(string s, ref int i)
    {
        var action = KeyAction.Press;
        if (string.CompareOrdinal(s, i, "up", 0, 2) == 0) { i += 2; action = KeyAction.Up; }
        else if (string.CompareOrdinal(s, i, "down", 0, 4) == 0) { i += 4; action = KeyAction.Down; }
        if (i < s.Length && (s[i] == ' ' || s[i] == '.')) i++;
        return action;
    }

    /// <summary>\SLEEP n waits n tenths of a second (max 100); \SLEEP alone waits 1 s; \SLEEP 0 waits 10 ms.</summary>
    static int ParseSleep(string s, ref int i)
    {
        int start = i, d = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) d = d * 10 + (s[i++] - '0');
        if (i < s.Length && (s[i] == ' ' || s[i] == '.')) i++;
        if (d == 0) return i == start ? 1000 : 10;
        return Math.Min(d, 100) * 100;
    }

    static int Hex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
