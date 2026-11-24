// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Commands;

/// <summary>A display mode request parsed from the "Desktop resolution" parameter. Zero means "keep the current value".</summary>
internal readonly record struct DisplayMode(uint Width, uint Height, uint Bits, uint Frequency);

/// <summary>Parameter of command 18: "w h bits freq [display][, w2 h2 bits2 freq2]".</summary>
internal sealed record ResolutionRequest(bool Empty, DisplayMode First, uint Display, DisplayMode? Second);

/// <summary>What the "Eject/Close CD" command does after asking the drive with SCSI TEST UNIT READY.</summary>
internal enum CdDoorAction { Eject, Close, MciFallback }

/// <summary>
/// Pure parameter parsing of the system, power and drive commands. It mirrors the C runtime functions the C++ code
/// used (strtol, strtoul), so existing .htk files keep their meaning.
/// </summary>
internal static class SystemParsing
{
    static bool IsCSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>
    /// C strtol with base 10 (32-bit long): skips white space, optional sign, digits. When there are no digits it
    /// returns 0 and leaves <paramref name="pos"/> unchanged. Overflow clamps to int range.
    /// </summary>
    public static int StrToL(string s, ref int pos)
    {
        long v = Parse(s, ref pos, out bool neg, out bool overflow);
        if (overflow) return neg ? int.MinValue : int.MaxValue;
        v = neg ? -v : v;
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    /// <summary>C strtoul with base 10 (32-bit): like <see cref="StrToL"/>, a minus sign negates modulo 2^32.</summary>
    public static uint StrToUL(string s, ref int pos)
    {
        long v = Parse(s, ref pos, out bool neg, out bool overflow);
        if (overflow || v > uint.MaxValue) return uint.MaxValue;
        return neg ? unchecked((uint)-v) : (uint)v;
    }

    static long Parse(string s, ref int pos, out bool neg, out bool overflow)
    {
        neg = false;
        overflow = false;
        int i = pos;
        while (i < s.Length && IsCSpace(s[i])) i++;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        int start = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            if (v <= uint.MaxValue) v = v * 10 + (s[i] - '0');
            i++;
        }
        if (i == start) { neg = false; return 0; } // no conversion: end pointer = beginning
        if (v > uint.MaxValue) overflow = true;
        pos = i;
        return v;
    }

    /// <summary>
    /// C++ strToIntStr: the leading number and the rest of the parameter after spaces. If the number is followed by
    /// ':' (a drive letter or a time), the whole parameter is the rest and the number is 0.
    /// </summary>
    public static int StrToIntStr(string param, out string rest)
    {
        int pos = 0;
        int result = StrToL(param, ref pos);
        if (pos < param.Length && param[pos] == ':')
        {
            rest = param;
            return 0;
        }
        while (pos < param.Length && param[pos] == ' ') pos++;
        rest = param[pos..];
        return result;
    }

    /// <summary>Numeric parameter as the C++ command() computed it (iparam).</summary>
    public static int IntParam(string param) => StrToIntStr(param, out _);

    // ---------------------------------------------------------------- power

    /// <summary>Shutdown/Reboot/Logoff parameter: 1 = force, 2 = close all windows first.</summary>
    public static bool IsForce(string param) => IntParam(param) == 1;
    public static bool IsCloseAll(string param) => IntParam(param) == 2;

    // ---------------------------------------------------------------- display

    /// <summary>lParam of SC_MONITORPOWER: the number if the parameter is not empty, otherwise 2 (off).</summary>
    public static int MonitorPowerParam(string param) => param.Length > 0 ? IntParam(param) : 2;

    public static ResolutionRequest ParseResolution(string param)
    {
        int pos = 0;
        uint w = StrToUL(param, ref pos), h = StrToUL(param, ref pos), c = StrToUL(param, ref pos),
            f = StrToUL(param, ref pos), d = StrToUL(param, ref pos);
        DisplayMode? second = null;
        if (param.Length > 0)
        {
            while (pos < param.Length && param[pos] == ' ') pos++;
            if (pos < param.Length && param[pos] == ',')
            {
                pos++;
                uint w2 = StrToUL(param, ref pos), h2 = StrToUL(param, ref pos), c2 = StrToUL(param, ref pos),
                    f2 = StrToUL(param, ref pos);
                second = new DisplayMode(w2, h2, c2, f2);
            }
        }
        return new ResolutionRequest(param.Length == 0, new DisplayMode(w, h, c, f), d, second);
    }

    /// <summary>Device name for EnumDisplaySettings/ChangeDisplaySettingsEx; null (primary display) for display 0.</summary>
    public static string? DisplayDevice(uint display) => display > 0 ? @"\\.\DISPLAY" + display : null;

    /// <summary>
    /// The mode to set, given the current one. An empty parameter toggles 800x600 and 1024x768. With two modes, the
    /// second is used when the first is already active, so one hotkey switches between them.
    /// </summary>
    public static DisplayMode TargetMode(ResolutionRequest r, DisplayMode current)
    {
        if (r.Empty)
        {
            return current.Width == 800
                ? current with { Width = 1024, Height = 768 }
                : current with { Width = 800, Height = 600 };
        }
        var m = r.First;
        if (r.Second is { } s
            && (m.Width == 0 || current.Width == m.Width)
            && (s.Width != m.Width || ((m.Bits == 0 || current.Bits == m.Bits) && (m.Frequency == 0 || current.Frequency == m.Frequency))))
        {
            m = s;
        }
        return new DisplayMode(
            m.Width != 0 ? m.Width : current.Width,
            m.Height != 0 ? m.Height : current.Height,
            m.Bits != 0 ? m.Bits : current.Bits,
            m.Frequency != 0 ? m.Frequency : current.Frequency);
    }

    // ---------------------------------------------------------------- control panel

    static readonly string[] ControlPanels = ["appwiz", "desk", "inetcpl", "main", "mmsys", "powercfg", "sysdm", "timedate"];

    /// <summary>Arguments for rundll32.exe. The parameter is a .cpl name without extension; it overrides the table.</summary>
    public static string ControlPanelArgs(int cmd, string param)
    {
        string args = "shell32.dll,Control_RunDLL ";
        if (param.Length == 0 && cmd != 12) args += ControlPanels[cmd - 50] + ".cpl";
        else if (param.Length > 0) args += param + ".cpl";
        return args;
    }

    // ---------------------------------------------------------------- system

    /// <summary>Files and folders older than this (both creation and modification) are deleted. Default 7 days.</summary>
    public static DateTime TempCutoffUtc(DateTime nowUtc, int days) => nowUtc - TimeSpan.FromDays(days != 0 ? days : 7);

    public static bool IsOldTempEntry(DateTime lastWriteUtc, DateTime creationUtc, DateTime cutoffUtc) =>
        lastWriteUtc < cutoffUtc && creationUtc < cutoffUtc;

    // ---------------------------------------------------------------- drives

    /// <summary>Drive letter of the CD commands: the first character, or null (first CD-ROM drive) when empty.</summary>
    public static char? CdDriveLetter(string param) => param.Length > 0 ? param[0] : null;

    /// <summary>CD speed command: speed first, then the drive letter.</summary>
    public static (int Speed, char? Drive) CdSpeedParam(string param)
    {
        int speed = StrToIntStr(param, out string rest);
        return (speed, CdDriveLetter(rest));
    }

    /// <summary>Bytes 2 and 3 of the SET CD SPEED command: kilobytes per second for "speed x" (1x = 176.4 kB/s).</summary>
    public static (byte Hi, byte Lo) CdSpeedBytes(int speed)
    {
        int v = (int)(uint)(176.4 * speed + 1);
        return ((byte)(v >> 8), (byte)v);
    }

    /// <summary>
    /// Interprets the sense data of TEST UNIT READY. Media present (no sense) = eject; "medium not present" with
    /// ASCQ 1 (tray closed) = eject, ASCQ 2 (tray open) = close; anything else = ask MCI.
    /// </summary>
    public static CdDoorAction DoorAction(bool ioctlOk, byte senseKey, byte asc, byte ascq)
    {
        if (!ioctlOk) return CdDoorAction.MciFallback;
        int result = 0;
        int s = senseKey & 0xf;
        if (s == 0) result = 1;
        if (s == 2 && asc == 0x3A) result = ascq;
        return result switch { 1 => CdDoorAction.Eject, 2 => CdDoorAction.Close, _ => CdDoorAction.MciFallback };
    }

    /// <summary>Remove drive: the first character as an upper case letter, or null when it is not A..Z.</summary>
    public static char? RemoveDriveLetter(char c)
    {
        char u = (char)(c & ~0x20);
        return u is >= 'A' and <= 'Z' ? u : null;
    }
}
