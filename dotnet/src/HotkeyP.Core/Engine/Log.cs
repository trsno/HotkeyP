// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Engine;

/// <summary>Minimal error log in %LOCALAPPDATA%\HotkeyP\hotkeyp.log, so failures in background threads are not lost.</summary>
public static class Log
{
    static readonly object sync = new();
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("HOTKEYP_DATA") is { Length: > 0 } d ? d
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotkeyP"),
        "hotkeyp.log");

    public static void Error(Exception ex) => Write("ERROR " + ex);
    public static void Info(string message) => Write(message);

    static void Write(string line)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
