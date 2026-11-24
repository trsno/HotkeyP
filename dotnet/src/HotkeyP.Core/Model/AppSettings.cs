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
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 620;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotkeyP");
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
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Petr Lastovicka\hotkey");
        if (key == null) return s;
        if (key.GetValue("file") is string f && f.Length > 0) s.File = f;
        if (key.GetValue("closeToTray") is int ct) s.CloseToTray = ct != 0;
        if (key.GetValue("minimizeToTray") is int mt) s.MinimizeToTray = mt != 0;
        if (key.GetValue("trayicon") is int ti) s.ShowTrayIcon = ti != 0;
        if (key.GetValue("hook") is int h && Enum.IsDefined(typeof(HookMode), h)) s.Hook = (HookMode)h;
        if (key.GetValue("mouseDelay") is int md && md > 0) s.MouseDelay = md;
        return s;
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "HotkeyP";

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
        return key?.GetValue(RunName) is string v && v.Contains("HotkeyP", StringComparison.OrdinalIgnoreCase);
    }
}
