// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.ComponentModel;
using System.Diagnostics;
using HotkeyP.Engine;
using HotkeyP.Input;
using HotkeyP.Model;
using HotkeyP.Native;
using static HotkeyP.Commands.WindowNative;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>Commands of the Window group. Ported from command() in Commands.cpp and the helpers it calls.</summary>
static class WindowCommands
{
    public static void Register(CommandTable t)
    {
        t.Add(7, c => SysCommandToggle(c, IsZoomed, SC_MAXIMIZE));
        t.Add(8, c => SysCommandToggle(c, IsIconic, SC_MINIMIZE));
        t.Add(9, Close);
        t.Add(10, AlwaysOnTop);
        t.Add(11, Terminate);
        t.Add(20, c => SetPriority(c, WindowParams.ParsePriority(c.Param)));
        t.Add([21, 73], CommandToWindow);
        t.Add(25, WindowHiding.HideWindowCommand);
        t.Add([28, 29, 30, 31, 32, 33, 34, 35, 36], Move);
        t.Add([46, 47, 48, 49], c => SetPriority(c, c.Command - 46));
        t.Add([59, 60], c => SetPriority(c, c.Command - 55));
        t.Add(65, _ => MinimizeOthers());
        t.Add(66, Information);
        t.Add([71, 72], c =>
        {
            var w = GetWindow(c, c.Param);
            if (w != IntPtr.Zero) PostMessage(w, WM_SYSCOMMAND, c.Command == 71 ? SC_MOVE : SC_SIZE, IntPtr.Zero);
        });
        t.Add(77, c =>
        {
            var (opacity, window) = WindowParams.StrToIntStr(c.Param);
            SetOpacity(GetWindow(c, window), opacity);
        });
        t.Add(81, _ => MacroPlayer.Play(@"\rep\win\D"));
        t.Add([83, 84], TaskSwitch.Run);
        t.Add(98, c => WindowHiding.TrayIconCommand(c, hide: true));
        t.Add(99, c => WindowHiding.TrayIconCommand(c, hide: false));
        t.Add(101, WindowHiding.HideApplicationCommand);
        t.Add([102, 115], WindowHiding.MinimizeToTrayCommand);
        t.Add([110, 111], OpacityStep);
        t.Add(112, _ => MaximizeAll());
    }

    /// <summary>Shows every window hidden by Hide window, Hide application and Minimize to tray. Call it when HotkeyP exits.</summary>
    public static void UnhideAll() => WindowHiding.UnhideAll();

    /// <summary>True while Hide window or Hide application keeps a window hidden (the tray icon can show it).</summary>
    public static bool HasHiddenWindows => WindowHiding.HasHiddenWindows;

    // ---------------------------------------------------------------- finding windows

    /// <summary>C++ getActiveWindow: the foreground window, with a message when there is none.</summary>
    internal static IntPtr ActiveWindow(CommandContext c)
    {
        var w = GetForegroundWindow();
        if (w == IntPtr.Zero) c.Host.ShowMessage("There is no foreground window");
        return w;
    }

    /// <summary>
    /// C++ getWindow2: window by title substring, class or exe name (optionally only in process <paramref name="pid"/>);
    /// empty text means the foreground window.
    /// </summary>
    internal static IntPtr GetWindow(CommandContext c, string? s, int pid = 0, bool quiet = false)
    {
        if (string.IsNullOrEmpty(s))
            return quiet ? GetForegroundWindow() : ActiveWindow(c);
        return WindowTools.Find(s, pid);
    }

    /// <summary>
    /// Windows 7 and later have a "show desktop" button which activates a hidden WorkerW window; closing it must close
    /// the Program Manager instead.
    /// </summary>
    static IntPtr FixWorkerW(IntPtr w)
    {
        if (w != IntPtr.Zero && GetClassName(w) == "WorkerW")
        {
            var w2 = FindWindow("Progman", "Program Manager");
            if (w2 != IntPtr.Zero && GetWindowThreadId(w, IntPtr.Zero) == GetWindowThreadId(w2, IntPtr.Zero)) return w2;
        }
        return w;
    }

    /// <summary>Releases a held mouse button, otherwise the window would not maximize or minimize.</summary>
    static void ForceNoLeftButton()
    {
        if (GetAsyncKeyState(VK_LBUTTON) < 0 || GetAsyncKeyState(VK_RBUTTON) < 0) // both, because they can be swapped
        {
            InputSender.Mouse(MOUSEEVENTF_LEFTUP);
            Thread.Sleep(10);
        }
    }

    // ---------------------------------------------------------------- commands

    /// <summary>7 maximize, 8 minimize: restores the window when it is already in that state.</summary>
    static void SysCommandToggle(CommandContext c, Func<IntPtr, bool> isInState, int sc)
    {
        var w = GetWindow(c, c.Param);
        ForceNoLeftButton();
        if (w != IntPtr.Zero) PostMessage(w, WM_SYSCOMMAND, isInState(w) ? SC_RESTORE : sc, IntPtr.Zero);
    }

    static void Close(CommandContext c)
    {
        var w = FixWorkerW(GetWindow(c, c.Param));
        if (w != IntPtr.Zero) PostMessage(w, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    static void AlwaysOnTop(CommandContext c)
    {
        var w = GetWindow(c, c.Param);
        if (w == IntPtr.Zero) return;
        bool topmost = (GetWindowLongPtr(w, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        SetWindowPos(w, topmost ? HWND_NOTOPMOST : HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_ASYNCWINDOWPOS);
    }

    static void Terminate(CommandContext c)
    {
        var w = GetWindow(c, c.Param);
        if (w == IntPtr.Zero) return;
        bool ok = false;
        int pid = GetProcessId(w);
        if (pid != 0)
        {
            var h = OpenProcess(PROCESS_TERMINATE, false, pid);
            if (h != IntPtr.Zero)
            {
                ok = TerminateProcess(h, unchecked((uint)-1));
                CloseHandle(h);
            }
        }
        if (!ok) c.Host.ShowMessage("Can't terminate process");
    }

    /// <summary>C++ priority(): sets the priority class of the foreground window's process. <paramref name="p"/> indexes <see cref="WindowParams.PriorityNames"/>.</summary>
    static void SetPriority(CommandContext c, int p)
    {
        var w = ActiveWindow(c);
        if (w == IntPtr.Zero) return;
        bool ok = false;
        int pid = GetProcessId(w);
        if (pid != 0)
        {
            var h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                p = Math.Clamp(p, 0, WindowParams.PriorityClasses.Length - 1);
                ok = SetPriorityClass(h, WindowParams.PriorityClasses[p]);
                CloseHandle(h);
            }
        }
        if (!ok) c.Host.ShowMessage("Can't change priority");
    }

    /// <summary>
    /// 21 "Send window command" and 73 "Command to active window": "command_id window [child]". 21 finds the window
    /// anywhere; 73 uses a window expression that must match the foreground window. The child is searched in the
    /// window's process. Port of cmdToWindow.
    /// </summary>
    static void CommandToWindow(CommandContext c)
    {
        if (!c.Param.Contains(' ')) return; // the C++ requires at least two parameters
        var (first, window, child, joined) = WindowTools.SplitWindowParams(c.Param);
        Func<string, bool, IntPtr> find = c.Command == 21
            ? (s, quiet) => GetWindow(c, s, 0, quiet)
            : (s, _) => WindowTools.MatchForeground(s);
        IntPtr w;
        if (joined != null)
        {
            // three unquoted words: "window child" can be one window title
            w = find(joined, true);
            if (w != IntPtr.Zero) child = null;
            else w = find(window, false);
        }
        else
        {
            w = find(window, false);
        }
        if (w == IntPtr.Zero) return;
        if (child != null)
        {
            int pid = GetProcessId(w);
            if (pid != 0) w = GetWindow(c, child, pid);
        }
        if (w != IntPtr.Zero) PostMessage(w, WM_COMMAND, WindowParams.WindowCommandId(first), IntPtr.Zero);
    }

    /// <summary>28..36: moves a window to the center, a corner or an edge of the work area (primary monitor, like the C++).</summary>
    static void Move(CommandContext c)
    {
        var actions = WindowParams.MoveActions(c.Command);
        if (actions == null) return;
        var (ax, ay) = actions.Value;
        var w = GetWindow(c, c.Param);
        if (w == IntPtr.Zero) return;
        var rcd = new RECT();
        SystemParametersInfo(SPI_GETWORKAREA, 0, ref rcd, 0);
        if (!GetWindowRect(w, out var rcw)) return;
        var (x, y) = WindowParams.MovePosition(
            (rcd.Left, rcd.Top, rcd.Right, rcd.Bottom), (rcw.Left, rcw.Top, rcw.Right, rcw.Bottom), ax, ay);
        SetWindowPos(w, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_ASYNCWINDOWPOS);
    }

    static bool IsRectEmpty(RECT rc) => rc.Right <= rc.Left || rc.Bottom <= rc.Top;

    /// <summary>65: minimizes all windows except the foreground window (and its owners).</summary>
    static void MinimizeOthers()
    {
        var keep = GetForegroundWindow();
        for (int depth = 0; depth < 100; depth++)
        {
            var owner = NativeMethods.GetWindow(keep, GW_OWNER);
            if (owner == IntPtr.Zero) break;
            keep = owner;
        }
        EnumWindows((h, _) =>
        {
            long style = GetWindowLongPtr(h, GWL_STYLE);
            if ((style & WS_MINIMIZEBOX) != 0 && IsWindowVisible(h) && NativeMethods.GetWindow(h, GW_OWNER) == IntPtr.Zero && h != keep)
            {
                GetWindowRect(h, out var rc);
                if (IsRectEmpty(rc)) PostMessage(h, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero); // Borland Delphi application
                else ShowWindow(h, SW_MINIMIZE);
            }
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>112: maximizes all windows, the foreground window last.</summary>
    static void MaximizeAll()
    {
        var w = GetForegroundWindow();
        EnumWindows((h, _) => { Maximize(h, w); return true; }, IntPtr.Zero);
        Thread.Sleep(100);
        if (w != IntPtr.Zero)
        {
            Maximize(w, IntPtr.Zero);
            SetForegroundWindow(w);
        }
    }

    /// <summary>C++ enumMaximize.</summary>
    static void Maximize(IntPtr h, IntPtr ignore)
    {
        long style = GetWindowLongPtr(h, GWL_STYLE);
        if ((style & WS_MAXIMIZEBOX) == 0 || !IsWindowVisible(h) || h == ignore) return;
        var owner = NativeMethods.GetWindow(h, GW_OWNER);
        if (owner != IntPtr.Zero)
        {
            if ((GetWindowLongPtr(owner, GWL_STYLE) & WS_SYSMENU) != 0)
            {
                GetWindowRect(owner, out var rc);
                if (IsRectEmpty(rc)) PostMessage(h, WM_SYSCOMMAND, SC_MAXIMIZE, IntPtr.Zero); // Borland Delphi application
            }
        }
        else
        {
            ShowWindowAsync(h, SW_MAXIMIZE);
        }
    }

    /// <summary>66: title, class, process, path, priority and memory of the foreground window.</summary>
    static void Information(CommandContext c)
    {
        var w = ActiveWindow(c);
        if (w == IntPtr.Zero) return;
        string title = GetWindowText(w), cls = GetClassName(w);
        int pid = GetProcessId(w);
        string path = GetProcessPath(pid) ?? "";
        string name = path.Length > 0 ? Path.GetFileName(path) : "";
        string priority = "";
        long memory = 0;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (name.Length == 0) name = p.ProcessName + ".exe";
            memory = p.WorkingSet64;
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                uint d = GetPriorityClass(h);
                CloseHandle(h);
                int i = Array.IndexOf(WindowParams.PriorityClasses, d);
                if (i >= 0) priority = WindowParams.PriorityNames[i];
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        c.Host.ShowMessage($"Title: {title}\r\nWindow Class: {cls}\r\nProcess: {name}\r\nPath: {path}\r\nPriority: {priority}\r\nMemory: {memory / 1024} KB", error: false);
    }

    /// <summary>C++ getOpacity: -1 when there is no window, 255 when the window is not transparent.</summary>
    static int GetOpacity(IntPtr w) => w == IntPtr.Zero ? -1 : WindowTools.GetOpacity(w);

    /// <summary>C++ setOpacity: 0 (invisible) .. 255 (opaque). Unlike WindowTools.SetOpacity it allows 0.</summary>
    static void SetOpacity(IntPtr w, int o)
    {
        if (w == IntPtr.Zero) return;
        long s = GetWindowLongPtr(w, GWL_EXSTYLE);
        if (((s & WS_EX_LAYERED) == 0) != (o == 255)) SetWindowLongPtr(w, GWL_EXSTYLE, (IntPtr)(s ^ WS_EX_LAYERED));
        SetLayeredWindowAttributes(w, 0, unchecked((byte)Math.Min(o, 255)), LWA_ALPHA);
    }

    /// <summary>110 opacity +, 111 opacity -: "[step] [window]", the step is 0..255 units, default 10.</summary>
    static void OpacityStep(CommandContext c)
    {
        var (step, window) = WindowParams.StrToIntStr(c.Param);
        var w = GetWindow(c, window);
        int o = GetOpacity(w);
        if (o < 0) return;
        SetOpacity(w, WindowParams.ChangeOpacity(o, step, c.Command == 110));
    }

    /// <summary>
    /// 83 previous task, 84 next task: Alt+(Shift+)Tab. When triggered by a mouse shortcut with a held button
    /// (e.g. fourth button + wheel), Alt stays down until a mouse button is released, so the task switcher stays open.
    /// </summary>
    static class TaskSwitch
    {
        static readonly object sync = new();
        static bool altDown, watching;
        static HookProc? hookProc; // keeps the delegate alive while the hook is installed

        public static void Run(CommandContext c)
        {
            lock (sync)
            {
                if (!altDown)
                {
                    altDown = true;
                    InputSender.KeyDown(InputSender.VK_MENU);
                }
                if (c.Command == 83) InputSender.KeyDown(InputSender.VK_SHIFT);
                InputSender.KeyDown(0x09);
                InputSender.KeyUp(0x09);
                if (c.Command == 83) InputSender.KeyUp(InputSender.VK_SHIFT);

                if (watching) return;
                if (HoldsMouseButton(c.Hotkey))
                {
                    watching = true;
                    new Thread(WatchButtons) { IsBackground = true, Name = "HotkeyP task switch" }.Start();
                }
                else
                {
                    altDown = false;
                    InputSender.KeyUp(InputSender.VK_MENU);
                }
            }
        }

        /// <summary>A mouse shortcut containing a real button (not only the wheel): the button is still held now.</summary>
        static bool HoldsMouseButton(Hotkey? hk) =>
            hk != null && hk.VKey == Hotkey.VkMouse && MouseButtons.Enumerate(hk.ScanCode).Any(b => b < (int)MouseButton.WheelLeft);

        /// <summary>
        /// The engine's pressed-buttons state is private and the hook may swallow the buttons (so GetAsyncKeyState
        /// does not see them); a separate low-level mouse hook installed after the engine's sees every button-up first.
        /// </summary>
        static void WatchButtons()
        {
            uint threadId = GetCurrentThreadId();
            hookProc = (code, wParam, lParam) =>
            {
                if (code >= 0 && (int)wParam is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP)
                    PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            };
            var hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
            if (hook != IntPtr.Zero)
            {
                // safety: never leave Alt down for long if the button-up is missed
                using var timeout = new Timer(_ => PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero), null, 30000, Timeout.Infinite);
                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                UnhookWindowsHookEx(hook);
            }
            lock (sync)
            {
                if (altDown)
                {
                    altDown = false;
                    InputSender.KeyUp(InputSender.VK_MENU);
                }
                watching = false;
                hookProc = null;
            }
        }
    }
}
