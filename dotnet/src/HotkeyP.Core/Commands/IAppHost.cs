// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Commands;

public enum OsdKind { Text, Volume, DiskFree }

/// <summary>
/// Services the user interface provides to commands. Commands run on the executor thread; every member
/// must be safe to call from there (the implementation marshals to the UI thread).
/// </summary>
public interface IAppHost
{
    IntPtr MainWindowHandle { get; }
    void ShowMainWindow();
    void ShowMessage(string text, bool error = true);
    bool Confirm(string text);
    /// <summary>"Delay" option: shows the command for a few seconds with a Cancel button; true when not cancelled.</summary>
    bool Countdown(string title, string detail);
    /// <summary>
    /// On-screen display. <paramref name="level"/> is 0..1 for a bar (volume), null for plain text.
    /// For <see cref="OsdKind.DiskFree"/> the text is in the <see cref="DiskFreeOsd"/> format (one bar per drive).
    /// <paramref name="durationMs"/> 0 means the default time. Showing the same text again hides it (toggle).
    /// </summary>
    void ShowOsd(OsdKind kind, string text, double? level = null, int durationMs = 0);
    /// <summary>Lets the user pick one item; returns the index or -1 when cancelled.</summary>
    int PickFromList(string title, IReadOnlyList<string> items);
    void SetTrayIconVisible(bool visible);
    void HighlightTrayIcon();
    /// <summary>Disable flags or hidden windows changed; refresh the tray icon and menus.</summary>
    void StateChanged();
    void ToggleMagnifier(int magnification);
    /// <summary>A command changed the hotkeys (e.g. Commands list rotated its parameter); save the file and refresh the list.</summary>
    void DocumentChanged();
    /// <summary>Puts a hidden window or application into the tray; clicking the icon restores it.</summary>
    void AddRestoreTrayIcon(string tooltip, IntPtr icon, Action restore);
}
