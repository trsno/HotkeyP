// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Commands;

/// <summary>Commands that control HotkeyP itself.</summary>
static class HotkeyPCommands
{
    public static void Register(CommandTable t)
    {
        t.Add(61, MultiCommand);
        t.Add(70, CommandsList);
        t.Add(87, c => Toggle(c, e => e.DisableAll = !e.DisableAll));
        t.Add(88, c => Toggle(c, e => e.DisableMouse = !e.DisableMouse));
        t.Add(97, c => Toggle(c, e => e.DisableKeyboard = !e.DisableKeyboard));
        // joystick and WinLIRC remote control are not ported; the toggles are accepted so old files keep working
        t.Add(95, _ => { });
        t.Add(96, _ => { });
        t.Add(91, c =>
        {
            c.Runner.TrayIconHidden = !c.Runner.TrayIconHidden;
            c.Host.SetTrayIconVisible(!c.Runner.TrayIconHidden);
        });
        t.Add(113, c => c.Host.ShowMainWindow());
        t.Add(114, c => c.Runner.Engine?.ReloadHooks());
        t.Add(103, c => c.Host.ToggleMagnifier(c.IntParam(2)));
    }

    static void Toggle(CommandContext c, Action<Engine.HotkeyEngine> change)
    {
        if (c.Runner.Engine == null) return;
        change(c.Runner.Engine);
        c.Host.StateChanged();
    }

    [ThreadStatic] static HashSet<Model.Hotkey>? running;

    /// <summary>Runs several hotkeys, given by their numbers in the list.</summary>
    static void MultiCommand(CommandContext c)
    {
        if (c.Hotkey == null) return;
        running ??= new();
        if (!running.Add(c.Hotkey)) return; // a multi command that includes itself
        try
        {
            foreach (var sub in c.Runner.SubCommands(c.Hotkey)) c.Runner.Execute(sub);
        }
        finally { running.Remove(c.Hotkey); }
    }

    /// <summary>
    /// Runs the first hotkey of the list and moves its number to the end, so each press runs the next one.
    /// The rotated list is saved in the hotkeys file.
    /// </summary>
    static void CommandsList(CommandContext c)
    {
        var hk = c.Hotkey;
        if (hk == null) return;
        var parts = hk.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0 || !int.TryParse(parts[0], out int n)) return;
        var list = c.Runner.Hotkeys;
        if (n < 1 || n > list.Count || ReferenceEquals(list[n - 1], hk))
        {
            c.Host.ShowMessage("Invalid multi-command number");
            return;
        }
        running ??= new();
        if (!running.Add(hk)) return;
        try { c.Runner.Execute(list[n - 1]); }
        finally { running.Remove(hk); }
        if (parts.Count > 1)
        {
            parts.Add(parts[0]);
            parts.RemoveAt(0);
            hk.Args = string.Join(' ', parts);
            c.Host.DocumentChanged();
        }
    }
}
