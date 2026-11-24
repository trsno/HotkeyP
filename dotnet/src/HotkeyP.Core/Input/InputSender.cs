// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Runtime.InteropServices;
using static HotkeyP.Native.NativeMethods;
using HotkeyP.Native;

namespace HotkeyP.Input;

/// <summary>Synthesizes keyboard and mouse input. Every event is tagged so HotkeyP's own hooks ignore it.</summary>
public static class InputSender
{
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_SPACE = 0x20,
        VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
        VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_NUMPAD0 = 0x60;

    static readonly HashSet<int> Extended =
        [0x2D, 0x24, 0x21, 0x2E, 0x23, 0x22, 0x25, 0x27, 0x26, 0x28, 0x90, 0x6F, VK_LWIN, VK_RWIN, 0x5D, VK_RCONTROL, VK_RMENU, VK_RSHIFT];

    public static bool IsExtendedKey(int vk) => Extended.Contains(vk);

    public static int VkToScan(int vk) => (int)MapVirtualKey((uint)vk, 0) | (IsExtendedKey(vk) ? 0x100 : 0);

    public static void KeyDown(int vk) => Key(vk, false);
    public static void KeyUp(int vk) => Key(vk, true);

    static void Key(int vk, bool up)
    {
        uint flags = (IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = flags, dwExtraInfo = ExtraInfoIgnore } },
        });
    }

    /// <summary>Types a character that has no key on the current layout.</summary>
    public static void Unicode(char c)
    {
        foreach (bool up in new[] { false, true })
            Send(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = ExtraInfoIgnore } },
            });
    }

    public static void Mouse(uint flags, int data = 0, int dx = 0, int dy = 0) => Send(new INPUT
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags, dwExtraInfo = ExtraInfoIgnore } },
    });

    public static void MouseDown(MouseButton b)
    {
        switch (b)
        {
            case MouseButton.Left: Mouse(MOUSEEVENTF_LEFTDOWN); break;
            case MouseButton.Right: Mouse(MOUSEEVENTF_RIGHTDOWN); break;
            case MouseButton.Middle: Mouse(MOUSEEVENTF_MIDDLEDOWN); break;
            case MouseButton.X1: Mouse(MOUSEEVENTF_XDOWN, 1); break;
            case MouseButton.X2: Mouse(MOUSEEVENTF_XDOWN, 2); break;
            case MouseButton.WheelUp: Mouse(MOUSEEVENTF_WHEEL, WHEEL_DELTA); break;
            case MouseButton.WheelDown: Mouse(MOUSEEVENTF_WHEEL, -WHEEL_DELTA); break;
            case MouseButton.WheelRight: Mouse(MOUSEEVENTF_HWHEEL, WHEEL_DELTA); break;
            case MouseButton.WheelLeft: Mouse(MOUSEEVENTF_HWHEEL, -WHEEL_DELTA); break;
        }
    }

    public static void MouseUp(MouseButton b)
    {
        switch (b)
        {
            case MouseButton.Left: Mouse(MOUSEEVENTF_LEFTUP); break;
            case MouseButton.Right: Mouse(MOUSEEVENTF_RIGHTUP); break;
            case MouseButton.Middle: Mouse(MOUSEEVENTF_MIDDLEUP); break;
            case MouseButton.X1: Mouse(MOUSEEVENTF_XUP, 1); break;
            case MouseButton.X2: Mouse(MOUSEEVENTF_XUP, 2); break;
        }
    }

    public static void Click(MouseButton b) { MouseDown(b); MouseUp(b); }

    public static void DoubleClick()
    {
        Click(MouseButton.Left);
        Thread.Sleep(1);
        Click(MouseButton.Left);
    }

    /// <summary>Waits (max 5 s) until the user releases every key and mouse button.</summary>
    public static void WaitForKeysReleased()
    {
        for (int k = 0; k < 100; k++)
        {
            bool pressed = false;
            for (int i = 1; i < 256 && !pressed; i++)
                if (GetAsyncKeyState(i) < 0) pressed = true;
            if (!pressed) break;
            Thread.Sleep(50);
        }
    }

    static void Send(INPUT input) => SendInput(1, [input], Marshal.SizeOf<INPUT>());
}
