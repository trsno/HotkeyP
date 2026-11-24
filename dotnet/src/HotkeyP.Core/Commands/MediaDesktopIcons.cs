// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Port of saveDesktopIcons (Commands.cpp) and saveIcons/loadIcons (hook.cpp). The C++ runs the ListView code inside
// explorer through a hook DLL; here the ListView messages are sent from HotkeyP with buffers allocated in explorer.
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using HotkeyP.Native;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054

/// <summary>Position of one desktop icon, identified by its text.</summary>
sealed record DesktopIcon(int X, int Y, string Name);

static class DesktopIcons
{
    /// <summary>Format of the registry value "desktopIcons": "x y name|" for each icon.</summary>
    public static string Serialize(IEnumerable<DesktopIcon> icons)
    {
        var sb = new StringBuilder();
        foreach (var i in icons) sb.Append(i.X).Append(' ').Append(i.Y).Append(' ').Append(i.Name).Append('|');
        return sb.ToString();
    }

    /// <summary>Parses like loadIcons: number, optional space, number, optional space, name up to '|'.</summary>
    public static List<DesktopIcon> Parse(string s)
    {
        var list = new List<DesktopIcon>();
        int p = 0;
        while (p < s.Length)
        {
            int x = StrToL(s, ref p);
            if (p < s.Length && s[p] == ' ') p++;
            int y = StrToL(s, ref p);
            if (p < s.Length && s[p] == ' ') p++;
            int e = s.IndexOf('|', p);
            if (e < 0) break;
            list.Add(new DesktopIcon(x, y, s[p..e]));
            p = e + 1;
        }
        return list;
    }

    static int StrToL(string s, ref int p)
    {
        int i = p;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
        int start = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { if (v < int.MaxValue) v = v * 10 + (s[i] - '0'); i++; }
        if (i == start) return 0; // no number: position unchanged, like strtol
        p = i;
        return (int)Math.Clamp(neg ? -v : v, int.MinValue, int.MaxValue);
    }

    // ------------------------------------------------------------ explorer's desktop ListView

    const int LVM_GETITEMCOUNT = 0x1004, LVM_SETITEMPOSITION = 0x100F, LVM_GETITEMPOSITION = 0x1010, LVM_GETITEMTEXTW = 0x1073;
    const uint PROCESS_VM_OPERATION = 0x8, PROCESS_VM_READ = 0x10, PROCESS_VM_WRITE = 0x20, PROCESS_QUERY_INFORMATION = 0x400;
    const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 4;
    const uint SMTO_ABORTIFHUNG = 2, SMTO_BLOCK = 1, Timeout = 5000;
    const int TextChars = 260, TextOffset = 1024, BufferSize = TextOffset + TextChars * 2;

    [StructLayout(LayoutKind.Sequential)]
    struct LVITEMW
    {
        public uint mask;
        public int iItem, iSubItem;
        public uint state, stateMask;
        public IntPtr pszText;
        public int cchTextMax, iImage;
        public IntPtr lParam;
        public int iIndent, iGroupId;
        public uint cColumns;
        public IntPtr puColumns, piColFmt;
        public int iGroup;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr addr, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(IntPtr process, IntPtr addr, IntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr addr, out POINT pt, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr process, IntPtr addr, ref LVITEMW item, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsWow64Process(IntPtr process, out bool wow64);

    /// <summary>Progman > SHELLDLL_DefView > SysListView32; the DefView moves under a WorkerW window on some systems.</summary>
    static IntPtr FindDesktopListView()
    {
        IntPtr defView = NativeMethods.FindWindowEx(NativeMethods.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (IntPtr w = IntPtr.Zero; defView == IntPtr.Zero;)
        {
            w = NativeMethods.FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
            if (w == IntPtr.Zero) break;
            defView = NativeMethods.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return defView == IntPtr.Zero ? IntPtr.Zero : NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    static IntPtr Send(IntPtr w, int msg, IntPtr wp, IntPtr lp)
    {
        NativeMethods.SendMessageTimeout(w, msg, wp, lp, SMTO_ABORTIFHUNG | SMTO_BLOCK, Timeout, out IntPtr r);
        return r;
    }

    /// <summary>Reads every icon's text and position from the desktop ListView in explorer's process.</summary>
    static List<DesktopIcon>? ReadIcons(IntPtr lv, out string? error)
    {
        error = null;
        NativeMethods.GetWindowThreadProcessId(lv, out int pid);
        IntPtr proc = NativeMethods.OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, false, pid);
        if (proc == IntPtr.Zero) { error = "Cannot access the desktop (explorer) process."; return null; }
        try
        {
            // structures with pointers must have explorer's layout
            bool target32 = !Environment.Is64BitOperatingSystem || IsWow64Process(proc, out bool wow) && wow;
            if (target32 == Environment.Is64BitProcess) { error = "Desktop icons can only be saved by a HotkeyP build of the same bitness as explorer."; return null; }
            IntPtr mem = VirtualAllocEx(proc, IntPtr.Zero, BufferSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (mem == IntPtr.Zero) { error = "Cannot access the desktop (explorer) process."; return null; }
            try
            {
                int n = (int)Send(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                var list = new List<DesktopIcon>(n);
                var text = new byte[TextChars * 2];
                for (int i = 0; i < n; i++)
                {
                    POINT pt = default;
                    if (Send(lv, LVM_GETITEMPOSITION, i, mem) != IntPtr.Zero)
                        ReadProcessMemory(proc, mem, out pt, Marshal.SizeOf<POINT>(), out _);
                    var item = new LVITEMW { iItem = i, iSubItem = 0, pszText = mem + TextOffset, cchTextMax = TextChars };
                    string name = "";
                    if (WriteProcessMemory(proc, mem, ref item, Marshal.SizeOf<LVITEMW>(), out _))
                    {
                        int len = Math.Clamp((int)Send(lv, LVM_GETITEMTEXTW, i, mem), 0, TextChars - 1);
                        if (len > 0 && ReadProcessMemory(proc, mem + TextOffset, text, len * 2, out _))
                            name = Encoding.Unicode.GetString(text, 0, len * 2);
                    }
                    list.Add(new DesktopIcon(pt.X, pt.Y, name));
                }
                return list;
            }
            finally { VirtualFreeEx(proc, mem, IntPtr.Zero, MEM_RELEASE); }
        }
        finally { NativeMethods.CloseHandle(proc); }
    }

    /// <summary>Command 106 (<paramref name="restore"/> false) and 107.</summary>
    public static void Run(CommandContext c, bool restore)
    {
        IntPtr lv = FindDesktopListView();
        if (lv == IntPtr.Zero) return;
        var current = ReadIcons(lv, out string? error);
        if (current == null) { c.Host.ShowMessage(error ?? "Cannot read the desktop icons."); return; }
        if (!restore)
        {
            MediaSettings.Set("desktopIcons", Serialize(current), RegistryValueKind.String);
            if (c.Hotkey != null) c.Host.ShowMessage("Desktop icon positions are saved", error: false);
            return;
        }
        if (MediaSettings.Get("desktopIcons") is not string saved) return;
        foreach (var icon in Parse(saved))
        {
            // ListView_FindItem with LVFI_STRING: first item with this text, case-insensitive
            int i = current.FindIndex(x => string.Equals(x.Name, icon.Name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) continue;
            int lp = (icon.Y & 0xFFFF) << 16 | (icon.X & 0xFFFF);
            Send(lv, LVM_SETITEMPOSITION, i, lp);
        }
    }
}
