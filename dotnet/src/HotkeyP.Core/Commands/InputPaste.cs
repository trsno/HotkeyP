// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HotkeyP.Engine;
using HotkeyP.Input;
using Microsoft.Win32;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>
/// "Paste text" (command 67): puts the text on the clipboard, presses Ctrl+V (Shift+Insert in terminals) and then
/// restores the previous clipboard content. Pastes requested while one is running are queued.
/// Everything runs on the command executor thread (STA); delays are timers that post back to it.
/// </summary>
static class InputPaste
{
    /// <summary>Wait after the paste keys before the clipboard is restored or the next queued text is pasted.</summary>
    const int RestoreDelay = 500, NextDelay = 300;

    // state, touched only on the executor thread
    static bool busy;
    static readonly Queue<string> queue = new();
    static List<(uint Format, byte[] Data)>? saved;

    /// <summary>Port of pasteText.</summary>
    public static void Paste(CommandRunner runner, string param)
    {
        if (busy)
        {
            queue.Enqueue(param);
            return;
        }
        busy = true;
        bool started = false;
        try
        {
            var items = SplitItems(param).Select(FormatText).ToList();
            int sel = 0;
            if (items.Count > 1) sel = PickWithFocusRestore(runner.Host, items);
            if (sel < 0) return;
            string text = items[sel];
            // a queued paste keeps the snapshot taken by the first one
            saved ??= SaveClipboard();
            if (!SetClipboardText(text)) return;
            // activation is needed for Notepad++
            var w = GetForegroundWindow();
            if (GetClassName(w) == "Notepad++") PostMessage(w, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
            started = true;
            After(runner, OnlyAltHeld() ? 150 : 10, () =>
            {
                try { MacroPlayer.Play(PasteKeysMacro(GetClassName(GetForegroundWindow()))); }
                finally { After(runner, queue.Count > 0 ? NextDelay : RestoreDelay, () => Done(runner)); }
            });
        }
        finally
        {
            if (!started) Done(runner);
        }
    }

    /// <summary>The paste finished: paste the next queued text, or restore the clipboard.</summary>
    static void Done(CommandRunner runner)
    {
        busy = false;
        if (queue.Count > 0)
        {
            Paste(runner, queue.Dequeue());
            return;
        }
        if (saved != null)
        {
            RestoreClipboard(saved);
            saved = null;
        }
    }

    static void After(CommandRunner runner, int ms, Action action)
        => Task.Delay(ms).ContinueWith(_ => runner.Post(action), TaskScheduler.Default);

    /// <summary>Like checkShifts(MOD_ALT): Alt is held and Shift, Ctrl and Win are not.</summary>
    static bool OnlyAltHeld() =>
        GetAsyncKeyState(InputSender.VK_MENU) < 0 && GetAsyncKeyState(InputSender.VK_SHIFT) >= 0
        && GetAsyncKeyState(InputSender.VK_CONTROL) >= 0
        && GetAsyncKeyState(InputSender.VK_LWIN) >= 0 && GetAsyncKeyState(InputSender.VK_RWIN) >= 0;

    static readonly string[] Terminals = ["PuTTY", "KiTTY", "mintty", "ConsoleWindowClass", "VirtualConsoleClass"];

    /// <summary>Terminals (PuTTY, console, ...) do not accept Ctrl+V; Shift+Insert is used for them (upstream issue #29).</summary>
    internal static string PasteKeysMacro(string foregroundClass)
        => Terminals.Contains(foregroundClass, StringComparer.Ordinal) ? @"\SHIFT\INS" : @"\^\V";

    /// <summary>Splits the parameter at "%|" (but not at "%%|", which is an escaped percent sign followed by |).</summary>
    internal static List<string> SplitItems(string param)
    {
        var result = new List<string>();
        int start = 0;
        for (int i = 0; i + 1 < param.Length; i++)
        {
            if (param[i] != '%' || param[i + 1] != '|') continue;
            int run = 0;
            for (int t = i; t >= 0 && param[t] == '%'; t--) run++;
            if ((run & 1) == 0) continue;
            result.Add(param[start..i]);
            start = i + 2;
            i++;
        }
        result.Add(param[start..]);
        return result;
    }

    /// <summary>Shows the list and gives the keyboard focus (and the edit box selection) back to the window that had it.</summary>
    static int PickWithFocusRestore(IAppHost host, List<string> items)
    {
        var foreground = GetForegroundWindow();
        var focus = GetFocusGlobal();
        int editSel = -1;
        bool unicode = focus != IntPtr.Zero && IsWindowUnicode(focus);
        if (focus != IntPtr.Zero && SendMessageTimeoutW(focus, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero, SMTO_BLOCK, 5000, out var lines) != IntPtr.Zero
            && lines > 0)
        {
            editSel = (int)SendMessage(focus, EM_GETSEL, IntPtr.Zero, IntPtr.Zero);
        }
        int sel = host.PickFromList("Paste text", items);
        if (foreground != IntPtr.Zero) SetForegroundWindow(foreground);
        if (editSel != -1)
        {
            uint tid = GetWindowThreadProcessId(focus, out _);
            AttachThreadInput(GetCurrentThreadId(), tid, true);
            SetFocus(focus);
            AttachThreadInput(GetCurrentThreadId(), tid, false);
            var (start, end) = ((IntPtr)(short)(editSel & 0xFFFF), (IntPtr)(short)((editSel >> 16) & 0xFFFF));
            if (unicode) SendMessageTimeoutW(focus, EM_SETSEL, start, end, SMTO_BLOCK, 15000, out _);
            else SendMessageTimeoutA(focus, EM_SETSEL, start, end, SMTO_BLOCK, 15000, out _);
        }
        return sel;
    }

    static IntPtr GetFocusGlobal()
    {
        var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(0, ref gui) ? gui.hwndFocus : IntPtr.Zero;
    }

    // ---------------------------------------------------------------- clipboard

    const uint CF_BITMAP = 2, CF_METAFILEPICT = 3, CF_PALETTE = 9, CF_UNICODETEXT = 13, CF_ENHMETAFILE = 14,
        CF_OWNERDISPLAY = 0x80, CF_DSPBITMAP = 0x82, CF_DSPMETAFILEPICT = 0x83, CF_DSPENHMETAFILE = 0x8E;

    /// <summary>Formats whose data is a GDI handle, not global memory; they cannot be copied (CF_DIB keeps images).</summary>
    static bool IsHandleFormat(uint f) => f is CF_BITMAP or CF_METAFILEPICT or CF_PALETTE or CF_ENHMETAFILE
        or CF_OWNERDISPLAY or CF_DSPBITMAP or CF_DSPMETAFILEPICT or CF_DSPENHMETAFILE;

    static bool OpenClipboardRetry()
    {
        for (int i = 0; i < 20; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>Copies every clipboard format that lives in global memory.</summary>
    static List<(uint, byte[])>? SaveClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        var list = new List<(uint, byte[])>();
        try
        {
            uint f = 0;
            while ((f = EnumClipboardFormats(f)) != 0)
            {
                if (IsHandleFormat(f)) continue;
                var hmem = GetClipboardData(f);
                if (hmem == IntPtr.Zero) continue;
                var ptr = GlobalLock(hmem);
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    long size = (long)GlobalSize(hmem);
                    if (size < 100_000_000)
                    {
                        var data = new byte[size];
                        Marshal.Copy(ptr, data, 0, (int)size);
                        list.Add((f, data));
                    }
                }
                finally { GlobalUnlock(hmem); }
            }
        }
        finally { CloseClipboard(); }
        return list.Count > 0 ? list : null;
    }

    static void RestoreClipboard(List<(uint Format, byte[] Data)> data)
    {
        if (!OpenClipboardRetry()) return;
        try
        {
            if (!EmptyClipboard()) return;
            foreach (var (format, bytes) in data) SetData(format, bytes);
        }
        finally { CloseClipboard(); }
    }

    static bool SetClipboardText(string text)
    {
        if (!OpenClipboardRetry()) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            return SetData(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
        }
        finally { CloseClipboard(); }
    }

    static bool SetData(uint format, byte[] bytes)
    {
        var hmem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(bytes.Length, 1));
        if (hmem == IntPtr.Zero) return false;
        var ptr = GlobalLock(hmem);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hmem);
            return false;
        }
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        GlobalUnlock(hmem);
        if (SetClipboardData(format, hmem) != IntPtr.Zero) return true;
        GlobalFree(hmem);
        return false;
    }

    /// <summary>Text on the clipboard, or null.</summary>
    internal static string? ReadClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            var hmem = GetClipboardData(CF_UNICODETEXT);
            if (hmem == IntPtr.Zero) return null;
            var ptr = GlobalLock(hmem);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(hmem); }
        }
        finally { CloseClipboard(); }
    }

    // ---------------------------------------------------------------- formatText

    /// <summary>Sources of the values formatText inserts; replaceable in tests.</summary>
    internal sealed class TextSources
    {
        public DateTime Now { get; init; } = DateTime.Now;
        public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
        public Func<string?> Clipboard { get; init; } = ReadClipboardText;
        public Func<string> UserName { get; init; } = () => Environment.UserName;
        public Func<string> ComputerName { get; init; } = () => Environment.MachineName;
        public Func<string, string?> Registry { get; init; } = ReadRegistryValue;
        public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;
    }

    /// <summary>
    /// Port of formatText, used by "Paste text", "Show text" and the lock screen text.
    /// %r new line, %u user name, %o computer name, %l clipboard text, %% percent sign,
    /// %"HKEY_...\key\value" registry value (DWORD or string), any other %x is a strftime code (%d.%m.%Y, %H:%M, %#x ...).
    /// </summary>
    internal static string FormatText(string param) => FormatText(param, new TextSources());

    internal static string FormatText(string param, TextSources src)
    {
        var sb = new StringBuilder(param.Length + 64);
        for (int i = 0; i < param.Length; i++)
        {
            char ch = param[i];
            if (ch != '%')
            {
                sb.Append(ch);
                continue;
            }
            if (++i >= param.Length) break; // a lone % at the end is dropped
            ch = param[i];
            switch (ch)
            {
                case 'r': sb.Append("\r\n"); break;
                case 'u': sb.Append(src.UserName()); break;
                case 'o': sb.Append(src.ComputerName()); break;
                case 'l': sb.Append(src.Clipboard()); break;
                case '%': sb.Append('%'); break;
                case '"':
                    int end = param.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        // no closing quote: the text is kept without the percent sign
                        sb.Append('"');
                        break;
                    }
                    sb.Append(src.Registry(param[(i + 1)..end]));
                    i = end;
                    break;
                default:
                    bool alt = false;
                    if (ch == '#')
                    {
                        if (i + 1 >= param.Length)
                        {
                            sb.Append("%#");
                            break;
                        }
                        alt = true;
                        ch = param[++i];
                    }
                    var s = StrFTime(ch, alt, src.Now, src.Culture, src.TimeZone);
                    if (s == null) sb.Append('%').Append(ch);
                    else sb.Append(s);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>One strftime conversion as the Microsoft C runtime does it with the user's locale; null if the code is unknown.</summary>
    internal static string? StrFTime(char code, bool alt, DateTime t, CultureInfo ci, TimeZoneInfo tz)
    {
        var f = ci.DateTimeFormat;
        int yday = t.DayOfYear - 1, wday = (int)t.DayOfWeek;
        string N(int v, int width) => alt ? v.ToString(CultureInfo.InvariantCulture) : v.ToString(new string('0', width), CultureInfo.InvariantCulture);
        string D(string pattern) => t.ToString(pattern, ci);
        int hour12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return code switch
        {
            'a' => f.AbbreviatedDayNames[wday],
            'A' => f.DayNames[wday],
            'b' or 'h' => f.AbbreviatedMonthNames[t.Month - 1],
            'B' => f.MonthNames[t.Month - 1],
            'c' => alt ? D(f.LongDatePattern) + " " + D(f.LongTimePattern) : D(f.ShortDatePattern) + " " + D(f.LongTimePattern),
            'C' => N(t.Year / 100, 2),
            'd' => N(t.Day, 2),
            'D' => $"{N(t.Month, 2)}/{N(t.Day, 2)}/{N(t.Year % 100, 2)}",
            'e' => alt ? N(t.Day, 2) : t.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2),
            'F' => $"{N(t.Year, 4)}-{N(t.Month, 2)}-{N(t.Day, 2)}",
            'g' => N(ISOWeek.GetYear(t) % 100, 2),
            'G' => N(ISOWeek.GetYear(t), 4),
            'H' => N(t.Hour, 2),
            'I' => N(hour12, 2),
            'j' => N(t.DayOfYear, 3),
            'm' => N(t.Month, 2),
            'M' => N(t.Minute, 2),
            'n' => "\n",
            'p' => t.Hour < 12 ? f.AMDesignator : f.PMDesignator,
            'R' => $"{N(t.Hour, 2)}:{N(t.Minute, 2)}",
            'S' => N(t.Second, 2),
            't' => "\t",
            'T' => $"{N(t.Hour, 2)}:{N(t.Minute, 2)}:{N(t.Second, 2)}",
            'u' => (wday == 0 ? 7 : wday).ToString(CultureInfo.InvariantCulture),
            'U' => N((yday + 7 - wday) / 7, 2),
            'V' => N(ISOWeek.GetWeekOfYear(t), 2),
            'w' => wday.ToString(CultureInfo.InvariantCulture),
            'W' => N((yday + 7 - (wday + 6) % 7) / 7, 2),
            'x' => D(alt ? f.LongDatePattern : f.ShortDatePattern),
            'X' => D(f.LongTimePattern),
            'y' => N(t.Year % 100, 2),
            'Y' => N(t.Year, 4),
            'z' => FormatOffset(tz.GetUtcOffset(t)),
            'Z' => tz.IsDaylightSavingTime(t) ? tz.DaylightName : tz.StandardName,
            _ => null,
        };
    }

    static string FormatOffset(TimeSpan o)
        => (o < TimeSpan.Zero ? "-" : "+") + Math.Abs(o.Hours).ToString("00", CultureInfo.InvariantCulture)
           + Math.Abs(o.Minutes).ToString("00", CultureInfo.InvariantCulture);

    static readonly (string Name, RegistryHive Hive)[] RegRoots =
    [
        ("LOCAL_MACHINE", RegistryHive.LocalMachine), ("CURRENT_USER", RegistryHive.CurrentUser),
        ("CLASSES_ROOT", RegistryHive.ClassesRoot), ("USERS", RegistryHive.Users), ("CURRENT_CONFIG", RegistryHive.CurrentConfig),
    ];

    /// <summary>Splits "HKEY_ROOT\key\path\value" into the hive, the key path and the value name (after the last backslash).</summary>
    internal static (RegistryHive Hive, string Key, string Value)? ParseRegistryPath(string path)
    {
        if (!path.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var (name, hive) in RegRoots)
        {
            if (string.Compare(path, 5, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            int i = 5 + name.Length;
            if (i >= path.Length || path[i] != '\\') return null;
            string rest = path[(i + 1)..];
            int last = rest.LastIndexOf('\\');
            return last < 0 ? (hive, "", rest) : (hive, rest[..last], rest[(last + 1)..]);
        }
        return null;
    }

    /// <summary>A DWORD (as an unsigned decimal number) or string value from the 64-bit registry view; empty if it does not exist.</summary>
    internal static string? ReadRegistryValue(string path)
    {
        if (ParseRegistryPath(path) is not { } p) return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(p.Hive, RegistryView.Registry64);
            using var key = p.Key.Length == 0 ? root : root.OpenSubKey(p.Key);
            if (key == null) return null;
            return key.GetValueKind(p.Value) switch
            {
                RegistryValueKind.DWord => unchecked((uint)(int)key.GetValue(p.Value, 0)!).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.String => key.GetValue(p.Value, "") as string,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- P/Invoke

    const int WM_ACTIVATE = 6, WA_ACTIVE = 1, EM_GETSEL = 0xB0, EM_SETSEL = 0xB1, EM_GETLINECOUNT = 0xBA;
    const uint GMEM_MOVEABLE = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Native.RECT rcCaret;
    }

    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll")] static extern bool IsWindowUnicode(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutA")]
    static extern IntPtr SendMessageTimeoutA(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
