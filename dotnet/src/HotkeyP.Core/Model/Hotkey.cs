// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Model;

/// <summary>Modifier flags, identical to the Win32 MOD_* values stored in .htk files.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

public enum ShowMode { Normal = 0, Maximized = 1, Minimized = 2 }

/// <summary>Process priority, in the order used by the .htk file.</summary>
public enum Priority { Idle = 0, Normal = 1, High = 2, Realtime = 3, BelowNormal = 4, AboveNormal = 5 }

/// <summary>One hotkey: a trigger (key, mouse buttons, joystick, remote) and an action (program or internal command).</summary>
public sealed class Hotkey
{
    /// <summary>Pseudo virtual key codes used by HotkeyP for non-keyboard triggers.</summary>
    public const int VkMouse = 512, VkDelete = 513, VkLirc = 514, VkJoy = 515;

    public string Note { get; set; } = "";
    /// <summary>Program, document or URL. Empty for internal commands.</summary>
    public string Exe { get; set; } = "";
    /// <summary>Program arguments, or the parameter of an internal command.</summary>
    public string Args { get; set; } = "";
    public string Dir { get; set; } = "";
    public string Sound { get; set; } = "";

    /// <summary>Scan code (lParam of WM_KEYDOWN) for keys; button mask for mouse; encoded input for joystick.</summary>
    public int ScanCode { get; set; }
    public int VKey { get; set; }
    public Modifiers Modifiers { get; set; }
    public ShowMode Show { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    /// <summary>Internal command id (see <see cref="Commands.CommandCatalog"/>), or -1 when <see cref="Exe"/> is used.</summary>
    public int Command { get; set; } = -1;
    /// <summary>0 = unchanged, 1..255 window opacity.</summary>
    public int Opacity { get; set; }
    public int Category { get; set; }
    /// <summary>WinLIRC remote button name, only when <see cref="VKey"/> is <see cref="VkLirc"/>.</summary>
    public string? Lirc { get; set; }

    public bool MultipleInstances { get; set; }
    public bool AutoStart { get; set; }
    public bool TrayMenu { get; set; }
    public bool Ask { get; set; }
    public bool Delay { get; set; }
    public bool Admin { get; set; }

    /// <summary>Runtime only: process started by this hotkey.</summary>
    internal int ProcessId { get; set; }

    public bool IsCommand => Command >= 0;
    public bool HasTrigger => VKey != 0;

    internal int Flags
    {
        get => (MultipleInstances ? 1 : 0) | (AutoStart ? 2 : 0) | (TrayMenu ? 4 : 0)
             | (Ask ? 8 : 0) | (Delay ? 16 : 0) | (Admin ? 32 : 0);
        set
        {
            MultipleInstances = (value & 1) != 0;
            AutoStart = (value & 2) != 0;
            TrayMenu = (value & 4) != 0;
            Ask = (value & 8) != 0;
            Delay = (value & 16) != 0;
            Admin = (value & 32) != 0;
        }
    }

    public Hotkey Clone()
    {
        var h = (Hotkey)MemberwiseClone();
        h.ProcessId = 0;
        return h;
    }
}
