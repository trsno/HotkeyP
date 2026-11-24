using HotkeyP.Commands;

namespace HotkeyP.Core.Tests;

/// <summary>
/// Calls the real Core Audio interfaces without changing anything (volume change of 0 on the default device).
/// Proves the COM interface layouts; runs only when HOTKEYP_LIVE_AUDIO=1 because it needs an audio device.
/// </summary>
public class LiveAudioTests
{
    [Fact]
    public void ReadsDefaultDeviceVolume()
    {
        if (Environment.GetEnvironmentVariable("HOTKEYP_LIVE_AUDIO") != "1") return;
        var st = VolumeController.Apply(new VolumeRequest("Mixer", false, 0, VolumeAction.Change, 0));
        Assert.NotNull(st);
        Assert.InRange(st!.Percent, 0, 100);
        Console.WriteLine($"LIVE volume {st.Percent} muted {st.Muted}");
    }
}
