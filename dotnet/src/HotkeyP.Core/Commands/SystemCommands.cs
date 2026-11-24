// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HotkeyP.Input;
using static HotkeyP.Commands.SystemNative;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>Power, display, control panel, system maintenance and drive commands.</summary>
static class SystemCommands
{
    public static void Register(CommandTable t)
    {
        // power
        t.Add(2, c => Exit(c, EWX_POWEROFF, true));
        t.Add(3, c => Exit(c, EWX_REBOOT, true));
        t.Add(5, c => Exit(c, EWX_LOGOFF, false));
        t.Add(4, c =>
        {
            EnableShutdownPrivilege();
            SetSuspendState(false, SystemParsing.IsForce(c.Param), false);
        });
        t.Add(64, c =>
        {
            InputSender.WaitForKeysReleased();
            EnableShutdownPrivilege();
            SetSuspendState(true, SystemParsing.IsForce(c.Param), false);
        });
        t.Add(38, _ => PostMessage(FindWindow("Shell_TrayWnd", null), WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        // the password lock screen of the C++ version (non-empty parameter) is not ported; always the system lock
        t.Add(63, _ => LockWorkStation());

        // display
        t.Add(6, c => PostMessage(CommandWindow(c, GetDesktopWindow()), WM_SYSCOMMAND, SC_SCREENSAVE, IntPtr.Zero));
        t.Add(19, MonitorOff);
        t.Add(18, Resolution);

        // control panel
        t.Add([12, 50, 51, 52, 53, 54, 55, 56, 57], c => Run(c, "rundll32.exe", SystemParsing.ControlPanelArgs(c.Command, c.Param)));

        // system
        t.Add(22, c => SHEmptyRecycleBin(c.Host.MainWindowHandle, "", (ushort)SystemParsing.IntParam(c.Param)));
        t.Add([92, 93], Service);
        t.Add(104, _ => SHAddToRecentDocs(0, IntPtr.Zero));
        t.Add(105, c => DeleteTempFiles(SystemParsing.IntParam(c.Param)));

        // drives
        t.Add([0, 1, 39, 40, 41, 42, 62], c => SystemDrives.AudioCD(c.Host, SystemParsing.CdDriveLetter(c.Param), c.Command));
        t.Add(100, c =>
        {
            var (speed, drive) = SystemParsing.CdSpeedParam(c.Param);
            SystemDrives.AudioCD(c.Host, drive, 100, speed);
        });
        t.Add(109, c => SystemDrives.RemoveDrive(c.Host, c.Param));
    }

    static IntPtr CommandWindow(CommandContext c, IntPtr fallback)
    {
        var w = c.Host.MainWindowHandle;
        return w != IntPtr.Zero ? w : fallback;
    }

    static void Run(CommandContext c, string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            c.Host.ShowMessage($"Cannot start {exe}: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- power

    /// <summary>Enables SeShutdownPrivilege (C++ privilege()).</summary>
    static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
        try
        {
            if (LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out long luid))
            {
                var tkp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    LuidLow = (uint)luid,
                    LuidHigh = (int)(luid >> 32),
                    Attributes = SE_PRIVILEGE_ENABLED,
                };
                AdjustTokenPrivileges(token, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        finally { CloseHandle(token); }
    }

    /// <summary>
    /// Shutdown, reboot, logoff. Parameter 1 = forced (EWX_FORCE), 2 = close all windows first.
    /// </summary>
    static void Exit(CommandContext c, uint flags, bool privilege)
    {
        if (privilege) EnableShutdownPrivilege();
        if (SystemParsing.IsCloseAll(c.Param)) CloseAllWindows();
        if (SystemParsing.IsForce(c.Param)) flags |= EWX_FORCE;
        ExitWindowsEx(flags, 0);
    }

    /// <summary>Asks every visible window (except Explorer and HotkeyP) to close (C++ closeAll2).</summary>
    static void CloseAllWindows()
    {
        int explorerPid = 0;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            if (explorerPid == 0) explorerPid = p.Id;
            p.Dispose();
        }
        int self = Environment.ProcessId;
        var windows = new List<IntPtr>();
        EnumWindows((w, _) =>
        {
            if (IsWindowVisible(w) && GetWindowThreadProcessId(w, out int pid) != 0 && pid != explorerPid && pid != self)
                windows.Add(w);
            return true;
        }, IntPtr.Zero);
        foreach (var w in windows)
            SendMessageTimeout(w, WM_SYSCOMMAND, SC_CLOSE, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 10000, out _);
    }

    // ---------------------------------------------------------------- display

    static int monitorWatch;

    /// <summary>Display power off. Moving the mouse turns it on again.</summary>
    static void MonitorOff(CommandContext c)
    {
        InputSender.WaitForKeysReleased();
        GetCursorPos(out var start);
        var w = CommandWindow(c, FindWindow("Shell_TrayWnd", null));
        PostMessage(w, WM_SYSCOMMAND, SC_MONITORPOWER, SystemParsing.MonitorPowerParam(c.Param));
        if (Interlocked.Exchange(ref monitorWatch, 1) == 1) return;
        new Thread(() =>
        {
            const int senz = 15;
            try
            {
                while (true)
                {
                    Thread.Sleep(100);
                    if (!GetCursorPos(out var pt)) continue;
                    if (Math.Abs(start.X - pt.X) > senz || Math.Abs(start.Y - pt.Y) > senz)
                    {
                        PostMessage(w, WM_SYSCOMMAND, SC_MONITORPOWER, -1);
                        break;
                    }
                }
            }
            finally { Volatile.Write(ref monitorWatch, 0); }
        }) { IsBackground = true, Name = "HotkeyP monitor wake" }.Start();
    }

    /// <summary>Changes resolution, color depth and frequency of a display (C++ resolution()).</summary>
    static void Resolution(CommandContext c)
    {
        var r = SystemParsing.ParseResolution(c.Param);
        string? dev = SystemParsing.DisplayDevice(r.Display);
        var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm))
        {
            c.Host.ShowMessage($"EnumDisplaySettings failed for display {r.Display}");
            return;
        }
        var m = SystemParsing.TargetMode(r, new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmBitsPerPel, dm.dmDisplayFrequency));
        dm.dmPelsWidth = m.Width;
        dm.dmPelsHeight = m.Height;
        dm.dmBitsPerPel = m.Bits;
        dm.dmDisplayFrequency = m.Frequency;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
        int err = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (err == DISP_CHANGE_NOTUPDATED) err = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        switch (err)
        {
            case DISP_CHANGE_SUCCESSFUL:
                break;
            case DISP_CHANGE_BADMODE:
                c.Host.ShowMessage("The graphics mode is not supported");
                break;
            case DISP_CHANGE_RESTART:
                c.Host.ShowMessage("The computer must be restarted in order for the graphics mode to work");
                break;
            default:
                c.Host.ShowMessage($"ChangeDisplaySettings failed for display {r.Display} (error code {err})");
                break;
        }
    }

    // ---------------------------------------------------------------- system

    static void Service(CommandContext c)
    {
        bool start = c.Command == 93;
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            c.Host.ShowMessage("Cannot open SC manager");
            return;
        }
        try
        {
            var svc = OpenService(scm, c.Param, start ? SERVICE_START : SERVICE_STOP);
            if (svc == IntPtr.Zero)
            {
                c.Host.ShowMessage($"Cannot open service {c.Param}");
                return;
            }
            try
            {
                if (start)
                {
                    if (!StartService(svc, 0, IntPtr.Zero))
                    {
                        int e = Marshal.GetLastWin32Error();
                        if (e != ERROR_SERVICE_ALREADY_RUNNING) c.Host.ShowMessage($"Cannot start service (error {e})");
                    }
                }
                else if (!ControlService(svc, SERVICE_CONTROL_STOP, out _))
                {
                    int e = Marshal.GetLastWin32Error();
                    if (e != ERROR_SERVICE_NOT_ACTIVE) c.Host.ShowMessage($"Cannot stop service (error {e})");
                }
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    /// <summary>Deletes files and empty folders in TEMP older than <paramref name="days"/> (default 7).</summary>
    static void DeleteTempFiles(int days)
    {
        string temp = Path.GetTempPath();
        if (temp.Length <= 4) return; // never a drive root
        DeleteTemp(temp, SystemParsing.TempCutoffUtc(DateTime.UtcNow, days));
    }

    static readonly EnumerationOptions TempEnum = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>C++ deleteTemp: a folder is removed only if it is old and became empty.</summary>
    internal static void DeleteTemp(string dir, DateTime cutoffUtc)
    {
        IEnumerable<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", TempEnum).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }

        foreach (var e in entries)
        {
            try
            {
                bool old = SystemParsing.IsOldTempEntry(e.LastWriteTimeUtc, e.CreationTimeUtc, cutoffUtc);
                if (e is DirectoryInfo d)
                {
                    if (d.Name.StartsWith('.')) continue;
                    // do not follow junctions and symbolic links out of the TEMP folder
                    if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) DeleteTemp(d.FullName, cutoffUtc);
                    if (old) RemoveDirectory(d.FullName);
                }
                else if (old && !DeleteFile(e.FullName))
                {
                    // read-only files
                    var attr = e.Attributes;
                    if (attr.HasFlag(FileAttributes.ReadOnly))
                    {
                        e.Attributes = attr & ~FileAttributes.ReadOnly;
                        DeleteFile(e.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "DeleteFileW", SetLastError = true)]
    static extern bool DeleteFile(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "RemoveDirectoryW", SetLastError = true)]
    static extern bool RemoveDirectory(string path);
}
