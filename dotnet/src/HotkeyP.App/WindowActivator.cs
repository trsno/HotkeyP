// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HotkeyP.App;

/// <summary>Brings HotkeyP windows to the front even when another program is active (e.g. after a hotkey).</summary>
static class WindowActivator
{
    public static void Bring(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) { w.Activate(); return; }
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        if (fgThread != me && fgThread != 0)
        {
            // Windows only lets the foreground thread change the foreground window; borrow its input state
            AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(h);
            AttachThreadInput(me, fgThread, false);
        }
        else SetForegroundWindow(h);
        w.Activate();
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
