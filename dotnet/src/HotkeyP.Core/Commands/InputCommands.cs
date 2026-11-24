// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using HotkeyP.Engine;
using HotkeyP.Input;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>Keyboard, mouse and text commands.</summary>
static class InputCommands
{
    public static void Register(CommandTable t)
    {
        t.Add(27, c => PlayMacro(c, c.Param));
        t.Add([26, 74, 94], CmdToWindow);
        t.Add(37, MoveMouse);
        t.Add(43, _ => Click(MouseButton.Left));
        t.Add(44, _ => Click(MouseButton.Middle));
        t.Add(45, _ => Click(MouseButton.Right));
        t.Add(79, _ => Click(MouseButton.X1));
        t.Add(80, _ => Click(MouseButton.X2));
        t.Add(76, _ => InputSender.DoubleClick());
        t.Add(75, c => Wheel(c, MOUSEEVENTF_WHEEL));
        t.Add(108, c => Wheel(c, MOUSEEVENTF_HWHEEL));
        t.Add(67, c => InputPaste.Paste(c.Runner, c.Param));
        t.Add(85, ShowText);
        t.Add(86, DisableKeys);
    }

    static void PlayMacro(CommandContext c, string macro, IntPtr target = default, KeyAction? force = null)
    {
        try { MacroPlayer.Play(macro, target, force); }
        catch (MacroSyntaxException ex) { c.Host.ShowMessage(ex.Message); }
    }

    // ---------------------------------------------------------------- keys to window

    /// <summary>
    /// 26 keys to another window, 74 keys to active window, 94 macro to active window.
    /// Parameter: "keys window [child window]". 26 and 74 post the keys to the window, 94 sends global input.
    /// </summary>
    static void CmdToWindow(CommandContext c)
    {
        if (!c.Param.Contains(' ')) return;
        var (keys, window, child, joined) = WindowTools.SplitWindowParams(c.Param);
        Func<string, IntPtr> find = c.Command < 30 ? s => WindowTools.Find(s) : WindowTools.MatchForeground;
        IntPtr w = IntPtr.Zero;
        if (joined != null)
        {
            // "keys some title" where "some title" is one window title
            w = find(joined);
            if (w != IntPtr.Zero) child = null;
        }
        if (w == IntPtr.Zero) w = find(window);
        if (w == IntPtr.Zero) return;
        if (child != null)
        {
            int pid = GetProcessId(w);
            if (pid != 0) w = WindowTools.Find(child, pid);
            if (w == IntPtr.Zero) return;
        }
        KeyAction? force = c.Command == 94 ? ForceFor(c.Kind) : null;
        PlayMacro(c, keys, c.Command > 90 ? IntPtr.Zero : w, force);
    }

    /// <summary>Hotkey pressed = keys down only (C++ command 904), released = keys up only (906).</summary>
    internal static KeyAction? ForceFor(TriggerKind kind) => kind switch
    {
        TriggerKind.Down => KeyAction.Down,
        TriggerKind.Up => KeyAction.Up,
        _ => null,
    };

    // ---------------------------------------------------------------- mouse

    /// <summary>"dx dy" moves the cursor relatively (without pointer acceleration); anything else centers it on the primary screen.</summary>
    static void MoveMouse(CommandContext c)
    {
        if (!TryParseMouseMove(c.Param, out int x, out int y))
        {
            InputSender.Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, 0, 32767, 32767);
        }
        else if (GetCursorPos(out var pt))
        {
            SetCursorPos(pt.X + x, pt.Y + y);
        }
        else
        {
            InputSender.Mouse(MOUSEEVENTF_MOVE, 0, x, y);
        }
    }

    /// <summary>Like sscanf("%d %d"): two integers separated by optional white space; trailing text is ignored.</summary>
    internal static bool TryParseMouseMove(string s, out int x, out int y)
    {
        int i = 0;
        y = 0;
        return TryScanInt(s, ref i, out x) && TryScanInt(s, ref i, out y);
    }

    static bool TryScanInt(string s, ref int i, out int value)
    {
        value = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
        int digits = i;
        long v = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { v = Math.Min(v * 10 + (s[i] - '0'), int.MaxValue); i++; }
        if (i == digits) { i = start; return false; }
        value = (int)(neg ? -v : v);
        return true;
    }

    /// <summary>
    /// Port of strToIntStr: the leading integer of the parameter and the text after it (spaces skipped).
    /// A number followed by ':' is not a number (e.g. a drive), then 0 and the whole parameter are returned.
    /// </summary>
    internal static int LeadingInt(string param, out string rest)
    {
        int i = 0;
        if (!TryScanInt(param, ref i, out int v))
        {
            rest = param;
            return 0;
        }
        if (i < param.Length && param[i] == ':')
        {
            rest = param;
            return 0;
        }
        while (i < param.Length && param[i] == ' ') i++;
        rest = param[i..];
        return v;
    }

    /// <summary>Wheel delta of 75 and 108: the parameter is a raw delta (120 = one notch), default one notch down/left.</summary>
    internal static int WheelDelta(string param)
    {
        int d = LeadingInt(param, out _);
        return d != 0 ? d : -WHEEL_DELTA;
    }

    static void Click(MouseButton b) => WithoutModifiers(() => InputSender.Click(b));

    static void Wheel(CommandContext c, uint flag)
    {
        int delta = WheelDelta(c.Param);
        WithoutModifiers(() => InputSender.Mouse(flag, delta));
    }

    static readonly int[] PhysicalModifiers =
    [
        InputSender.VK_RMENU, InputSender.VK_LSHIFT, InputSender.VK_RSHIFT, InputSender.VK_RCONTROL,
        InputSender.VK_LMENU, InputSender.VK_LWIN, InputSender.VK_RWIN, InputSender.VK_LCONTROL,
    ];

    /// <summary>
    /// Port of forceNoShift/restoreShift: releases the modifiers the user holds for the hotkey, runs the action
    /// and presses them again, so Ctrl+hotkey does not become Ctrl+wheel.
    /// </summary>
    static void WithoutModifiers(Action action)
    {
        var held = new bool[PhysicalModifiers.Length];
        for (int i = 0; i < held.Length; i++)
        {
            held[i] = GetAsyncKeyState(PhysicalModifiers[i]) < 0;
            if (held[i]) InputSender.KeyUp(PhysicalModifiers[i]);
        }
        try { action(); }
        finally
        {
            bool hook = PhysicalKeys.HookActive;
            if (hook)
            {
                // the hook knows which keys the user still holds
                for (int i = 0; i < held.Length; i++) held[i] = PhysicalKeys.IsDown(PhysicalModifiers[i]);
            }
            else if (held[0])
            {
                // right Alt also reports left Ctrl (AltGr)
                held[Array.IndexOf(PhysicalModifiers, InputSender.VK_LCONTROL)] = false;
            }
            bool lctrl = false;
            for (int i = 0; i < held.Length; i++)
            {
                if (!held[i]) continue;
                InputSender.KeyDown(PhysicalModifiers[i]);
                if (PhysicalModifiers[i] == InputSender.VK_LCONTROL) lctrl = true;
            }
            if ((GetAsyncKeyState(InputSender.VK_LWIN) < 0 || GetAsyncKeyState(InputSender.VK_RWIN) < 0
                 || GetAsyncKeyState(InputSender.VK_LMENU) < 0) && !lctrl)
            {
                // a lone Win or Alt release would open the Start menu or the window menu
                InputSender.KeyDown(InputSender.VK_CONTROL);
                InputSender.KeyUp(InputSender.VK_CONTROL);
            }
            if (hook)
            {
                // the user could release a key just before it was pressed again
                for (int i = 0; i < held.Length; i++)
                    if (held[i] && !PhysicalKeys.IsDown(PhysicalModifiers[i])) InputSender.KeyUp(PhysicalModifiers[i]);
            }
        }
    }

    // ---------------------------------------------------------------- show text

    /// <summary>
    /// "[milliseconds] text": a leading number of at least 100 is the duration, otherwise the duration is 6 s
    /// and the whole parameter is the text (C++ case 85).
    /// </summary>
    internal static (int Milliseconds, string Text) ParseShowText(string param)
    {
        int ms = LeadingInt(param, out string rest);
        return ms < 100 ? (6000, param) : (ms, rest);
    }

    static void ShowText(CommandContext c)
    {
        var (ms, text) = ParseShowText(c.Param);
        // pressing the hotkey again while the same text is shown hides it (handled by the host)
        c.Host.ShowOsd(OsdKind.Text, InputPaste.FormatText(text), null, ms);
    }

    // ---------------------------------------------------------------- disable keys

    /// <summary>
    /// Virtual keys listed in the parameter, written like a macro (\LWIN\APPS or plain characters).
    /// Characters that have no key on the layout, mouse buttons and macro commands are ignored.
    /// </summary>
    internal static List<int> ParseKeyList(string param, Func<char, int> charToVk)
    {
        var result = new List<int>();
        foreach (var step in MacroParser.Parse(param))
        {
            int vk = step switch
            {
                KeyStep k => k.VKey,
                CharStep ch => charToVk(ch.Char),
                _ => -1,
            };
            if (vk == -1) continue;
            vk &= 0xFF;
            if (!result.Contains(vk)) result.Add(vk);
        }
        return result;
    }

    static int CharToVk(char ch)
        => VkKeyScanEx(ch, GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _)));

    /// <summary>Blocks the listed keys; if any of them is already blocked, unblocks them all instead.</summary>
    static void DisableKeys(CommandContext c)
    {
        var engine = c.Runner.Engine;
        if (engine == null) return;
        List<int> keys;
        try { keys = ParseKeyList(c.Param, CharToVk); }
        catch (MacroSyntaxException ex)
        {
            c.Host.ShowMessage(ex.Message);
            return;
        }
        bool anyBlocked = keys.Any(engine.IsKeyBlocked);
        foreach (int vk in keys) engine.SetKeyBlocked(vk, !anyBlocked);
    }
}
