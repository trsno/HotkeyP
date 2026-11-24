// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Text.Json;
using HotkeyP.Engine;
using Microsoft.Win32;

namespace HotkeyP.Model;

public enum ThemeChoice { System, Light, Dark }

/// <summary>User settings, stored as JSON in %APPDATA%\HotkeyP\settings.json.</summary>
public sealed class AppSettings
{
    public string? File { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public HookMode Hook { get; set; } = HookMode.WhenNeeded;
    public int MouseDelay { get; set; } = 600;
    /// <summary>Mouse buttons passed to applications at once (not held back while a button combination may follow).
    /// Button ids as in MouseButton: 0 left, 1 right, 4 middle, 5 X1, 6 X2.</summary>
    public List<int> NotDelayedButtons { get; set; } = [0];
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 620;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    // declared before Folder: static initializers run in source order
    /// <summary>Separate data folder for tests (also a separate single instance, log and no registry import).</summary>
    public static string? DataOverride { get; } = Environment.GetEnvironmentVariable("HOTKEYP_DATA") is { Length: > 0 } d ? d : null;

    /// <summary>Settings folder; HOTKEYP_DATA points a test instance at a folder of its own.</summary>
    public static string Folder { get; } = DataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotkeyP");
    static string SettingsPath => Path.Combine(Folder, "settings.json");
    public static string DefaultHotkeysPath => Path.Combine(Folder, "hotkeys.htk");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(SettingsPath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Log.Error(ex); }
        return ImportFromOriginal();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error(ex); }
    }

    /// <summary>First run: take over the hotkeys file and main options of the original HotkeyP, if it was used.</summary>
    static AppSettings ImportFromOriginal()
    {
        var s = new AppSettings();
        if (DataOverride != null) return s;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Petr Lastovicka\hotkey");
        if (key == null) return s;
        if (key.GetValue("file") is string f && f.Length > 0) s.File = f;
        if (key.GetValue("closeToTray") is int ct) s.CloseToTray = ct != 0;
        if (key.GetValue("minimizeToTray") is int mt) s.MinimizeToTray = mt != 0;
        if (key.GetValue("trayicon") is int ti) s.ShowTrayIcon = ti != 0;
        if (key.GetValue("hook") is int h && Enum.IsDefined(typeof(HookMode), h)) s.Hook = (HookMode)h;
        if (key.GetValue("mouseDelay") is int md && md > 0) s.MouseDelay = md;
        var nd = new List<int>();
        foreach (var (name, id) in new[] { ("notDelayLeftButton", 0), ("notDelayRightButton", 1), ("notDelayM", 4), ("notDelayX1", 5), ("notDelayX2", 6) })
            if (key.GetValue(name) is int v ? v != 0 : id == 0) nd.Add(id);
        s.NotDelayedButtons = nd;
        return s;
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // a value name of its own: the original HotkeyP uses "HotkeyP", and that entry must not be repointed or deleted
    const string RunName = "HotkeyP (.NET)";

    /// <summary>Registers or removes HotkeyP in the current user's startup programs.</summary>
    public static void SetStartWithWindows(bool on, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{exePath}\" -hide");
        else if (key.GetValue(RunName) != null) key.DeleteValue(RunName);
    }

    public static bool IsStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }
}
