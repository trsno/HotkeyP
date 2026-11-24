using System.Text;
using HotkeyP.Model;

namespace HotkeyP.Core.Tests;

public class HotkeyDocumentTests
{
    const string Sample =
        "hotKeys7 3 2\r\n" +
        "Notepad\r\nnotepad.exe\r\n\r\n\r\n\r\n" +
        "3014657 78 10 0 1 1 -1 0 0\r\n" +
        "Volume up \u00e9\u4e2d\r\n\r\n5\r\n\r\nC:\\ding.wav\r\n" +
        "0 107 2 0 1 36 13 200 1\r\n" +
        "Remote\r\n\r\n\r\n\r\n\r\n" +
        "0 514 0 0 1 0 17 0 1 MUTE\r\n" +
        "Media\r\n";

    [Fact]
    public void RoundTripsVersion7ByteForByte()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample);
        var doc = HotkeyDocument.Parse(bytes);
        Assert.Equal(bytes, doc.Serialize());
    }

    [Fact]
    public void ParsesFields()
    {
        var doc = HotkeyDocument.Parse(Encoding.UTF8.GetBytes(Sample));
        Assert.Equal(3, doc.Hotkeys.Count);
        Assert.Equal(new[] { "", "Media" }, doc.Categories);

        var np = doc.Hotkeys[0];
        Assert.Equal("notepad.exe", np.Exe);
        Assert.Equal('N', np.VKey);
        Assert.Equal(Modifiers.Control | Modifiers.Win, np.Modifiers);
        Assert.False(np.IsCommand);
        Assert.True(np.MultipleInstances);

        var vol = doc.Hotkeys[1];
        Assert.Equal("Volume up \u00e9\u4e2d", vol.Note);
        Assert.Equal(13, vol.Command);
        Assert.Equal("5", vol.Args);
        Assert.Equal(@"C:\ding.wav", vol.Sound);
        Assert.True(vol.TrayMenu);
        Assert.True(vol.Admin);
        Assert.Equal(200, vol.Opacity);
        Assert.Equal(1, vol.Category);

        Assert.Equal("MUTE", doc.Hotkeys[2].Lirc);
    }

    [Fact]
    public void ReadsOldVersionWithoutCategoriesOrSound()
    {
        // version 3: no sound line, no category, ANSI text
        var data = Encoding.ASCII.GetBytes("hotKeys3 1\nCalc\ncalc.exe\n\n\n0 67 3 1 2 0 -1 0\n");
        var doc = HotkeyDocument.Parse(data);
        var hk = Assert.Single(doc.Hotkeys);
        Assert.Equal("calc.exe", hk.Exe);
        Assert.Equal(ShowMode.Maximized, hk.Show);
        Assert.Equal(Priority.High, hk.Priority);
        Assert.Equal(0, hk.Category);
        Assert.Single(doc.Categories);
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => HotkeyDocument.Parse(Encoding.ASCII.GetBytes("hello")));
    }

    /// <summary>Optional check against a real user file, set HOTKEYP_TEST_FILE to run it.</summary>
    [Fact]
    public void RoundTripsRealFileIfProvided()
    {
        var path = Environment.GetEnvironmentVariable("HOTKEYP_TEST_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var bytes = File.ReadAllBytes(path);
        var normalized = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Equal(normalized, HotkeyDocument.Parse(bytes).Serialize());
    }
}
