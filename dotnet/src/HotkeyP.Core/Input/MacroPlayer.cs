// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using static HotkeyP.Native.NativeMethods;
using static HotkeyP.Input.InputSender;

namespace HotkeyP.Input;

/// <summary>Tracks which keys the user physically holds; updated by the keyboard hook.</summary>
public static class PhysicalKeys
{
    static readonly bool[] down = new bool[256];
    public static bool HookActive { get; internal set; }
    public static bool IsDown(int vk) => HookActive ? down[vk & 255] : GetAsyncKeyState(vk) < 0;
    internal static void Set(int vk, bool isDown) => down[vk & 255] = isDown;
}

/// <summary>Plays macros produced by <see cref="MacroParser"/>.</summary>
public sealed class MacroPlayer
{
    static readonly int[] ShiftKeys = [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN];
    static readonly int[] PhysicalModifiers = [VK_RMENU, VK_LSHIFT, VK_RSHIFT, VK_RCONTROL, VK_LMENU, VK_LWIN, VK_RWIN, VK_LCONTROL];

    readonly IntPtr target;
    readonly bool[] shifts = new bool[ShiftKeys.Length];
    readonly bool[] heldBefore = new bool[PhysicalModifiers.Length];

    /// <param name="target">Window that receives posted key messages, or zero to send global input.</param>
    public MacroPlayer(IntPtr target = default) => this.target = target;

    /// <summary>Serializes macros, so two hotkeys never interleave their keys.</summary>
    static readonly object playLock = new();

    public static void Play(string macro, IntPtr target = default, KeyAction? force = null)
        => new MacroPlayer(target).Play(MacroParser.Parse(macro), force);

    public void Play(IReadOnlyList<MacroStep> steps, KeyAction? force = null)
    {
        lock (playLock)
        {
            int capslock = GetKeyState(VK_CAPITAL) & 1;
            bool rep = false;
            ReleaseHeldModifiers();
            if (target != IntPtr.Zero) Thread.Sleep(1);
            foreach (var step in steps)
            {
                switch (step)
                {
                    case KeyStep k:
                        if (k.VKey == VK_CAPITAL) capslock = 0;
                        Press(k.VKey, k.Action == KeyAction.Press && force.HasValue ? force.Value : k.Action);
                        break;
                    case CharStep c:
                        TypeChar(c.Char, force ?? c.Action, ref capslock);
                        break;
                    case MouseStep m:
                        var a = m.Action == KeyAction.Press && force.HasValue ? force.Value : m.Action;
                        if (a != KeyAction.Up) MouseDown(m.Button);
                        if (a != KeyAction.Down) MouseUp(m.Button);
                        break;
                    case WaitForKeysStep: WaitForKeysReleased(); break;
                    case ShowStep:
                        if (target != IntPtr.Zero) SetForegroundWindow(target);
                        Thread.Sleep(10);
                        break;
                    case SleepStep s: Thread.Sleep(s.Milliseconds); break;
                    case RepeatStep: rep = true; break;
                    case DoubleClickStep: InputSender.DoubleClick(); break;
                }
            }
            ShiftsUp();
            if (capslock == 2) Press(VK_CAPITAL, KeyAction.Press);
            if (rep || PhysicalKeys.HookActive) RestoreHeldModifiers();
        }
    }

    void TypeChar(char ch, KeyAction action, ref int capslock)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        int vk = VkKeyScanEx(ch, layout);
        if (vk == -1 || (vk & 0xff) == 0xff)
        {
            if (action != KeyAction.Up) Unicode(ch);
            return;
        }
        if (action != KeyAction.Up)
        {
            if (capslock == 1)
            {
                if ((vk & 0xff) is >= 'A' and <= 'Z' && (vk & 0x600) == 0) vk ^= 0x100; // invert shift
                else if (vk != VK_SPACE) { Press(VK_CAPITAL, KeyAction.Press); capslock = 2; }
            }
            if ((vk & 0x100) != 0) ModifierDown(VK_SHIFT);
            if ((vk & 0x200) != 0) ModifierDown(VK_CONTROL);
            if ((vk & 0x400) != 0) ModifierDown(VK_MENU);
        }
        Press(vk & 0xff, action);
    }

    /// <summary>Modifier keys stay down until the next ordinary key is released, so "\^C" means Ctrl+C.</summary>
    void Press(int vk, KeyAction action)
    {
        switch (action)
        {
            case KeyAction.Press:
                ModifierDown(vk);
                if (!IsShiftKey(vk)) Up(vk);
                break;
            case KeyAction.Up:
                Up(vk);
                break;
            case KeyAction.Down:
                Down(vk);
                break;
        }
    }

    void ModifierDown(int vk)
    {
        int i = Array.IndexOf(ShiftKeys, vk);
        if (i >= 0) shifts[i] = true;
        Down(vk);
    }

    void Up(int vk)
    {
        Up1(vk);
        ShiftsUp();
    }

    void ShiftsUp()
    {
        for (int i = 0; i < shifts.Length; i++)
            if (shifts[i]) { shifts[i] = false; Up1(ShiftKeys[i]); }
    }

    static bool IsShiftKey(int vk) => Array.IndexOf(ShiftKeys, vk) >= 0;

    bool AltHeld => shifts[2];

    void Down(int vk)
    {
        if (target == IntPtr.Zero || IsShiftKey(vk)) KeyDown(vk);
        if (target != IntPtr.Zero)
        {
            PostMessage(target, AltHeld ? WM_SYSKEYDOWN : WM_KEYDOWN, vk, 1 | (VkToScan(vk) << 16));
            Thread.Sleep(IsShiftKey(vk) ? 10 : 1);
        }
    }

    void Up1(int vk)
    {
        if (target == IntPtr.Zero || IsShiftKey(vk)) KeyUp(vk);
        if (target != IntPtr.Zero)
        {
            PostMessage(target, AltHeld ? WM_SYSKEYUP : WM_KEYUP, vk, unchecked((int)0xC0000001) | (VkToScan(vk) << 16));
            Thread.Sleep(1);
        }
    }

    /// <summary>Releases modifiers the user holds for the hotkey itself (e.g. Ctrl of Ctrl+F1), so they do not mix into the macro.</summary>
    void ReleaseHeldModifiers()
    {
        for (int i = 0; i < PhysicalModifiers.Length; i++)
        {
            heldBefore[i] = GetAsyncKeyState(PhysicalModifiers[i]) < 0;
            if (heldBefore[i]) KeyUp(PhysicalModifiers[i]);
        }
    }

    void RestoreHeldModifiers()
    {
        bool lctrl = false;
        for (int i = 0; i < PhysicalModifiers.Length; i++)
        {
            bool held = heldBefore[i] && PhysicalKeys.IsDown(PhysicalModifiers[i]);
            if (held) { KeyDown(PhysicalModifiers[i]); if (PhysicalModifiers[i] == VK_LCONTROL) lctrl = true; }
        }
        if ((GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0 || GetAsyncKeyState(VK_LMENU) < 0) && !lctrl)
        {
            // a lone Win or Alt release would open the Start menu or the window menu
            KeyDown(VK_CONTROL);
            KeyUp(VK_CONTROL);
        }
    }

    static void PostMessage(IntPtr hWnd, int msg, int wParam, int lParam)
        => Native.NativeMethods.PostMessage(hWnd, msg, wParam, lParam);
}
