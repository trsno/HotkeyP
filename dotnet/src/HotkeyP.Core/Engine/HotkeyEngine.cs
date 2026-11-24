// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HotkeyP.Input;
using HotkeyP.Model;
using static HotkeyP.Native.NativeMethods;
using HotkeyP.Native;

namespace HotkeyP.Engine;

public enum TriggerKind { Down, Up, OnlyDown }

/// <summary>How the low-level keyboard hook is used (same values as the original "hook" setting).</summary>
public enum HookMode { Off = 0, WhenNeeded = 1, MediaKeys = 2, AllKeys = 3 }

public sealed class EngineOptions
{
    public HookMode Hook { get; init; } = HookMode.WhenNeeded;
    /// <summary>How long a mouse button press is held back while waiting for the rest of a button combination.</summary>
    public int MouseDelay { get; init; } = 600;
    /// <summary>Buttons (by <see cref="MouseButton"/> id) that are never held back. By default the left button.</summary>
    public IReadOnlySet<int> NotDelayedButtons { get; init; } = new HashSet<int> { (int)MouseButton.Left };
}

/// <summary>
/// Watches the keyboard and mouse and reports when a hotkey is pressed. Runs a dedicated thread with a message loop,
/// which owns the RegisterHotKey registrations and the low-level hooks.
/// </summary>
public sealed class HotkeyEngine : IDisposable
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LMENU = 0xA4;
    const int TimerMouseDelay = 1;

    readonly Func<Hotkey, bool> isActive;
    readonly Action<Hotkey, TriggerKind> triggered;
    readonly ConcurrentQueue<Action> queue = new();
    readonly Thread thread;
    readonly ManualResetEventSlim started = new();
    uint threadId;

    // state owned by the engine thread
    Hotkey[] hotkeys = [];
    EngineOptions options = new();
    readonly Dictionary<int, List<Hotkey>> registered = new();
    readonly HashSet<Hotkey> hooked = new();
    readonly HashSet<Hotkey> isDown = new();
    IntPtr keyboardHook, mouseHook;
    readonly HookProc keyboardProc, mouseProc;
    int buttons = MouseButtons.Empty, delayedButtons = MouseButtons.Empty, ignoredButtons;
    bool preventWinMenu;
    UIntPtr delayTimer;

    public HotkeyEngine(Func<Hotkey, bool> isActive, Action<Hotkey, TriggerKind> triggered)
    {
        this.isActive = isActive;
        this.triggered = triggered;
        keyboardProc = KeyboardProc;
        mouseProc = MouseProc;
        thread = new Thread(Run) { IsBackground = true, Name = "HotkeyP engine" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        started.Wait();
    }

    /// <summary>All hotkeys are off except the commands that switch them back on.</summary>
    public bool DisableAll { get; set; }
    public bool DisableKeyboard { get; set; }
    public bool DisableMouse { get; set; }
    bool paused;

    /// <summary>
    /// Hotkeys are released (unregistered and not matched by the hooks), e.g. while the user records a new key in the editor.
    /// </summary>
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value;
            Invoke(() => { if (paused) UnregisterAll(); else Register(); });
        }
    }

    Action<int, int, Modifiers>? capture;
    readonly bool[] captureDown = new bool[256];

    /// <summary>
    /// Records keys for the hotkey editor: every key press (with the modifiers held) is reported to <paramref name="onKey"/>
    /// as (virtual key, scan code, modifiers) and swallowed, so even Win+E or Alt+Tab can be recorded. Pass null to stop.
    /// The callback runs on the engine thread.
    /// </summary>
    public void Capture(Action<int, int, Modifiers>? onKey) => Invoke(() =>
    {
        capture = onKey;
        Array.Clear(captureDown);
        if (onKey != null && keyboardHook == IntPtr.Zero)
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(null), 0);
        if (onKey == null) Register();
    });

    bool OnCapture(bool up, int vk, int scan)
    {
        captureDown[vk & 255] = !up;
        if (up) return true;
        bool d(int k) => captureDown[k];
        var mod = Modifiers.None;
        if (d(0xA0) || d(0xA1) || d(VK_SHIFT)) mod |= Modifiers.Shift;
        if (d(0xA2) || d(0xA3) || d(VK_CONTROL)) mod |= Modifiers.Control;
        if (d(0xA4) || d(0xA5) || d(VK_MENU)) mod |= Modifiers.Alt;
        if (d(VK_LWIN) || d(VK_RWIN)) mod |= Modifiers.Win;
        // modifier presses are reported too, so the editor can show "Ctrl+..." while the user is still pressing keys
        try { capture!(vk, scan, mod); } catch (Exception ex) { Log.Error(ex); }
        return true;
    }

    readonly bool[] blockedKeys = new bool[256];

    /// <summary>Keys that the keyboard hook swallows (command "Disable key"). Installs the hook if needed.</summary>
    public void SetKeyBlocked(int vk, bool blocked) => Invoke(() =>
    {
        blockedKeys[vk & 255] = blocked;
        Register();
    });

    public bool IsKeyBlocked(int vk) => blockedKeys[vk & 255];

    public void SetHotkeys(IEnumerable<Hotkey> list, EngineOptions? opts = null)
    {
        var copy = list.ToArray();
        Invoke(() =>
        {
            hotkeys = copy;
            if (opts != null) options = opts;
            Register();
        });
    }

    /// <summary>Re-installs the hooks; Windows silently removes a hook that responded too slowly.</summary>
    public void ReloadHooks() => Invoke(() => { Unhook(); Register(); });

    void Invoke(Action a)
    {
        queue.Enqueue(a);
        PostThreadMessage(threadId, WM_APP, 0, 0);
    }

    void Run()
    {
        threadId = GetCurrentThreadId();
        // create the message queue before anyone posts to it
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        started.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            switch (msg.message)
            {
                case WM_APP:
                    while (queue.TryDequeue(out var a))
                    {
                        try { a(); } catch (Exception ex) { Log.Error(ex); }
                    }
                    break;
                case WM_HOTKEY:
                    OnRegisteredHotkey((int)msg.wParam);
                    break;
                case 0x113: // WM_TIMER
                    if (msg.wParam == (IntPtr)(nuint)delayTimer) UnDelayButtons();
                    break;
                default:
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                    break;
            }
        }
        UnregisterAll();
        Unhook();
    }

    void Register()
    {
        UnregisterAll();
        if (paused) return;
        hooked.Clear();
        bool needKeyboardHook = options.Hook != HookMode.Off || blockedKeys.Any(b => b), needMouseHook = false;
        foreach (var hk in hotkeys)
        {
            if (hk.VKey == Hotkey.VkMouse) { needMouseHook = true; continue; }
            if (hk.VKey <= 0 || hk.VKey >= 256) continue;
            if (KeyNeedsHook(hk.VKey, hk.Modifiers)) { hooked.Add(hk); continue; }
            int id = ((int)hk.Modifiers << 8) | hk.VKey;
            if (registered.TryGetValue(id, out var same)) { same.Add(hk); continue; }
            if (RegisterHotKey(IntPtr.Zero, id, (uint)hk.Modifiers, (uint)hk.VKey)) registered[id] = [hk];
            else
            {
                // already taken by another program, the hook can still catch it
                hooked.Add(hk);
                needKeyboardHook = true;
            }
        }
        if (needKeyboardHook && keyboardHook == IntPtr.Zero)
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(null), 0);
        if (!needKeyboardHook && capture == null) UnhookKeyboard();
        PhysicalKeys.HookActive = keyboardHook != IntPtr.Zero;
        if (needMouseHook && mouseHook == IntPtr.Zero)
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
        if (!needMouseHook && mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
    }

    bool KeyNeedsHook(int vk, Modifiers mod)
    {
        if (options.Hook == HookMode.Off) return false;
        if (options.Hook == HookMode.AllKeys || options.Hook == HookMode.MediaKeys && vk is >= 0xA6 and <= 0xB9) return true;
        if (vk is VK_LWIN or VK_RWIN or 0x5D /*apps*/ or >= 0xA0 and <= 0xA5) return true;
        if ((mod & Modifiers.Win) == 0) return false;
        return mod == Modifiers.Win || vk is >= '0' and <= '9'
            || mod == (Modifiers.Win | Modifiers.Control) && vk is 'C' or 'D' or 'F' or 'P' or 'Q' or 0x08 or 0x73 or 0x25 or 0x0D or 0x27 or 0x20
            || mod == (Modifiers.Win | Modifiers.Shift) && vk is 'C' or 'M' or 'S' or 'T' or 'V' or 0x28 or 0x25 or 0x27 or 0x20 or 0x26
            || mod == (Modifiers.Win | Modifiers.Alt) && vk is 'B' or 'R' or 0x28 or 0x2C or 0x26
            || mod == (Modifiers.Win | Modifiers.Control | Modifiers.Shift) && vk == 'B';
    }

    void UnregisterAll()
    {
        foreach (var id in registered.Keys) UnregisterHotKey(IntPtr.Zero, id);
        registered.Clear();
    }

    void UnhookKeyboard()
    {
        if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        PhysicalKeys.HookActive = false;
    }

    void Unhook()
    {
        UnhookKeyboard();
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
    }

    bool Allowed(Hotkey hk)
    {
        if (Paused) return false;
        if (DisableAll) return hk.Command == 87;
        if (hk.VKey == Hotkey.VkMouse && DisableMouse) return hk.Command == 88;
        if (hk.VKey < 256 && DisableKeyboard) return hk.Command == 97;
        return true;
    }

    void OnRegisteredHotkey(int id)
    {
        if (!registered.TryGetValue(id, out var list)) return;
        var active = list.Where(h => Allowed(h) && isActive(h)).ToList();
        if (active.Count == 0)
        {
            // not for this window: give the key to the active application
            PassThrough(id);
            return;
        }
        foreach (var hk in active) Fire(hk, TriggerKind.OnlyDown);
    }

    /// <summary>Sends the key combination on as if HotkeyP had not caught it.</summary>
    void PassThrough(int id)
    {
        int vk = id & 0xff;
        var mod = (Modifiers)(id >> 8);
        UnregisterHotKey(IntPtr.Zero, id);
        InputSender.KeyDown(vk);
        InputSender.KeyUp(vk);
        RegisterHotKey(IntPtr.Zero, id, (uint)mod, (uint)vk);
    }

    void Fire(Hotkey hk, TriggerKind kind)
    {
        try { triggered(hk, kind); } catch (Exception ex) { Log.Error(ex); }
    }

    static bool Held(int vk) => GetAsyncKeyState(vk) < 0;

    static bool ModifiersMatch(Modifiers mod) =>
        Held(VK_SHIFT) == ((mod & Modifiers.Shift) != 0) &&
        Held(VK_CONTROL) == ((mod & Modifiers.Control) != 0) &&
        Held(VK_MENU) == ((mod & Modifiers.Alt) != 0) &&
        (Held(VK_LWIN) || Held(VK_RWIN)) == ((mod & Modifiers.Win) != 0);

    /// <summary>Lone Win or Alt released after a hotkey would open the Start menu or a window menu; a Ctrl tap prevents it.</summary>
    static void TapCtrl()
    {
        InputSender.KeyDown(VK_CONTROL);
        InputSender.KeyUp(VK_CONTROL);
    }

    // ---------------------------------------------------------------- keyboard hook

    IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (k.dwExtraInfo != ExtraInfoIgnore && OnKey((int)wParam, (int)k.vkCode, (int)((k.scanCode << 16) | (k.flags << 24))))
                return 1;
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    bool OnKey(int msg, int vk, int scan)
    {
        bool up = msg is WM_KEYUP or WM_SYSKEYUP;
        PhysicalKeys.Set(vk, !up);
        if (capture != null) return OnCapture(up, vk, scan);
        if (blockedKeys[vk & 255]) return true;

        if (preventWinMenu && up && vk is VK_LWIN or VK_RWIN or VK_LMENU)
        {
            preventWinMenu = false;
            TapCtrl();
        }
        if (hooked.Count == 0) return false;

        bool handled = false;
        foreach (var hk in hooked)
        {
            if (hk.VKey != vk || ((hk.ScanCode ^ scan) & 0x1ff0000) != 0 && hk.ScanCode != 0) continue;
            if (up)
            {
                if (!isDown.Remove(hk)) continue;
                Fire(hk, TriggerKind.Up);
                handled = true;
                continue;
            }
            if (!ModifiersMatch(hk.Modifiers) || !Allowed(hk) || !isActive(hk)) continue;
            if ((hk.Modifiers & (Modifiers.Win | Modifiers.Alt)) != 0 && (hk.Modifiers & ~(Modifiers.Win | Modifiers.Alt)) == 0) TapCtrl();
            isDown.Add(hk);
            Fire(hk, TriggerKind.Down);
            handled = true;
        }
        return handled;
    }

    // ---------------------------------------------------------------- mouse hook

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == 0 && (int)wParam != 0x200 /*WM_MOUSEMOVE*/)
        {
            var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (m.dwExtraInfo != ExtraInfoIgnore && OnMouse((int)wParam, (int)(m.mouseData >> 16)))
                return 1;
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    bool OnMouse(int msg, int data)
    {
        int down = 15, up = 15;
        switch (msg)
        {
            case WM_LBUTTONDOWN: down = (int)MouseButton.Left; break;
            case WM_LBUTTONUP: up = (int)MouseButton.Left; break;
            case WM_RBUTTONDOWN: down = (int)MouseButton.Right; break;
            case WM_RBUTTONUP: up = (int)MouseButton.Right; break;
            case WM_MBUTTONDOWN: down = (int)MouseButton.Middle; break;
            case WM_MBUTTONUP: up = (int)MouseButton.Middle; break;
            case WM_XBUTTONDOWN: down = data == 2 ? (int)MouseButton.X2 : (int)MouseButton.X1; break;
            case WM_XBUTTONUP: up = data == 2 ? (int)MouseButton.X2 : (int)MouseButton.X1; break;
            case WM_MOUSEWHEEL:
            case WM_MOUSEHWHEEL:
            {
                short delta = (short)data;
                var wheel = msg == WM_MOUSEWHEEL
                    ? (delta < 0 ? MouseButton.WheelDown : MouseButton.WheelUp)
                    : (delta < 0 ? MouseButton.WheelLeft : MouseButton.WheelRight);
                int seq = MouseButtons.Down((int)wheel, buttons);
                if (FireMouse(seq))
                {
                    IgnoreDelayed();
                    return true;
                }
                if (UnDelayButtons())
                {
                    Invoke(() => InputSender.MouseDown(wheel));
                    return true;
                }
                return false;
            }
        }
        if (down < 15)
        {
            buttons = MouseButtons.Down(down, buttons);
            if ((delayedButtons != MouseButtons.Empty || !options.NotDelayedButtons.Contains(down)) && IsPartOfMouseHotkey(buttons))
            {
                // wait whether the user presses the rest of a button combination
                delayedButtons = MouseButtons.Down(down, delayedButtons);
                delayTimer = SetTimer(IntPtr.Zero, delayTimer, (uint)options.MouseDelay, IntPtr.Zero);
                return true;
            }
            if (FireMouse(buttons))
            {
                ignoredButtons |= 1 << down;
                IgnoreDelayed();
                return true;
            }
            if (UnDelayButtons())
            {
                int b = down;
                Invoke(() => InputSender.MouseDown((MouseButton)b));
                return true;
            }
        }
        if (up < 15)
        {
            buttons = MouseButtons.Up(up, buttons);
            if (UnDelayButtons())
            {
                int b = up;
                Invoke(() => InputSender.MouseUp((MouseButton)b));
                return true;
            }
            if ((ignoredButtons & (1 << up)) != 0)
            {
                ignoredButtons &= ~(1 << up);
                return true;
            }
        }
        return false;
    }

    void IgnoreDelayed()
    {
        foreach (int b in MouseButtons.Enumerate(delayedButtons)) ignoredButtons |= 1 << b;
        delayedButtons = MouseButtons.Empty;
        KillDelayTimer();
    }

    void KillDelayTimer()
    {
        if (delayTimer != UIntPtr.Zero) { KillTimer(IntPtr.Zero, delayTimer); delayTimer = UIntPtr.Zero; }
    }

    /// <summary>The held-back presses were not a hotkey after all: send them on, in the original order.</summary>
    bool UnDelayButtons()
    {
        if (delayedButtons == MouseButtons.Empty) return false;
        KillDelayTimer();
        var list = MouseButtons.Enumerate(delayedButtons).ToList();
        delayedButtons = MouseButtons.Empty;
        Invoke(() => { foreach (int b in list) InputSender.MouseDown((MouseButton)b); });
        return true;
    }

    bool IsPartOfMouseHotkey(int seq) => hotkeys.Any(hk =>
        hk.VKey == Hotkey.VkMouse && MouseButtons.IsPrefix(hk.ScanCode, seq) && ModifiersMatch(hk.Modifiers) && Allowed(hk) && isActive(hk));

    bool FireMouse(int seq)
    {
        bool any = false;
        foreach (var hk in hotkeys)
        {
            if (hk.VKey != Hotkey.VkMouse || !MouseButtons.Matches(hk.ScanCode, seq)) continue;
            if (!ModifiersMatch(hk.Modifiers) || !Allowed(hk) || !isActive(hk)) continue;
            if ((hk.Modifiers & (Modifiers.Win | Modifiers.Alt)) != 0 && (hk.Modifiers & ~(Modifiers.Win | Modifiers.Alt)) == 0)
                preventWinMenu = true;
            Fire(hk, TriggerKind.OnlyDown);
            any = true;
        }
        return any;
    }

    public void Dispose()
    {
        PostThreadMessage(threadId, WM_QUIT, 0, 0);
        thread.Join(2000);
    }

    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint elapse, IntPtr proc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hWnd, UIntPtr id);
}
