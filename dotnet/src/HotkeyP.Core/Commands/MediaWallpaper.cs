// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Port of changeWallpaper, search, searchC in source/Commands.cpp.
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054

enum WallpaperAction { Random = 0, Next = 1, Previous = 2 }

/// <summary>Settings HotkeyP keeps in the registry, in the same key and format as the C++ version.</summary>
static class MediaSettings
{
    public const string SubKey = @"Software\Petr Lastovicka\hotkey";

    public static object? Get(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Set(string name, object value, RegistryValueKind kind)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue(name, value, kind);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { Engine.Log.Error(ex); }
    }
}

/// <summary>
/// The current wallpaper of each folder, so Next/Previous continue from it. Stored as the REG_BINARY value "wallpaper":
/// UTF-16 pairs (folder, last file), each null-terminated.
/// </summary>
sealed class WallpaperState
{
    public List<(string Dir, string File)> Pairs { get; } = new();

    public static WallpaperState Parse(byte[]? data)
    {
        var st = new WallpaperState();
        if (data == null || data.Length < 2) return st;
        string s = Encoding.Unicode.GetString(data, 0, data.Length & ~1);
        if (s.Length == 0 || s[^1] != '\0') return st; // not null-terminated: ignored like in the C++
        var parts = s[..^1].Split('\0');
        for (int i = 0; i < parts.Length; i += 2)
            st.Pairs.Add((parts[i], i + 1 < parts.Length ? parts[i + 1] : ""));
        return st;
    }

    public byte[] Serialize()
    {
        var sb = new StringBuilder();
        foreach (var (d, f) in Pairs) sb.Append(d).Append('\0').Append(f).Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    /// <summary>The last file of a folder; the folder text must match exactly (case-sensitive, as _tcscmp).</summary>
    public string Last(string dir) => Pairs.FirstOrDefault(p => p.Dir == dir).File ?? "";

    public void SetLast(string dir, string file)
    {
        int i = Pairs.FindIndex(p => p.Dir == dir);
        if (i >= 0) Pairs[i] = (dir, file);
        else Pairs.Add((dir, file));
    }
}

static class Wallpaper
{
    /// <summary>Pictures are files whose extension starts with BMP, JPG, JPE or GIF (no PNG, as in the C++).</summary>
    public static bool IsPicture(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        if (dot < 0 || fileName.Length - dot - 1 < 3) return false;
        string e = fileName.Substring(dot + 1, 3).ToUpperInvariant();
        return e is "BMP" or "JPG" or "JPE" or "GIF";
    }

    static readonly EnumerationOptions listOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
    };

    /// <summary>
    /// Pictures in directory listing order. Subfolders (except names starting with '.') are searched where they
    /// appear in the listing, like the recursive FindFirstFile search.
    /// </summary>
    public static List<string> ListPictures(string dir, bool recurse)
    {
        var list = new List<string>();
        Search(dir, recurse, list, 0);
        return list;
    }

    static void Search(string dir, bool recurse, List<string> list, int depth)
    {
        IEnumerable<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", listOptions).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }
        foreach (var e in entries)
        {
            if (e is DirectoryInfo)
            {
                if (recurse && !e.Name.StartsWith('.') && depth < 32) Search(e.FullName, recurse, list, depth + 1);
            }
            else if (IsPicture(e.Name)) list.Add(e.FullName);
        }
    }

    /// <summary>
    /// The picture to show. Next follows <paramref name="last"/> and wraps to the first; Previous wraps to the last.
    /// When <paramref name="last"/> is not in the list, Next gives the first picture and Previous the last one.
    /// </summary>
    public static string? Pick(IReadOnlyList<string> files, WallpaperAction action, string? last, Random rnd)
    {
        if (files.Count == 0) return null;
        int idx = -1;
        if (!string.IsNullOrEmpty(last))
        {
            for (int i = 0; i < files.Count; i++)
                if (string.Equals(files[i], last, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        }
        return action switch
        {
            WallpaperAction.Next => files[idx < 0 ? 0 : (idx + 1) % files.Count],
            WallpaperAction.Previous => files[idx <= 0 ? files.Count - 1 : idx - 1],
            _ => files[rnd.Next(files.Count)],
        };
    }

    /// <summary>
    /// Chooses the picture for a parameter: a file is used directly, a folder is searched recursively, an empty
    /// parameter means the Windows folder (not recursive). Returns null and sets <paramref name="error"/> on failure.
    /// </summary>
    public static string? Choose(string dir, WallpaperAction action, string? last, Random rnd, out string? error)
    {
        error = null;
        if (dir.Length > 0)
        {
            if (File.Exists(dir)) return dir;
            if (!Directory.Exists(dir))
            {
                error = $"Cannot find folder or file {dir}";
                return null;
            }
            return Pick(ListPictures(dir, true), action, last, rnd);
        }
        return Pick(ListPictures(Environment.GetFolderPath(Environment.SpecialFolder.Windows), false), action, last, rnd);
    }

    /// <summary>Removes the quotes around the parameter.</summary>
    public static string Unquote(string p) =>
        p.Length >= 2 && p[0] == '"' && p[^1] == '"' ? p[1..^1] : p;

    // ------------------------------------------------------------ command

    const uint SPI_SETDESKWALLPAPER = 0x14, SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, string pvParam, uint winIni);

    public static void Change(CommandContext c, WallpaperAction action)
    {
        string dir = Unquote(c.Param);
        var state = WallpaperState.Parse(MediaSettings.Get("wallpaper") as byte[]);
        string? result = Choose(dir, action, state.Last(dir), Random.Shared, out string? error);
        if (error != null) { c.Host.ShowMessage(error); return; }
        if (string.IsNullOrEmpty(result)) return;
        state.SetLast(dir, result);
        MediaSettings.Set("wallpaper", state.Serialize(), RegistryValueKind.Binary);
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, result, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            c.Host.ShowMessage($"Cannot set the wallpaper {result} (error {Marshal.GetLastWin32Error()})");
    }
}
