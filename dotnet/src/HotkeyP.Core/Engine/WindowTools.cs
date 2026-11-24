// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Diagnostics;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Engine;

/// <summary>Finding and matching windows, shared by the commands.</summary>
public static class WindowTools
{
    /// <summary>
    /// Window expression used by "to active window" commands: terms are a window class, part of a window title
    /// or an exe name; combine with &amp;, |, ! and parentheses; quote a term with ' to include those characters.
    /// </summary>
    public static bool ForegroundMatches(string? expression) => MatchForeground(expression) != IntPtr.Zero;

    /// <summary>The foreground window when it matches <paramref name="expression"/> (or any window if it is empty), otherwise zero.</summary>
    public static IntPtr MatchForeground(string? expression)
    {
        var w = GetForegroundWindow();
        if (string.IsNullOrEmpty(expression)) return w;
        var p = new ExprParser(expression, GetClassName(w), GetWindowText(w), GetProcessId(w));
        return p.Or() ? w : IntPtr.Zero;
    }

    /// <summary>Best window whose title contains <paramref name="text"/> or whose class equals it. Empty text means the foreground window.</summary>
    public static IntPtr Find(string? text, int pid = 0)
    {
        if (string.IsNullOrEmpty(text)) return GetForegroundWindow();
        IntPtr found = IntPtr.Zero;
        int best = 0;
        EnumWindows((h, _) =>
        {
            if (pid != 0 && GetProcessId(h) != pid) return true;
            int e = 0;
            var title = GetWindowText(h);
            if (title.Contains(text, StringComparison.Ordinal))
            {
                e = 20;
                if (title == text) e += 10;
            }
            if (GetClassName(h) == text) e += 10;
            if (e > 0 && IsWindowVisible(h))
            {
                e += 100;
                if ((GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0) e += 50;
            }
            if (e > best) { best = e; found = h; }
            return true;
        }, IntPtr.Zero);
        return found != IntPtr.Zero ? found : FindProgramWindow(text, 0);
    }

    /// <summary>Main visible window of a running program, by process id or by exe path/name.</summary>
    public static IntPtr FindProgramWindow(string exe, int pid)
    {
        IntPtr found = IntPtr.Zero;
        int pid1 = FindProcess(exe);
        if (pid != 0) found = FindProcessWindow(pid);
        if (pid1 != 0 && pid1 != pid)
        {
            var w = FindProcessWindow(pid1);
            if (w != IntPtr.Zero) found = w;
        }
        return found;
    }

    public static IntPtr FindProcessWindow(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (GetProcessId(h) == pid && IsWindowVisible(h) && GetWindowRect(h, out var rc) && rc.Width > 0 && rc.Height > 0)
            {
                if ((GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0 || found == IntPtr.Zero) found = h;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Process id of a running program, matched by full path, or by file name when <paramref name="exe"/> has no path.</summary>
    public static int FindProcess(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return 0;
        exe = exe.Trim('"');
        string name = Path.GetFileNameWithoutExtension(exe);
        bool hasPath = exe.Contains('\\');
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                if (!hasPath) return p.Id;
                var path = GetProcessPath(p.Id);
                if (path == null || string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) return p.Id;
            }
        }
        return 0;
    }

    /// <summary>True if the process runs from <paramref name="exe"/>, given as a file name or a full path.</summary>
    public static bool ProcessIs(int pid, string exe)
    {
        if (pid == 0) return false;
        var path = GetProcessPath(pid);
        if (path == null) return false;
        return string.Equals(exe, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
            || string.Equals(exe, path, StringComparison.OrdinalIgnoreCase);
    }

    public static int GetOpacity(IntPtr w)
    {
        if ((GetWindowLongPtr(w, GWL_EXSTYLE) & WS_EX_LAYERED) == 0) return 255;
        return GetLayeredWindowAttributes(w, out _, out byte a, out uint f) && (f & LWA_ALPHA) != 0 ? a : 255;
    }

    public static void SetOpacity(IntPtr w, int opacity)
    {
        opacity = Math.Clamp(opacity, 1, 255);
        var ex = GetWindowLongPtr(w, GWL_EXSTYLE);
        if (opacity == 255)
        {
            if ((ex & WS_EX_LAYERED) != 0) SetWindowLongPtr(w, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
            return;
        }
        if ((ex & WS_EX_LAYERED) == 0) SetWindowLongPtr(w, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        SetLayeredWindowAttributes(w, 0, (byte)opacity, LWA_ALPHA);
    }

    /// <summary>Top-level window owning <paramref name="w"/>.</summary>
    public static IntPtr Root(IntPtr w) => w == IntPtr.Zero ? w : GetAncestor(w, GA_ROOT);

    /// <summary>
    /// Splits "first window [child]" used by the "to window" commands. The window and the child can be quoted.
    /// If the window is not quoted and there are three words, "window child" is first tried as one window title.
    /// </summary>
    public static (string First, string Window, string? Child, string? WindowAndChild) SplitWindowParams(string param)
    {
        int sp = param.IndexOf(' ');
        if (sp < 0) return (param, "", null, null);
        string first = param[..sp];
        int i = sp;
        while (i < param.Length && param[i] == ' ') i++;
        string window;
        bool quoted = i < param.Length && param[i] == '"';
        if (quoted)
        {
            int end = param.IndexOf('"', i + 1);
            if (end < 0) end = param.Length;
            window = param[(i + 1)..end];
            i = Math.Min(end + 1, param.Length);
        }
        else
        {
            int end = param.IndexOf(' ', i);
            if (end < 0) end = param.Length;
            window = param[i..end];
            i = end;
        }
        int sepStart = i;
        while (i < param.Length && param[i] == ' ') i++;
        if (i >= param.Length) return (first, window, null, null);
        string child;
        bool childQuoted = param[i] == '"';
        if (childQuoted)
        {
            int end = param.IndexOf('"', i + 1);
            child = param[(i + 1)..(end < 0 ? param.Length : end)];
        }
        else child = param[i..];
        string? joined = !quoted && !childQuoted && sepStart < param.Length && param[sepStart] == ' ' ? window + " " + child : null;
        return (first, window, child, joined);
    }

    sealed class ExprParser(string s, string cls, string title, int pid)
    {
        int i;

        public bool Or()
        {
            bool b = And();
            while (i < s.Length && s[i] == '|') { i++; b |= And(); }
            return b;
        }

        bool And()
        {
            bool b = Unary();
            while (i < s.Length && s[i] == '&') { i++; b &= Unary(); }
            return b;
        }

        bool Unary()
        {
            if (i < s.Length && s[i] == '(')
            {
                i++;
                bool b = Or();
                if (i < s.Length && s[i] == ')') i++;
                return b;
            }
            if (i < s.Length && s[i] == '!') { i++; return !Unary(); }
            string term;
            if (i < s.Length && s[i] == '\'')
            {
                int end = s.IndexOf('\'', i + 1);
                if (end < 0) end = s.Length;
                term = s[(i + 1)..end];
                i = Math.Min(end + 1, s.Length);
            }
            else
            {
                int start = i, par = 0;
                while (i < s.Length && s[i] != '&' && s[i] != '|')
                {
                    if (s[i] == '(') par++;
                    if (s[i] == ')' && par-- == 0) break;
                    i++;
                }
                term = s[start..i];
            }
            return cls == term || title.Contains(term, StringComparison.Ordinal) || ProcessIs(pid, term);
        }
    }
}
