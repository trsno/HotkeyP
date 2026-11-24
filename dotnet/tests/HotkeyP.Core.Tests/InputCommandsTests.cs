using System.Globalization;
using HotkeyP.Commands;
using HotkeyP.Engine;
using HotkeyP.Input;
using Microsoft.Win32;

namespace HotkeyP.Core.Tests;

public class InputCommandsTests
{
    // Tuesday 2024-03-05 07:08:09 (day 65 of a leap year)
    static readonly DateTime When = new(2024, 3, 5, 7, 8, 9);

    static InputPaste.TextSources Sources(string? clipboard = "CLIP") => new()
    {
        Now = When,
        Culture = CultureInfo.GetCultureInfo("en-US"),
        Clipboard = () => clipboard,
        UserName = () => "alice",
        ComputerName = () => "PC1",
        Registry = path => path == @"HKEY_CURRENT_USER\Software\X\Val" ? "42" : null,
        TimeZone = TimeZoneInfo.Utc,
    };

    static string Format(string s) => InputPaste.FormatText(s, Sources());

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("100%%", "100%")]
    [InlineData("end%", "end")]
    [InlineData("a%rb", "a\r\nb")]
    [InlineData("%u@%o", "alice@PC1")]
    [InlineData("[%l]", "[CLIP]")]
    [InlineData("%d.%m.%Y %H:%M:%S", "05.03.2024 07:08:09")]
    [InlineData("%#d.%#m. %#H", "5.3. 7")]
    [InlineData("%y %j %#j", "24 065 65")]
    [InlineData("%a %A %b %B", "Tue Tuesday Mar March")]
    [InlineData("%I %p", "07 AM")]
    [InlineData("%w %U %W %V", "2 09 10 10")]
    [InlineData("%F %T %D %R", "2024-03-05 07:08:09 03/05/24 07:08")]
    [InlineData("%x", "3/5/2024")]
    [InlineData("%#x", "Tuesday, March 5, 2024")]
    [InlineData("%z %Z", "+0000 Coordinated Universal Time")]
    [InlineData("%q", "%q")]
    [InlineData("%#q", "%q")]
    public void FormatTextSubstitutes(string input, string expected)
    {
        if (input.Contains("%Z")) expected = "+0000 " + TimeZoneInfo.Utc.StandardName;
        Assert.Equal(expected, Format(input));
    }

    [Fact]
    public void FormatTextReadsRegistry()
    {
        Assert.Equal("v=42!", Format(@"v=%""HKEY_CURRENT_USER\Software\X\Val""!"));
        Assert.Equal("v=!", Format(@"v=%""HKEY_CURRENT_USER\Missing""!"));
    }

    [Fact]
    public void FormatTextUnterminatedQuoteDropsPercent()
    {
        Assert.Equal("a\"bc", Format("a%\"bc"));
    }

    [Fact]
    public void FormatTextEmptyClipboard()
    {
        Assert.Equal("[]", InputPaste.FormatText("[%l]", Sources(null)));
    }

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProductName", RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName")]
    [InlineData(@"hkey_current_user\Software\X\", RegistryHive.CurrentUser, @"Software\X", "")]
    [InlineData(@"HKEY_USERS\Value", RegistryHive.Users, "", "Value")]
    public void ParsesRegistryPath(string path, RegistryHive hive, string key, string value)
    {
        Assert.Equal((hive, key, value), InputPaste.ParseRegistryPath(path));
    }

    [Theory]
    [InlineData(@"HKLM\Software\X")]
    [InlineData(@"HKEY_NOWHERE\Software\X")]
    [InlineData(@"HKEY_CURRENT_USERSoftware")]
    public void RejectsBadRegistryPath(string path)
    {
        Assert.Null(InputPaste.ParseRegistryPath(path));
    }

    [Theory]
    [InlineData("one", new[] { "one" })]
    [InlineData("one%|two%|three", new[] { "one", "two", "three" })]
    [InlineData("100%%|x", new[] { "100%%|x" })]
    [InlineData("a%%%|b", new[] { "a%%", "b" })]
    [InlineData("%|", new[] { "", "" })]
    public void SplitsPasteItems(string param, string[] expected)
    {
        Assert.Equal(expected, InputPaste.SplitItems(param));
    }

    [Theory]
    [InlineData("PuTTY", @"\SHIFT\INS")]
    [InlineData("KiTTY", @"\SHIFT\INS")]
    [InlineData("mintty", @"\SHIFT\INS")]
    [InlineData("ConsoleWindowClass", @"\SHIFT\INS")]
    [InlineData("VirtualConsoleClass", @"\SHIFT\INS")]
    [InlineData("Notepad", @"\^\V")]
    [InlineData("putty", @"\^\V")]
    [InlineData("", @"\^\V")]
    public void PasteKeysDependOnWindowClass(string cls, string macro)
    {
        Assert.Equal(macro, InputPaste.PasteKeysMacro(cls));
    }

    [Theory]
    [InlineData("10 -5", true, 10, -5)]
    [InlineData("  -3   4 extra", true, -3, 4)]
    [InlineData("+7 +8", true, 7, 8)]
    [InlineData("10", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData("10,5", false, 0, 0)]
    [InlineData("a b", false, 0, 0)]
    public void ParsesMouseMove(string param, bool ok, int x, int y)
    {
        Assert.Equal(ok, InputCommands.TryParseMouseMove(param, out int px, out int py));
        if (ok) Assert.Equal((x, y), (px, py));
    }

    [Theory]
    [InlineData("", -120)]
    [InlineData("0", -120)]
    [InlineData("240", 240)]
    [InlineData("-60", -60)]
    [InlineData("abc", -120)]
    [InlineData("3:", -120)]
    public void ParsesWheelDelta(string param, int delta)
    {
        Assert.Equal(delta, InputCommands.WheelDelta(param));
    }

    [Theory]
    [InlineData("1500 Hello", 1500, "Hello")]
    [InlineData("1500   Hello world", 1500, "Hello world")]
    [InlineData("Hello", 6000, "Hello")]
    [InlineData("50 bottles", 6000, "50 bottles")]
    [InlineData("C: drive", 6000, "C: drive")]
    [InlineData("", 6000, "")]
    public void ParsesShowText(string param, int ms, string text)
    {
        Assert.Equal((ms, text), InputCommands.ParseShowText(param));
    }

    [Fact]
    public void LeadingIntIgnoresNumberBeforeColon()
    {
        Assert.Equal(0, InputCommands.LeadingInt("2:x", out var rest));
        Assert.Equal("2:x", rest);
        Assert.Equal(12, InputCommands.LeadingInt("12  ab", out rest));
        Assert.Equal("ab", rest);
    }

    [Fact]
    public void ParsesKeyList()
    {
        // characters are translated by the layout; 'é' has no key here
        static int Layout(char c) => c == 'a' ? 0x41 | 0x100 : -1;
        Assert.Equal(new[] { 0x5B, 0x5D, 0x41, 0x70 }, InputCommands.ParseKeyList(@"\LWIN\APPSaé\F1\LWIN\LBUTTON\SLEEP", Layout));
    }

    [Fact]
    public void KeyListWithUnknownNameThrows()
    {
        Assert.Throws<MacroSyntaxException>(() => InputCommands.ParseKeyList(@"\LWIN\?x", _ => -1));
    }

    [Fact]
    public void MacroToActiveWindowForcesDirection()
    {
        Assert.Equal(KeyAction.Down, InputCommands.ForceFor(TriggerKind.Down));
        Assert.Equal(KeyAction.Up, InputCommands.ForceFor(TriggerKind.Up));
        Assert.Null(InputCommands.ForceFor(TriggerKind.OnlyDown));
    }
}
