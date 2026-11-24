using HotkeyP.Input;

namespace HotkeyP.Core.Tests;

public class MacroParserTests
{
    [Fact]
    public void ParsesCtrlV()
    {
        Assert.Equal(new MacroStep[] { new KeyStep(0x11, KeyAction.Press), new KeyStep('V', KeyAction.Press) },
            MacroParser.Parse(@"\^\V"));
    }

    [Fact]
    public void ParsesTextAndEscapedBackslash()
    {
        Assert.Equal(new MacroStep[] { new CharStep('a', KeyAction.Press), new CharStep('\\', KeyAction.Press), new CharStep('b', KeyAction.Press) },
            MacroParser.Parse(@"a\\b"));
    }

    [Theory]
    [InlineData(@"\F1", 0x70)]
    [InlineData(@"\F10", 0x79)]
    [InlineData(@"\f24", 0x87)]
    [InlineData(@"\BACKSPACE", 0x08)]
    [InlineData(@"\BACK", 0xA6)]
    [InlineData(@"\delete", 0x2E)]
    [InlineData(@"\x41", 0x41)]
    [InlineData(@"\INS", 0x2D)]
    public void ParsesKeyNames(string macro, int vk)
    {
        Assert.Equal(new KeyStep(vk, KeyAction.Press), Assert.Single(MacroParser.Parse(macro)));
    }

    [Fact]
    public void ParsesUpDownAndSeparators()
    {
        Assert.Equal(new MacroStep[] {
                new KeyStep(0x5B, KeyAction.Down), new KeyStep(0x0D, KeyAction.Press), new KeyStep(0x5B, KeyAction.Up), new CharStep('x', KeyAction.Press) },
            MacroParser.Parse(@"\windown\enter\winup.x"));
    }

    [Fact]
    public void SeparatorIsConsumedOnlyOnce()
    {
        Assert.Equal(new MacroStep[] { new KeyStep(0x09, KeyAction.Press), new CharStep(' ', KeyAction.Press) },
            MacroParser.Parse(@"\TAB  "));
    }

    [Theory]
    [InlineData(@"\SLEEP", 1000)]
    [InlineData(@"\SLEEP 5", 500)]
    [InlineData(@"\SLEEP 0", 10)]
    [InlineData(@"\SLEEP 500", 10000)]
    public void ParsesSleep(string macro, int ms)
    {
        Assert.Equal(new SleepStep(ms), Assert.Single(MacroParser.Parse(macro)));
    }

    [Fact]
    public void ParsesMouseAndSpecialCommands()
    {
        Assert.Equal(new MacroStep[] {
                new MouseStep(MouseButton.Left, KeyAction.Down), new MouseStep(MouseButton.WheelUp, KeyAction.Press),
                new WaitForKeysStep(), new RepeatStep(), new DoubleClickStep() },
            MacroParser.Parse(@"\LBUTTONdown\WHEELUP\WAIT\REP\DOUBLECLICK"));
    }

    [Fact]
    public void RejectsUnknownKeyName()
    {
        var ex = Assert.Throws<MacroSyntaxException>(() => MacroParser.Parse(@"ab\qq x"));
        Assert.Contains("qq", ex.Message);
        Assert.Equal(2, ex.Position);
    }

    [Fact]
    public void F13ToF24Supported()
    {
        for (int n = 13; n <= 24; n++)
            Assert.Equal(new KeyStep(0x70 + n - 1, KeyAction.Press), Assert.Single(MacroParser.Parse($@"\F{n}")));
    }
}
