using HotkeyP.Commands;

namespace HotkeyP.Core.Tests;

public class MediaCommandsTests
{
    // ------------------------------------------------------------ volume parameter

    [Theory]
    [InlineData("", 0, "", false)]
    [InlineData("5", 5, "", true)]
    [InlineData("5 Wave", 5, "Wave", true)]
    [InlineData("-3  Speakers", -3, "Speakers", true)]
    [InlineData("Wave", 0, "Wave", false)]
    [InlineData("2:Speakers", 0, "2:Speakers", false)]
    [InlineData("r:Microphone", 0, "r:Microphone", false)]
    [InlineData("10 R:2:Line", 10, "R:2:Line", true)]
    public void StrToIntStr(string param, int value, string rest, bool hasNumber)
    {
        Assert.Equal(value, VolumeRequest.StrToIntStr(param, out string r, out bool n));
        Assert.Equal(rest, r);
        Assert.Equal(hasNumber, n);
    }

    [Fact]
    public void VolumeUpDefaultsToMixerAndStepOne()
    {
        Assert.Equal(new VolumeRequest("Mixer", false, 0, VolumeAction.Change, 1), VolumeRequest.ForCommand(13, ""));
        Assert.Equal(new VolumeRequest("Mixer", false, 0, VolumeAction.Change, -5), VolumeRequest.ForCommand(14, "5"));
    }

    [Fact]
    public void VolumePrefixes()
    {
        Assert.Equal(new VolumeRequest("Microphone", true, 2, VolumeAction.Change, 4), VolumeRequest.ForCommand(13, "4 r:2:Microphone"));
        Assert.Equal(new VolumeRequest("Mixer", true, 0, VolumeAction.ToggleMute, 1), VolumeRequest.ForCommand(17, "R:"));
        Assert.Equal(new VolumeRequest("Speakers", false, 3, VolumeAction.ToggleMute, 1), VolumeRequest.ForCommand(17, "3: Speakers"));
    }

    [Fact]
    public void WaveCommandsIgnoreTheLineName()
    {
        Assert.Equal(new VolumeRequest("Wave", false, 0, VolumeAction.Change, 7), VolumeRequest.ForCommand(15, "7 Speakers"));
        Assert.Equal(new VolumeRequest("Wave", false, 0, VolumeAction.Change, -1), VolumeRequest.ForCommand(16, ""));
        Assert.Equal(new VolumeRequest("Wave", false, 0, VolumeAction.ToggleMute, 1), VolumeRequest.ForCommand(58, ""));
    }

    [Fact]
    public void SetVolumeDefaultsTo50()
    {
        Assert.Equal(new VolumeRequest("Mixer", false, 0, VolumeAction.Set, 50), VolumeRequest.ForCommand(78, ""));
        Assert.Equal(new VolumeRequest("Mixer", false, 0, VolumeAction.Set, 0), VolumeRequest.ForCommand(78, "0"));
        Assert.Equal(new VolumeRequest("Speakers", false, 0, VolumeAction.Set, 30), VolumeRequest.ForCommand(78, "30 Speakers"));
        Assert.Equal(new VolumeRequest("Speakers", false, 2, VolumeAction.Set, 50), VolumeRequest.ForCommand(78, "2:Speakers"));
    }

    [Theory]
    [InlineData(50, 0, 5, 55)]
    [InlineData(98, 0, 5, 100)]
    [InlineData(2, 0, -5, 0)]
    [InlineData(20, 2, 70, 70)]
    [InlineData(20, 2, 150, 100)]
    [InlineData(20, 1, 1, 20)]
    public void NewPercent(int current, int action, int value, int expected) =>
        Assert.Equal(expected, VolumeRequest.NewPercent(current, (VolumeAction)action, value));

    [Fact]
    public void DbLevelStepIsAtLeastOneHardwareStep()
    {
        // range -40..0 dB, step 1.5 dB: 1 % (0.4 dB) is rounded up to one step
        var (level, percent, changed) = VolumeRequest.NewDbLevel(-20, -40, 0, 1.5f, VolumeAction.Change, 1);
        Assert.Equal(-18.5f, level);
        Assert.Equal(51, percent);
        Assert.True(changed);
        var down = VolumeRequest.NewDbLevel(-1, -40, 0, 0.5f, VolumeAction.Change, 10);
        Assert.Equal(0f, down.Level); // clamped to the range
        Assert.Equal(100, down.Percent);
        var set = VolumeRequest.NewDbLevel(-20, -40, 0, 0f, VolumeAction.Set, 25);
        Assert.Equal(-30f, set.Level);
        Assert.Equal(25, set.Percent);
    }

    [Fact]
    public void VolumeDisplayText()
    {
        var mixer = VolumeRequest.ForCommand(13, "");
        Assert.Equal("Volume: 45 %", MediaCommands.VolumeText(mixer, new VolumeState(45, false)));
        Assert.Equal("Volume: Muted", MediaCommands.VolumeText(mixer, new VolumeState(45, true)));
        Assert.Equal("2: R:Mic: ???", MediaCommands.VolumeText(VolumeRequest.ForCommand(13, "R:2:Mic"), null));
    }

    // ------------------------------------------------------------ wallpaper

    sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hkp-wall-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string File(string rel)
        {
            var p = System.IO.Path.Combine(Path, rel);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            System.IO.File.WriteAllBytes(p, [0]);
            return p;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("a.bmp", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.png", false)]
    [InlineData("a.jp", false)]
    [InlineData("noext", false)]
    public void PictureExtensions(string name, bool expected) => Assert.Equal(expected, Wallpaper.IsPicture(name));

    [Fact]
    public void NextAndPreviousWrapAround()
    {
        using var d = new TempDir();
        string a = d.File("a.jpg"), b = d.File("b.bmp"), c = d.File("c.gif");
        d.File("notes.txt");
        d.File(@".hidden\x.jpg");
        string sub = d.File(@"sub\s.jpg");
        var files = Wallpaper.ListPictures(d.Path, true);
        Assert.Equal([a, b, c, sub], files);

        Assert.Equal(b, Wallpaper.Pick(files, WallpaperAction.Next, a, new Random(1)));
        Assert.Equal(a, Wallpaper.Pick(files, WallpaperAction.Next, sub, new Random(1)));
        Assert.Equal(a, Wallpaper.Pick(files, WallpaperAction.Next, "", new Random(1)));
        Assert.Equal(a, Wallpaper.Pick(files, WallpaperAction.Next, @"C:\elsewhere\x.jpg", new Random(1)));
        Assert.Equal(c, Wallpaper.Pick(files, WallpaperAction.Previous, sub, new Random(1)));
        Assert.Equal(sub, Wallpaper.Pick(files, WallpaperAction.Previous, a, new Random(1)));
        Assert.Equal(sub, Wallpaper.Pick(files, WallpaperAction.Previous, null, new Random(1)));
        Assert.Equal(b, Wallpaper.Pick(files, WallpaperAction.Next, a.ToUpperInvariant(), new Random(1)));
        Assert.Contains(Wallpaper.Pick(files, WallpaperAction.Random, null, new Random(1)), files);
        Assert.Null(Wallpaper.Pick([], WallpaperAction.Next, a, new Random(1)));
    }

    [Fact]
    public void ChooseHandlesFileFolderAndMissingPath()
    {
        using var d = new TempDir();
        string a = d.File("a.jpg");
        Assert.Equal(a, Wallpaper.Choose(a, WallpaperAction.Next, null, new Random(1), out var err1));
        Assert.Null(err1);
        Assert.Equal(a, Wallpaper.Choose(d.Path, WallpaperAction.Next, a, new Random(1), out _));
        Assert.Null(Wallpaper.Choose(System.IO.Path.Combine(d.Path, "missing"), WallpaperAction.Next, null, new Random(1), out var err2));
        Assert.StartsWith("Cannot find folder or file", err2);
        Assert.Equal(d.Path, Wallpaper.Unquote("\"" + d.Path + "\""));
    }

    [Fact]
    public void WallpaperStateRoundTrip()
    {
        var st = new WallpaperState();
        st.SetLast(@"C:\Pics", @"C:\Pics\a.jpg");
        st.SetLast("", @"C:\Windows\x.bmp");
        st.SetLast(@"C:\Pics", @"C:\Pics\b.jpg");
        var bytes = st.Serialize();
        var back = WallpaperState.Parse(bytes);
        Assert.Equal(@"C:\Pics\b.jpg", back.Last(@"C:\Pics"));
        Assert.Equal(@"C:\Windows\x.bmp", back.Last(""));
        Assert.Equal("", back.Last(@"c:\pics")); // case-sensitive like the C++
        Assert.Equal(2, back.Pairs.Count);
        Assert.Empty(WallpaperState.Parse(System.Text.Encoding.Unicode.GetBytes("dir\0file")).Pairs); // not null-terminated
    }

    // ------------------------------------------------------------ disk free

    [Theory]
    [InlineData(3_000_000_000UL, "2.8 GB")]
    [InlineData(2_000_000_000UL, "1907.3 MB")]
    [InlineData(5_000_000UL, "4.8 MB")]
    [InlineData(1_500_000UL, "1464 kB")]
    public void FreeSpaceText(ulong free, string expected) => Assert.Equal(expected, DiskFreeOsd.FormatFree(free));

    [Fact]
    public void DiskFreeTextRoundTrip()
    {
        var lines = new[] { DiskFreeOsd.Line('C', 3_000_000_000UL, 12_000_000_000UL), DiskFreeOsd.Line('D', 0, 0) };
        string text = DiskFreeOsd.Format(lines);
        Assert.Equal("C:\t2.8 GB\t0.25\nD:\t0 kB\t0", text);
        Assert.Equal(lines, DiskFreeOsd.Parse(text));
    }

    // ------------------------------------------------------------ desktop icons, snapshot

    [Fact]
    public void DesktopIconsFormat()
    {
        var icons = new List<DesktopIcon> { new(10, 20, "Recycle Bin"), new(-5, 300, "This PC") };
        string s = DesktopIcons.Serialize(icons);
        Assert.Equal("10 20 Recycle Bin|-5 300 This PC|", s);
        Assert.Equal(icons, DesktopIcons.Parse(s));
        Assert.Equal([new DesktopIcon(1, 2, "a")], DesktopIcons.Parse("1 2 a|3 4 unterminated"));
    }

    [Fact]
    public void BmpHeader()
    {
        var bmp = Screenshot.Bmp(2, 3, new byte[2 * 3 * 4]);
        Assert.Equal(54 + 24, bmp.Length);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(78, BitConverter.ToInt32(bmp, 2));
        Assert.Equal(54, BitConverter.ToInt32(bmp, 10));
        Assert.Equal(2, BitConverter.ToInt32(bmp, 18));
        Assert.Equal(3, BitConverter.ToInt32(bmp, 22));
        Assert.Equal(32, BitConverter.ToInt16(bmp, 28));
    }

    [Fact]
    public void AllMediaCommandsAreRegistered()
    {
        var t = new CommandTable();
        MediaCommands.Register(t);
        foreach (int id in new[] { 13, 14, 15, 16, 17, 58, 78, 23, 68, 69, 82, 24, 89, 90, 106, 107 })
            Assert.True(t.Contains(id), $"command {id}");
    }
}
