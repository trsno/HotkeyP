// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HotkeyP.Commands;
using HotkeyP.Model;

namespace HotkeyP.App;

/// <summary>Entry of the navigation pane: a filter, a category or a section header.</summary>
public sealed class NavItem
{
    public required string Name { get; init; }
    public string Glyph { get; init; } = "";
    public bool IsHeader { get; init; }
    public bool IsSelectable => !IsHeader;
    /// <summary>Category index (>= 0), or null for a filter.</summary>
    public int? Category { get; init; }
    public Func<Hotkey, bool> Filter { get; init; } = _ => true;
    public int Count { get; set; }
}

/// <summary>One hotkey as shown in the list.</summary>
public sealed class HotkeyRow
{
    public required Hotkey Hotkey { get; init; }
    public required int Index { get; init; }
    public string Number => (Index + 1).ToString(CultureInfo.CurrentCulture);
    public string Title => CommandRunner.DisplayName(Hotkey);
    public IReadOnlyList<string> Keys => KeyFormatter.Parts(Hotkey);
    public bool HasNoKeys => !Hotkey.HasTrigger;
    public ImageSource? Icon { get; init; }
    public string Glyph => Icon == null ? CommandGlyphs.For(Hotkey) : "";

    public string Subtitle
    {
        get
        {
            if (Hotkey.IsCommand)
            {
                var name = CommandCatalog.NameOf(Hotkey.Command);
                return string.IsNullOrEmpty(Hotkey.Args) ? name : $"{name}: {Hotkey.Args.Replace('\n', ' ')}";
            }
            return string.IsNullOrEmpty(Hotkey.Args) ? Hotkey.Exe : $"{Hotkey.Exe} {Hotkey.Args}";
        }
    }

    public IEnumerable<string> Badges
    {
        get
        {
            if (Hotkey.AutoStart) yield return "Startup";
            if (Hotkey.TrayMenu) yield return "Tray";
            if (Hotkey.Admin) yield return "Admin";
            if (Hotkey.Ask) yield return "Asks first";
        }
    }

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || Subtitle.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || KeyFormatter.Format(Hotkey).Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}

static class CommandGlyphs
{
    public static string For(Hotkey hk)
    {
        if (!hk.IsCommand)
        {
            var exe = hk.Exe.Trim();
            if (exe.Contains("://") || exe.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return ""; // globe
            return ""; // document
        }
        return CommandCatalog.Get(hk.Command)?.Group switch
        {
            CommandGroup.Window => "",
            CommandGroup.Mouse => "",
            CommandGroup.Keyboard => "",
            CommandGroup.Audio => "",
            CommandGroup.Display => "",
            CommandGroup.Power => "",
            CommandGroup.System => "",
            CommandGroup.Drives => "",
            _ => "",
        };
    }

    public static string For(CommandGroup g) => For(new Hotkey { Command = CommandCatalog.All.First(c => c.Group == g).Id });
}

/// <summary>Icons of programs and documents, cached by path.</summary>
static class FileIcons
{
    static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds a program given without a path (e.g. "notepad.exe") the way Windows would.</summary>
    static string? Resolve(string file)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Prepend(Environment.SystemDirectory);
        foreach (var d in dirs)
        {
            try
            {
                var f = Path.Combine(d.Trim(), file);
                if (File.Exists(f)) return f;
                if (!Path.HasExtension(file) && File.Exists(f + ".exe")) return f + ".exe";
            }
            catch (ArgumentException) { }
        }
        // App Paths registry, e.g. "chrome.exe"
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + file);
        return key?.GetValue(null) as string;
    }

    public static ImageSource? For(Hotkey hk)
    {
        if (hk.IsCommand) return null;
        var path = Environment.ExpandEnvironmentVariables(hk.Exe.Trim().Trim('"'));
        if (path.Length == 0 || path.Contains("://") || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
        if (cache.TryGetValue(path, out var img)) return img;
        img = null;
        try
        {
            if (!Path.IsPathRooted(path)) path = Resolve(path) ?? path;
            if (File.Exists(path) || Directory.Exists(path))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null)
                {
                    img = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        cache[path] = img;
        return img;
    }
}

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
