// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Text;
using HotkeyP.Engine;
using HotkeyP.Native;

namespace HotkeyP.Model;

/// <summary>Human readable names of hotkey triggers, e.g. "Ctrl+Alt+N" or "Mouse Right+Left".</summary>
public static class KeyFormatter
{
    static readonly string[] MediaKeys =
    [
        "Back", "Forward", "Refresh", "Stop", "Search", "Favorites", "Browser", "Mute", "Volume down", "Volume up",
        "Next track", "Previous track", "Stop media", "Play/Pause", "Mail", "Media", "App1", "App2",
    ];

    /// <summary>Modifier names in display order.</summary>
    public static IEnumerable<string> ModifierNames(Modifiers m)
    {
        if ((m & Modifiers.Control) != 0) yield return "Ctrl";
        if ((m & Modifiers.Shift) != 0) yield return "Shift";
        if ((m & Modifiers.Alt) != 0) yield return "Alt";
        if ((m & Modifiers.Win) != 0) yield return "Win";
    }

    /// <summary>The parts of a hotkey, e.g. ["Ctrl", "Alt", "N"], for showing as separate key caps.</summary>
    public static IReadOnlyList<string> Parts(Hotkey hk)
    {
        if (!hk.HasTrigger) return [];
        var parts = ModifierNames(hk.Modifiers).ToList();
        parts.Add(KeyName(hk.VKey, hk.ScanCode, hk.Lirc));
        return parts;
    }

    public static string Format(Hotkey hk) => string.Join("+", Parts(hk));

    public static string Format(Modifiers m, int vk, int scan) => string.Join("+", ModifierNames(m).Append(KeyName(vk, scan)));

    public static string KeyName(int vk, int scan, string? lirc = null)
    {
        switch (vk)
        {
            case Hotkey.VkMouse:
                return "Mouse " + string.Join("+", MouseButtons.Enumerate(scan).Select(MouseButtons.Name));
            case Hotkey.VkLirc:
                return "Remote {" + lirc + "}";
            case Hotkey.VkJoy:
                return "Joystick";
            case Hotkey.VkDelete:
                return "Delete";
        }
        if (vk is >= '0' and <= '9' or >= 'A' and <= 'Z') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 166 and < 166 + 18) return MediaKeys[vk - 166];
        if (vk == 95) return "Sleep";
        if (vk is 0x5B or 0x5C) return vk == 0x5B ? "Left Win" : "Right Win";
        if (vk == 0x5D) return "Menu";
        if (scan == 0) scan = InputScan(vk);
        var sb = new StringBuilder(64);
        if (NativeMethods.GetKeyNameText(scan, sb, sb.Capacity) > 0)
        {
            var s = sb.ToString();
            return s.Length == 1 ? s.ToUpperInvariant() : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        }
        return $"Key {vk}";
    }

    /// <summary>Scan code in the lParam format stored in .htk files (bits 16-23 scan code, bit 24 extended).</summary>
    public static int InputScan(int vk)
    {
        int sc = (int)NativeMethods.MapVirtualKey((uint)vk, 0);
        return (sc << 16) | (Input.InputSender.IsExtendedKey(vk) ? 1 << 24 : 0);
    }
}
