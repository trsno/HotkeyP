using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HotkeyP.Commands;
using static HotkeyP.Commands.SystemNative;

namespace HotkeyP.Core.Tests;

// Only pure parsing and struct layouts are tested; no command is ever executed.
public class SystemCommandsTests
{
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("12", 12, 2)]
    [InlineData("  -7x", -7, 4)]
    [InlineData("+5", 5, 2)]
    [InlineData("x12", 0, 0)]
    [InlineData(" -", 0, 0)]
    [InlineData("99999999999", int.MaxValue, 11)]
    public void StrToL_MatchesC(string s, int value, int end)
    {
        int pos = 0;
        Assert.Equal(value, SystemParsing.StrToL(s, ref pos));
        Assert.Equal(end, pos);
    }

    [Fact]
    public void StrToUL_NegativeWraps()
    {
        int pos = 0;
        Assert.Equal(uint.MaxValue, SystemParsing.StrToUL("-1", ref pos));
    }

    [Theory]
    [InlineData("", 0, "")]
    [InlineData("1", 1, "")]
    [InlineData("2  rest", 2, "rest")]
    [InlineData("16 E", 16, "E")]
    [InlineData("E", 0, "E")]
    [InlineData("1:30", 0, "1:30")]
    [InlineData("1 x", 1, "x")]
    public void StrToIntStr(string param, int value, string rest)
    {
        Assert.Equal(value, SystemParsing.StrToIntStr(param, out var r));
        Assert.Equal(rest, r);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("0", false, false)]
    [InlineData("1", true, false)]
    [InlineData("2", false, true)]
    [InlineData("1 anything", true, false)]
    public void PowerParam(string param, bool force, bool closeAll)
    {
        Assert.Equal(force, SystemParsing.IsForce(param));
        Assert.Equal(closeAll, SystemParsing.IsCloseAll(param));
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("x", 0)]
    public void MonitorPower(string param, int expected) => Assert.Equal(expected, SystemParsing.MonitorPowerParam(param));

    static readonly DisplayMode Current = new(1280, 1024, 32, 60);

    [Fact]
    public void Resolution_HelpExample_Toggles()
    {
        var r = SystemParsing.ParseResolution("1280 1024 16 60, 1024 768 32 75");
        Assert.False(r.Empty);
        Assert.Equal(new DisplayMode(1280, 1024, 16, 60), r.First);
        Assert.Equal(0u, r.Display);
        Assert.Equal(new DisplayMode(1024, 768, 32, 75), r.Second);
        // 1280x1024x16 is active -> second mode
        Assert.Equal(new DisplayMode(1024, 768, 32, 75), SystemParsing.TargetMode(r, new DisplayMode(1280, 1024, 16, 60)));
        // width matches but the color depth does not (and widths differ) -> second mode, as in C++
        Assert.Equal(new DisplayMode(1024, 768, 32, 75), SystemParsing.TargetMode(r, Current));
        // another resolution is active -> first mode
        Assert.Equal(new DisplayMode(1280, 1024, 16, 60), SystemParsing.TargetMode(r, new DisplayMode(1024, 768, 32, 75)));
    }

    [Fact]
    public void Resolution_SameWidth_UsesBitsAndFrequency()
    {
        var r = SystemParsing.ParseResolution("1280 1024 32 60, 1280 1024 32 75");
        Assert.Equal(new DisplayMode(1280, 1024, 32, 75), SystemParsing.TargetMode(r, Current));
        Assert.Equal(new DisplayMode(1280, 1024, 32, 60), SystemParsing.TargetMode(r, Current with { Frequency = 75 }));
    }

    [Fact]
    public void Resolution_DisplayNumber()
    {
        var r = SystemParsing.ParseResolution("1920 1080 32 60 2");
        Assert.Equal(new DisplayMode(1920, 1080, 32, 60), r.First);
        Assert.Equal(2u, r.Display);
        Assert.Null(r.Second);
        Assert.Equal(@"\\.\DISPLAY2", SystemParsing.DisplayDevice(r.Display));
        Assert.Null(SystemParsing.DisplayDevice(0));

        var r2 = SystemParsing.ParseResolution("1920 1080 32 60 12, 800 600");
        Assert.Equal(12u, r2.Display);
        Assert.Equal(new DisplayMode(800, 600, 0, 0), r2.Second);
    }

    [Fact]
    public void Resolution_PartialKeepsCurrent()
    {
        var r = SystemParsing.ParseResolution("1024 768");
        Assert.Equal(new DisplayMode(1024, 768, 32, 60), SystemParsing.TargetMode(r, Current));
    }

    [Fact]
    public void Resolution_XFormat_OnlyWidth()
    {
        // strtoul stops at 'x' without advancing, so only the width is read (same as the C++ version)
        var r = SystemParsing.ParseResolution("1920x1080x32@60");
        Assert.Equal(new DisplayMode(1920, 0, 0, 0), r.First);
        Assert.Null(r.Second);
        Assert.Equal(new DisplayMode(1920, 1024, 32, 60), SystemParsing.TargetMode(r, Current));
    }

    [Fact]
    public void Resolution_SpacesOnly_KeepsCurrent()
    {
        var r = SystemParsing.ParseResolution("   ");
        Assert.False(r.Empty);
        Assert.Equal(Current, SystemParsing.TargetMode(r, Current));
    }

    [Fact]
    public void Resolution_Empty_Toggles800And1024()
    {
        var r = SystemParsing.ParseResolution("");
        Assert.True(r.Empty);
        Assert.Equal(new DisplayMode(800, 600, 32, 60), SystemParsing.TargetMode(r, Current));
        Assert.Equal(new DisplayMode(1024, 768, 16, 70), SystemParsing.TargetMode(r, new DisplayMode(800, 600, 16, 70)));
    }

    [Theory]
    [InlineData(12, "", "shell32.dll,Control_RunDLL ")]
    [InlineData(12, "main", "shell32.dll,Control_RunDLL main.cpl")]
    [InlineData(50, "", "shell32.dll,Control_RunDLL appwiz.cpl")]
    [InlineData(57, "", "shell32.dll,Control_RunDLL timedate.cpl")]
    [InlineData(55, "", "shell32.dll,Control_RunDLL powercfg.cpl")]
    [InlineData(51, "main", "shell32.dll,Control_RunDLL main.cpl")]
    public void ControlPanel(int cmd, string param, string expected) =>
        Assert.Equal(expected, SystemParsing.ControlPanelArgs(cmd, param));

    [Fact]
    public void TempCutoff()
    {
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddDays(-7), SystemParsing.TempCutoffUtc(now, 0));
        Assert.Equal(now.AddDays(-30), SystemParsing.TempCutoffUtc(now, 30));
        var cutoff = now.AddDays(-7);
        Assert.True(SystemParsing.IsOldTempEntry(cutoff.AddDays(-1), cutoff.AddDays(-2), cutoff));
        Assert.False(SystemParsing.IsOldTempEntry(cutoff.AddDays(1), cutoff.AddDays(-2), cutoff));
        Assert.False(SystemParsing.IsOldTempEntry(cutoff.AddDays(-1), cutoff.AddDays(1), cutoff));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("E", 'E')]
    [InlineData("e:", 'e')]
    [InlineData("D:\\", 'D')]
    public void CdDrive(string param, char? expected) => Assert.Equal(expected, SystemParsing.CdDriveLetter(param));

    [Theory]
    [InlineData("16 E", 16, 'E')]
    [InlineData("8", 8, null)]
    [InlineData("E", 0, 'E')]
    [InlineData("E:", 0, 'E')]
    [InlineData("", 0, null)]
    public void CdSpeedParam(string param, int speed, char? drive)
    {
        var p = SystemParsing.CdSpeedParam(param);
        Assert.Equal(speed, p.Speed);
        Assert.Equal(drive, p.Drive);
    }

    [Fact]
    public void CdSpeedBytes()
    {
        Assert.Equal(((byte)0, (byte)1), SystemParsing.CdSpeedBytes(0));
        // 16x: (unsigned)(176.4*16+1) = 2823 = 0x0B07
        Assert.Equal(((byte)0x0B, (byte)0x07), SystemParsing.CdSpeedBytes(16));
    }

    [Theory]
    [InlineData(true, 0, 0, 0, (int)CdDoorAction.Eject)]          // media present
    [InlineData(true, 2, 0x3A, 1, (int)CdDoorAction.Eject)]       // no media, tray closed
    [InlineData(true, 2, 0x3A, 2, (int)CdDoorAction.Close)]       // no media, tray open
    [InlineData(true, 0x72, 0x3A, 2, (int)CdDoorAction.Close)]    // only the low 4 bits are the sense key
    [InlineData(true, 2, 0x04, 1, (int)CdDoorAction.MciFallback)] // not ready, becoming ready
    [InlineData(true, 6, 0x28, 0, (int)CdDoorAction.MciFallback)]
    [InlineData(false, 0, 0, 0, (int)CdDoorAction.MciFallback)]
    public void DoorAction(bool ok, byte key, byte asc, byte ascq, int expected) =>
        Assert.Equal((CdDoorAction)expected, SystemParsing.DoorAction(ok, key, asc, ascq));

    [Theory]
    [InlineData('e', 'E')]
    [InlineData('E', 'E')]
    [InlineData('z', 'Z')]
    [InlineData('1', null)]
    [InlineData('@', null)]
    public void RemoveDriveLetter(char c, char? expected) => Assert.Equal(expected, SystemParsing.RemoveDriveLetter(c));

    [Fact]
    public void AllIdsRegistered()
    {
        var t = new CommandTable();
        SystemCommands.Register(t);
        foreach (int id in new[] { 0, 1, 2, 3, 4, 5, 6, 12, 18, 19, 22, 38, 39, 40, 41, 42, 50, 51, 52, 53, 54, 55, 56, 57, 62, 63, 64, 92, 93, 100, 104, 105, 109 })
            Assert.True(t.Contains(id), $"command {id}");
    }

    [Fact]
    public void StructLayouts()
    {
        Assert.Equal(220, Marshal.SizeOf<DEVMODE>());
        Assert.Equal(72, (int)Marshal.OffsetOf<DEVMODE>(nameof(DEVMODE.dmFields)));
        Assert.Equal(168, (int)Marshal.OffsetOf<DEVMODE>(nameof(DEVMODE.dmBitsPerPel)));
        Assert.Equal(184, (int)Marshal.OffsetOf<DEVMODE>(nameof(DEVMODE.dmDisplayFrequency)));
        Assert.Equal(16, Unsafe.SizeOf<TOKEN_PRIVILEGES>());
        Assert.Equal(28, Unsafe.SizeOf<SERVICE_STATUS>());
        Assert.Equal(12, Unsafe.SizeOf<STORAGE_DEVICE_NUMBER>());
        Assert.Equal(12, Unsafe.SizeOf<STORAGE_PROPERTY_QUERY>());
        if (IntPtr.Size == 8)
        {
            Assert.Equal(56, Unsafe.SizeOf<SCSI_PASS_THROUGH_DIRECT>());
            Assert.Equal(24, (int)Marshal.OffsetOf<SCSI_PASS_THROUGH_DIRECT>(nameof(SCSI_PASS_THROUGH_DIRECT.DataBuffer)));
            Assert.Equal(32, (int)Marshal.OffsetOf<SCSI_PASS_THROUGH_DIRECT>(nameof(SCSI_PASS_THROUGH_DIRECT.SenseInfoOffset)));
            Assert.Equal(88, Unsafe.SizeOf<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>());
            Assert.Equal(32, Unsafe.SizeOf<SP_DEVICE_INTERFACE_DATA>());
            Assert.Equal(32, Unsafe.SizeOf<SP_DEVINFO_DATA>());
            Assert.Equal(8, DeviceInterfaceDetailCbSize);
        }
        else
        {
            Assert.Equal(44, Unsafe.SizeOf<SCSI_PASS_THROUGH_DIRECT>());
            Assert.Equal(76, Unsafe.SizeOf<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>());
            Assert.Equal(28, Unsafe.SizeOf<SP_DEVICE_INTERFACE_DATA>());
            Assert.Equal(28, Unsafe.SizeOf<SP_DEVINFO_DATA>());
            Assert.Equal(6, DeviceInterfaceDetailCbSize);
        }
    }
}
