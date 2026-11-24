// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Runtime.InteropServices;
using HotkeyP.Native;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054 // plain DllImport keeps interop simple and readable

/// <summary>NOTIFYICONDATAW, used to hide or show tray icons of other applications.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATAW
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
    public uint uTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

/// <summary>P/Invoke used only by the window commands.</summary>
internal static class WindowNative
{
    public const int VK_LBUTTON = 1, VK_RBUTTON = 2;
    public const int WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_SYSMENU = 0x80000;
    public const uint GW_OWNER = 4;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const int SW_MAXIMIZE = 3;
    public const int WM_COMMAND = 0x111, WM_GETICON = 0x7F;
    public const int ICON_SMALL2 = 2;
    public const int GCLP_HICON = -14, GCLP_HICONSM = -34;
    public const uint SPI_GETWORKAREA = 0x30;
    public const uint NIM_MODIFY = 1, NIF_STATE = 8, NIS_HIDDEN = 1;

    [DllImport("kernel32.dll")] public static extern bool SetPriorityClass(IntPtr process, uint priorityClass);
    [DllImport("kernel32.dll")] public static extern uint GetPriorityClass(IntPtr process);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint winIni);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] public static extern uint GetWindowThreadId(IntPtr hWnd, IntPtr pid);
}
