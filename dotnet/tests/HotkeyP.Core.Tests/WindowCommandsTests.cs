using System.Runtime.InteropServices;
using HotkeyP.Commands;

namespace HotkeyP.Core.Tests;

public class WindowCommandsTests
{
    [Theory]
    [InlineData("", 0, "")]
    [InlineData("128", 128, "")]
    [InlineData("128 Notepad", 128, "Notepad")]
    [InlineData("128    Untitled - Notepad", 128, "Untitled - Notepad")]
    [InlineData("Notepad", 0, "Notepad")]
    [InlineData("  Notepad", 0, "Notepad")]
    [InlineData("-5 x", -5, "x")]
    [InlineData("+7x", 7, "x")]
    [InlineData("  42 abc", 42, "abc")]
    [InlineData("3:Microphone", 0, "3:Microphone")]
    [InlineData("99999999999 w", int.MaxValue, "w")]
    public void StrToIntStrMatchesCpp(string param, int value, string rest)
    {
        Assert.Equal((value, rest), WindowParams.StrToIntStr(param));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("idle", 0)]
    [InlineData("Normal", 1)]
    [InlineData("HIGH", 2)]
    [InlineData("realtime", 3)]
    [InlineData("below normal", 4)]
    [InlineData("Above Normal", 5)]
    [InlineData("2", 2)]
    [InlineData("9", 5)]
    [InlineData("-1", 0)]
    [InlineData("junk", 0)]
    [InlineData("below  normal", 0)]
    public void ParsesPriority(string param, int expected)
    {
        Assert.Equal(expected, WindowParams.ParsePriority(param));
    }

    [Theory]
    [InlineData("18808", 18808)]
    [InlineData("abc", 0)]
    [InlineData("12:3", 12)]
    public void ParsesWindowCommandId(string first, int expected)
    {
        Assert.Equal(expected, WindowParams.WindowCommandId(first));
    }

    [Fact]
    public void MoveActionsMatchCppTable()
    {
        Assert.Equal((3, 3), WindowParams.MoveActions(28)!.Value);
        Assert.Equal((1, 2), WindowParams.MoveActions(29)!.Value);
        Assert.Equal((0, 2), WindowParams.MoveActions(30)!.Value);
        Assert.Equal((2, 2), WindowParams.MoveActions(31)!.Value);
        Assert.Equal((1, 0), WindowParams.MoveActions(32)!.Value);
        Assert.Equal((2, 0), WindowParams.MoveActions(33)!.Value);
        Assert.Equal((1, 1), WindowParams.MoveActions(34)!.Value);
        Assert.Equal((0, 1), WindowParams.MoveActions(35)!.Value);
        Assert.Equal((2, 1), WindowParams.MoveActions(36)!.Value);
        Assert.Null(WindowParams.MoveActions(37));
    }

    [Theory]
    [InlineData(3, 3, 760, 420)]   // center
    [InlineData(1, 2, 0, 840)]     // bottom left
    [InlineData(0, 2, 100, 840)]   // bottom
    [InlineData(2, 2, 1520, 840)]  // bottom right
    [InlineData(1, 0, 0, 50)]      // left
    [InlineData(2, 1, 1520, 0)]    // top right
    public void ComputesMovePosition(int ax, int ay, int x, int y)
    {
        var desk = (0, 0, 1920, 1040);
        var win = (100, 50, 500, 250); // 400 x 200
        Assert.Equal((x, y), WindowParams.MovePosition(desk, win, ax, ay));
    }

    [Fact]
    public void CenterUsesArithmeticShift()
    {
        // odd sums and negative coordinates round like the C++ >>1
        Assert.Equal((-1, 0), WindowParams.MovePosition((-1, 0, 0, 0), (0, 0, 0, 0), 3, 0));
    }

    [Theory]
    [InlineData(255, 0, true, 255)]
    [InlineData(200, 0, false, 190)]
    [InlineData(5, 0, false, 0)]
    [InlineData(100, 50, true, 150)]
    [InlineData(250, 50, true, 255)]
    public void ChangesOpacity(int current, int step, bool increase, int expected)
    {
        Assert.Equal(expected, WindowParams.ChangeOpacity(current, step, increase));
    }

    [Fact]
    public void ShowDesktopMacroIsWinD()
    {
        Assert.Equal(
            new HotkeyP.Input.MacroStep[]
            {
                new HotkeyP.Input.RepeatStep(),
                new HotkeyP.Input.KeyStep(0x5B, HotkeyP.Input.KeyAction.Press),
                new HotkeyP.Input.KeyStep('D', HotkeyP.Input.KeyAction.Press),
            },
            HotkeyP.Input.MacroParser.Parse(@"\rep\win\D"));
    }

    [Fact]
    public void NotifyIconDataHasWindowsSize()
    {
        Assert.Equal(IntPtr.Size == 8 ? 976 : 956, Marshal.SizeOf<NOTIFYICONDATAW>());
    }

    [Fact]
    public void RegistersAllWindowCommands()
    {
        var t = new CommandTable();
        WindowCommands.Register(t);
        int[] ids = [7, 8, 9, 10, 11, 20, 21, 25, 28, 29, 30, 31, 32, 33, 34, 35, 36, 46, 47, 48, 49, 59, 60, 65, 66, 71, 72,
            73, 77, 81, 83, 84, 98, 99, 101, 102, 110, 111, 112, 115];
        foreach (var id in ids) Assert.True(t.Contains(id), $"command {id} not registered");
        foreach (var id in new[] { 26, 74, 91, 94 }) Assert.False(t.Contains(id));
    }
}
