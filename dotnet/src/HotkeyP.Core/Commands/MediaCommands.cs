// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Commands;

/// <summary>Audio volume, wallpaper, disk free space, snapshots and desktop icons.</summary>
static class MediaCommands
{
    public static void Register(CommandTable t)
    {
        t.Add([13, 14, 15, 16, 17, 58, 78], Volume);
        t.Add(23, c => Wallpaper.Change(c, WallpaperAction.Random));
        t.Add(82, c => Wallpaper.Change(c, WallpaperAction.Random));
        t.Add(68, c => Wallpaper.Change(c, WallpaperAction.Next));
        t.Add(69, c => Wallpaper.Change(c, WallpaperAction.Previous));
        t.Add(24, DiskFreeOsd.Show);
        t.Add(89, c => Screenshot.Take(c, desktop: true));
        t.Add(90, c => Screenshot.Take(c, desktop: false));
        t.Add(106, c => DesktopIcons.Run(c, restore: false));
        t.Add(107, c => DesktopIcons.Run(c, restore: true));
    }

    /// <summary>volumeCmd: changes the volume, then shows the level of the line ("???" when no line matched).</summary>
    static void Volume(CommandContext c)
    {
        var req = VolumeRequest.ForCommand(c.Command, c.Param);
        var st = VolumeController.Apply(req);
        c.Host.ShowOsd(OsdKind.Volume, VolumeText(req, st), st?.Percent / 100.0);
    }

    internal static string VolumeText(VolumeRequest req, VolumeState? st) =>
        $"{req.Label}: " + (st == null ? "???" : st.Muted ? "Muted" : $"{st.Percent} %");
}
