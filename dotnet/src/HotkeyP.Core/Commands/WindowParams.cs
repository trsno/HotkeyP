// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Commands;

/// <summary>Parameter parsing of the window commands, kept identical to the C++ version so old .htk files work.</summary>
internal static class WindowParams
{
    /// <summary>Process priority names in the order of the C++ priorTab (0 = idle ... 5 = above normal).</summary>
    public static readonly string[] PriorityNames = ["idle", "normal", "high", "realtime", "below normal", "above normal"];

    /// <summary>Priority classes matching <see cref="PriorityNames"/> (C++ priorCnst).</summary>
    public static readonly uint[] PriorityClasses = [0x40, 0x20, 0x80, 0x100, 0x4000, 0x8000];

    /// <summary>
    /// Port of C++ strToIntStr: a leading integer (strtol, base 10) and the rest of the text after it with leading
    /// spaces skipped. Without a number the value is 0 and the rest is the whole text. When the number is followed
    /// by ':' (e.g. a drive or "R:Microphone") the value is 0 and the rest is the whole, unchanged text.
    /// </summary>
    public static (int Value, string Remainder) StrToIntStr(string param)
    {
        int result = Strtol(param, out int end);
        if (end < param.Length && param[end] == ':') return (0, param);
        while (end < param.Length && param[end] == ' ') end++;
        return (result, param[end..]);
    }

    /// <summary>
    /// Port of C strtol with base 10 (32-bit long): skips white space, optional sign, digits. <paramref name="end"/> is
    /// the index after the number, or 0 when there is no number. Overflow gives int.MinValue/int.MaxValue.
    /// </summary>
    public static int Strtol(string s, out int end)
    {
        int i = 0;
        while (i < s.Length && IsCSpace(s[i])) i++;
        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { negative = s[i] == '-'; i++; }
        int digitsStart = i;
        long value = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            if (value <= int.MaxValue + 1L) value = value * 10 + (s[i] - '0');
            i++;
        }
        if (i == digitsStart)
        {
            end = 0;
            return 0;
        }
        end = i;
        if (negative) value = -value;
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    static bool IsCSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>
    /// Priority index of command 20 "Process priority": empty means normal; a priority name (case-insensitive,
    /// the whole parameter) or else the leading number; the result is clamped to 0..5 like the C++ priority().
    /// </summary>
    public static int ParsePriority(string param)
    {
        int p;
        if (param.Length == 0) p = 1;
        else
        {
            p = StrToIntStr(param).Value;
            for (int i = 0; i < PriorityNames.Length; i++)
                if (string.Equals(PriorityNames[i], param, StringComparison.OrdinalIgnoreCase)) p = i;
        }
        return Math.Clamp(p, 0, PriorityNames.Length - 1);
    }

    /// <summary>Horizontal and vertical action of the move commands 28..36: 0 = keep, 1 = left/top, 2 = right/bottom, 3 = center.</summary>
    public static (int X, int Y)? MoveActions(int cmd) => cmd switch
    {
        28 => (3, 3),
        29 => (1, 2),
        30 => (0, 2),
        31 => (2, 2),
        32 => (1, 0),
        33 => (2, 0),
        34 => (1, 1),
        35 => (0, 1),
        36 => (2, 1),
        _ => null,
    };

    /// <summary>New window position computed like the C++ moveCmd. Rectangles are (left, top, right, bottom).</summary>
    public static (int X, int Y) MovePosition(
        (int Left, int Top, int Right, int Bottom) desk, (int Left, int Top, int Right, int Bottom) win, int actionX, int actionY)
    {
        int x = actionX switch
        {
            1 => desk.Left,
            2 => desk.Right - win.Right + win.Left,
            3 => (desk.Right + desk.Left - win.Right + win.Left) >> 1,
            _ => win.Left,
        };
        int y = actionY switch
        {
            1 => desk.Top,
            2 => desk.Bottom - win.Bottom + win.Top,
            3 => (desk.Bottom + desk.Top - win.Bottom + win.Top) >> 1,
            _ => win.Top,
        };
        return (x, y);
    }

    /// <summary>Command id of "Send window command" / "Command to active window": strtol of the first word, 0 if not a number.</summary>
    public static int WindowCommandId(string first) => Strtol(first, out _);

    /// <summary>New opacity for "Opacity +" (<paramref name="increase"/>) and "Opacity -": the step defaults to 10, the result is 0..255.</summary>
    public static int ChangeOpacity(int current, int step, bool increase)
    {
        if (step == 0) step = 10;
        long v = increase ? (long)current + step : (long)current - step;
        return (int)Math.Clamp(v, 0, 255);
    }
}
