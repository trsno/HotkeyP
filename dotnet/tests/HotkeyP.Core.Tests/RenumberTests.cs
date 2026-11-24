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

public class MultiCommandParamTests
{
    [Theory]
    [InlineData("3,5,8", new[] { 3, 5, 8 })]
    [InlineData("3 5 8", new[] { 3, 5, 8 })]
    [InlineData("3;5", new[] { 3, 5 })]
    [InlineData("2 0 4", new[] { 2 })]
    [InlineData("abc", new int[0])]
    [InlineData("", new int[0])]
    public void ParsesLikeCpp(string s, int[] expected)
        => Assert.Equal(expected, HotkeyP.Model.MultiCommandParam.Parse(s).Select(n => n.Value));

    [Fact]
    public void RenumberKeepsCommaSeparators()
        => Assert.Equal("4,6,9", HotkeyP.Model.MultiCommandParam.Renumber("3,5,8", i => i + 1));

    [Fact]
    public void RenumberDropsDeleted()
        => Assert.Equal("3,7", HotkeyP.Model.MultiCommandParam.Renumber("3,5,8", i => i == 4 ? null : i > 4 ? i - 1 : i));

    [Fact]
    public void RenumberLeavesUnparsableAlone()
        => Assert.Equal("abc", HotkeyP.Model.MultiCommandParam.Renumber("abc", i => i + 1));
}

public class DataOverrideTests
{
    [Fact]
    public void FolderUsesOverrideWhenSet()
    {
        // static initializer order: Folder must see DataOverride
        if (HotkeyP.Model.AppSettings.DataOverride is string d) Assert.Equal(d, HotkeyP.Model.AppSettings.Folder);
        else Assert.EndsWith("HotkeyP", HotkeyP.Model.AppSettings.Folder);
    }
}
