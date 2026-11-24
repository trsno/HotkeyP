// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using HotkeyP.Engine;
using static HotkeyP.Commands.WindowNative;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>
/// Hide window (25), Hide application (101), Minimize application/window to tray (102, 115), Hide/Restore tray icon
/// of another application (98, 99). Port of hideWindow, hideApp, unhideApp, getExeIcon and iconProc.
/// </summary>
internal static class WindowHiding
{
    /// <summary>C++ HideInfo: windows hidden together, restored together.</summary>
    sealed class HideInfo
    {
        public readonly List<IntPtr> Windows = new();
        public IntPtr ActiveWnd;
        public int Pid;
        public IntPtr Icon;
        public bool MustDestroyIcon;
    }

    static readonly object sync = new();
    /// <summary>Window hidden by "Hide window" without a parameter (C++ hiddenWin).</summary>
    static IntPtr hiddenWin;
    /// <summary>Windows hidden by "Hide application" (C++ hiddenApp).</summary>
    static readonly HideInfo hiddenApp = new();
    /// <summary>Applications minimized to the tray (C++ trayIconA).</summary>
    static readonly HideInfo[] trayApps = Enumerable.Range(0, 30).Select(_ => new HideInfo()).ToArray();

    /// <summary>True while "Hide window" or "Hide application" keeps something hidden (the C++ shows another tray icon then).</summary>
    public static bool HasHiddenWindows
    {
        get { lock (sync) return hiddenWin != IntPtr.Zero || hiddenApp.Windows.Count > 0; }
    }

    /// <summary>25: hide or unhide a window.</summary>
    public static void HideWindowCommand(CommandContext c)
    {
        bool restored = false;
        lock (sync)
        {
            if (hiddenWin != IntPtr.Zero)
            {
                ShowWindowAsync(hiddenWin, SW_SHOW);
                SetForegroundWindow(hiddenWin);
                hiddenWin = IntPtr.Zero;
                restored = true;
            }
        }
        if (!restored)
        {
            // GetWindow may show a message, so it is called outside the lock
            var w = WindowCommands.GetWindow(c, c.Param);
            lock (sync)
            {
                if (c.Param.Length == 0) hiddenWin = w;
                if (w != IntPtr.Zero) ShowWindowAsync(w, IsWindowVisible(w) ? SW_HIDE : SW_SHOW);
            }
        }
        c.Host.StateChanged();
    }

    /// <summary>101: hide all windows of an application, or restore them.</summary>
    public static void HideApplicationCommand(CommandContext c)
    {
        bool restored = false;
        lock (sync)
        {
            if (hiddenApp.Windows.Count > 0)
            {
                UnhideApp(hiddenApp);
                restored = true;
            }
        }
        if (!restored)
        {
            var w = WindowCommands.GetWindow(c, c.Param);
            lock (sync) HideApp(w, hiddenApp, true);
        }
        c.Host.StateChanged();
    }

    /// <summary>102 (whole application) and 115 (one window): hide and put a restore icon into the tray.</summary>
    public static void MinimizeToTrayCommand(CommandContext c)
    {
        var w = WindowCommands.GetWindow(c, c.Param);
        if (w == IntPtr.Zero) return;
        if (w == c.Host.MainWindowHandle && !c.Runner.TrayIconHidden)
        {
            // HotkeyP window: the C++ calls hideMainWindow(); IAppHost has no "hide main window", so hide it directly
            ShowWindowAsync(w, SW_HIDE);
            return;
        }
        if (!IsWindowVisible(w)) return;
        if (GetClassName(w) == "Shell_TrayWnd") return; // don't hide the taskbar
        string title = GetWindowText(w);
        if (title.Length > 63) title = title[..63];
        HideInfo? info;
        lock (sync)
        {
            info = trayApps.FirstOrDefault(i => i.Pid == 0);
            if (info == null) return;
            HideApp(w, info, c.Command == 102);
            if (info.Pid == 0) return;
            GetExeIcon(info, c.Host.MainWindowHandle);
        }
        var restoreInfo = info;
        c.Host.AddRestoreTrayIcon(title, info.Icon, () =>
        {
            lock (sync) UnhideApp(restoreInfo);
        });
    }

    static void HideWindow(IntPtr w, HideInfo info)
    {
        if (info.Icon == IntPtr.Zero || w == info.ActiveWnd)
        {
            var icon = GetClassLongPtr(w, GCLP_HICONSM);
            if (icon != IntPtr.Zero) info.Icon = icon;
        }
        ShowWindowAsync(w, SW_HIDE);
        info.Windows.Insert(0, w);
    }

    static void HideApp(IntPtr w, HideInfo info, bool all)
    {
        if (w == IntPtr.Zero) return;
        int pid = GetProcessId(w);
        if (pid == 0) return;
        info.Pid = pid;
        info.Icon = IntPtr.Zero;
        info.MustDestroyIcon = false;
        info.ActiveWnd = w;
        if (all)
        {
            // hide all windows which belong to the same process as window w
            bool isExplorer = WindowTools.ProcessIs(pid, "explorer.exe");
            EnumWindows((h, _) =>
            {
                if (IsWindowVisible(h) && GetProcessId(h) == pid)
                {
                    // explorer: only folder windows and dialogs, not the desktop or the taskbar
                    string cls = isExplorer ? GetClassName(h) : "";
                    if (!isExplorer || cls is "CabinetWClass" or "ExploreWClass" or "#32770") HideWindow(h, info);
                }
                return true;
            }, IntPtr.Zero);
        }
        else
        {
            HideWindow(w, info);
        }
    }

    /// <summary>Tray icon for a hidden application: the exe icon, else the window icon, else the HotkeyP icon.</summary>
    static void GetExeIcon(HideInfo info, IntPtr mainWindow)
    {
        var path = GetProcessPath(info.Pid);
        if (path != null)
        {
            var small = new IntPtr[1];
            if (ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                info.Icon = small[0];
                info.MustDestroyIcon = true;
            }
        }
        if (info.Icon == IntPtr.Zero)
        {
            IntPtr icon = IntPtr.Zero;
            var w = info.ActiveWnd;
            if (w != IntPtr.Zero)
            {
                SendMessageTimeout(w, WM_GETICON, ICON_SMALL2, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 1000, out icon);
                if (icon == IntPtr.Zero) icon = GetClassLongPtr(w, GCLP_HICON);
            }
            if (icon == IntPtr.Zero && mainWindow != IntPtr.Zero) icon = GetClassLongPtr(mainWindow, GCLP_HICONSM);
            info.Icon = icon;
        }
    }

    static void UnhideApp(HideInfo info)
    {
        if (info.Pid == 0) return;
        foreach (var w in info.Windows) ShowWindowAsync(w, SW_SHOW);
        info.Windows.Clear();
        if (info.MustDestroyIcon) DestroyIcon(info.Icon);
        info.Icon = IntPtr.Zero;
        info.MustDestroyIcon = false;
        info.Pid = 0;
        SetForegroundWindow(info.ActiveWnd);
    }

    /// <summary>
    /// Shows every window hidden by the window commands. The application must call this when it exits (C++ unhideAll);
    /// it should also remove the restore icons it added through <see cref="IAppHost.AddRestoreTrayIcon"/>.
    /// </summary>
    public static void UnhideAll()
    {
        lock (sync)
        {
            foreach (var info in trayApps) UnhideApp(info);
            UnhideApp(hiddenApp);
            if (hiddenWin != IntPtr.Zero) ShowWindowAsync(hiddenWin, SW_SHOW);
            hiddenWin = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 98 hide icon, 99 restore tray icon. Parameter "icon_id window". The window may not exist yet (e.g. at startup),
    /// so it is retried ten times with increasing delays on a separate thread (C++ iconProc).
    /// </summary>
    public static void TrayIconCommand(CommandContext c, bool hide)
    {
        var (id, name) = WindowParams.StrToIntStr(c.Param);
        new Thread(() =>
        {
            try
            {
                for (int cnt = 0; cnt < 10; cnt++)
                {
                    Thread.Sleep(500 * cnt);
                    var w = WindowCommands.GetWindow(c, name);
                    if (w != IntPtr.Zero && SetIconHidden(w, (uint)id, hide)) break;
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }) { IsBackground = true, Name = "HotkeyP tray icon" }.Start();
    }

    static bool SetIconHidden(IntPtr w, uint id, bool hide)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = w,
            uID = id,
            uFlags = NIF_STATE,
            dwState = hide ? NIS_HIDDEN : 0,
            dwStateMask = NIS_HIDDEN,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
        return Shell_NotifyIcon(NIM_MODIFY, ref data);
    }
}
