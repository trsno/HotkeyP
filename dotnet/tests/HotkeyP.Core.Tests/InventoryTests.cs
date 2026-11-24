using HotkeyP.Commands;
using HotkeyP.Model;

namespace HotkeyP.Core.Tests;

/// <summary>Read-only summary of a real hotkeys file (HOTKEYP_TEST_FILE); prints what it uses.</summary>
public class InventoryTests
{
    [Fact]
    public void Inventory()
    {
        var path = Environment.GetEnvironmentVariable("HOTKEYP_TEST_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var doc = HotkeyDocument.Load(path);
        var h = doc.Hotkeys;
        Console.WriteLine($"INV total {h.Count}");
        Console.WriteLine($"INV keyboard {h.Count(x => x.VKey is > 0 and < 256)} mouse {h.Count(x => x.VKey == Hotkey.VkMouse)} joystick {h.Count(x => x.VKey == Hotkey.VkJoy)} lirc {h.Count(x => x.VKey == Hotkey.VkLirc || !string.IsNullOrEmpty(x.Lirc))} none {h.Count(x => x.VKey == 0)}");
        Console.WriteLine($"INV programs {h.Count(x => !x.IsCommand)} ask {h.Count(x => x.Ask)} delay {h.Count(x => x.Delay)} admin {h.Count(x => x.Admin)} autostart {h.Count(x => x.AutoStart)} tray {h.Count(x => x.TrayMenu)} sound {h.Count(x => !string.IsNullOrEmpty(x.Sound))} opacity {h.Count(x => x.Opacity != 0)}");
        foreach (var x in h.Where(x => x.VKey == Hotkey.VkMouse))
            Console.WriteLine($"INV mouse scan {x.ScanCode} = {string.Join("+", HotkeyP.Engine.MouseButtons.Enumerate(x.ScanCode).Select(HotkeyP.Engine.MouseButtons.Name))} mods {x.Modifiers} cmd {x.Command}");
        foreach (var x in h.Where(x => x.Command == 27))
            Console.WriteLine($"INV macro [{x.Args}] steps: {string.Join(" ", HotkeyP.Input.MacroParser.Parse(x.Args).Select(st => st.ToString()))}");
        foreach (var g in h.Where(x => x.IsCommand).GroupBy(x => x.Command).OrderBy(g => g.Key))
            Console.WriteLine($"INV cmd {g.Key} {CommandCatalog.NameOf(g.Key)} x{g.Count()}" +
                (g.Key is 2 or 3 or 5 ? $" params[{string.Join("|", g.Select(x => x.Args))}]" : "") +
                (g.Key is 61 or 70 ? $" params[{string.Join("|", g.Select(x => x.Args))}]" : "") +
                (g.Key is 77 or 110 or 111 or 63 ? $" params[{string.Join("|", g.Select(x => x.Args.Length > 0 ? "set" : ""))}]" : ""));
    }
}
