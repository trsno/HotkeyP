// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Port of diskFree, addDrive in source/Commands.cpp.
using System.Globalization;
using System.Runtime.InteropServices;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054

/// <summary>One line of the disk free space display.</summary>
/// <param name="Drive">Drive name, e.g. "C:".</param>
/// <param name="FreeText">Free space, e.g. "12.3 GB".</param>
/// <param name="FreeFraction">Free space / total size, 0..1 (the length of the bar).</param>
public sealed record DiskFreeLine(string Drive, string FreeText, double FreeFraction);

/// <summary>
/// Text format of <see cref="OsdKind.DiskFree"/>: one line per drive, separated by '\n'; each line is
/// "C:\t12.3 GB\t0.4567" (drive, free space, free fraction with invariant culture). The UI draws a bar per line;
/// <see cref="Parse"/> decodes the text.
/// </summary>
public static class DiskFreeOsd
{
    public static string Format(IEnumerable<DiskFreeLine> lines) =>
        string.Join("\n", lines.Select(l => $"{l.Drive}\t{l.FreeText}\t{l.FreeFraction.ToString("0.####", CultureInfo.InvariantCulture)}"));

    public static IReadOnlyList<DiskFreeLine> Parse(string text)
    {
        var result = new List<DiskFreeLine>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t');
            if (f.Length < 3) continue;
            double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double frac);
            result.Add(new DiskFreeLine(f[0], f[1], Math.Clamp(frac, 0, 1)));
        }
        return result;
    }

    /// <summary>addDrive: GB above 2 000 000 000 bytes, MB above 2 000 000, else kB.</summary>
    public static string FormatFree(ulong free, int precision = 1)
    {
        precision = Math.Clamp(precision, 0, 6);
        string fmt = "F" + precision.ToString(CultureInfo.InvariantCulture);
        if (free > 2_000_000_000) return (free / 1073741824.0).ToString(fmt, CultureInfo.InvariantCulture) + " GB";
        if (free > 2_000_000) return (free / 1048576.0).ToString(fmt, CultureInfo.InvariantCulture) + " MB";
        return (free / 1024).ToString(CultureInfo.InvariantCulture) + " kB";
    }

    public static DiskFreeLine Line(char drive, ulong free, ulong total) =>
        new($"{drive}:", FormatFree(free), total == 0 ? 0 : (double)free / total);

    const uint DRIVE_FIXED = 3, DRIVE_REMOTE = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetDiskFreeSpaceEx(string root, out ulong freeToCaller, out ulong total, out ulong totalFree);

    /// <summary>Fixed and network drives A: to Z:.</summary>
    public static List<DiskFreeLine> Measure()
    {
        var list = new List<DiskFreeLine>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string root = c + @":\";
            uint t = GetDriveType(root);
            if (t != DRIVE_FIXED && t != DRIVE_REMOTE) continue;
            if (GetDiskFreeSpaceEx(root, out ulong free, out ulong total, out _)) list.Add(Line(c, free, total));
        }
        return list;
    }

    /// <summary>Command 24. The level is the free fraction of the first drive; the text has every drive.</summary>
    public static void Show(CommandContext c)
    {
        var lines = Measure();
        // the parameter is the display time in milliseconds, as in the original
        c.Host.ShowOsd(OsdKind.DiskFree, Format(lines), lines.Count > 0 ? lines[0].FreeFraction : null, Math.Max(0, c.IntParam(0)));
    }
}
