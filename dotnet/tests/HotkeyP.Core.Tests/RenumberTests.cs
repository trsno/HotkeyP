using HotkeyP.Model;

namespace HotkeyP.Core.Tests;

public class RenumberTests
{
    static HotkeyDocument Doc()
    {
        var d = new HotkeyDocument();
        for (int i = 0; i < 5; i++) d.Hotkeys.Add(new Hotkey { Note = "k" + i });
        d.Hotkeys.Add(new Hotkey { Command = 61, Args = "1 3 5" });
        return d;
    }

    [Fact]
    public void DeleteDropsAndShiftsReferences()
    {
        var d = Doc();
        d.RemoveAt(2);
        Assert.Equal("1 4", d.Hotkeys[^1].Args);
    }

    [Fact]
    public void InsertShiftsReferences()
    {
        var d = Doc();
        d.Insert(1, new Hotkey());
        Assert.Equal("1 4 6", d.Hotkeys[^1].Args);
    }

    [Fact]
    public void MoveKeepsTargets()
    {
        var d = Doc();
        var targets = new[] { 0, 2, 4 }.Select(i => d.Hotkeys[i]).ToList();
        d.Move(0, 3);
        d.Move(4, 1);
        var multi = d.Hotkeys.Single(h => h.Command == 61);
        var now = multi.Args.Split(' ').Select(n => d.Hotkeys[int.Parse(n) - 1]).ToList();
        Assert.Equal(targets, now);
    }
}
