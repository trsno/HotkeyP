// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Diagnostics;
using System.IO;
using System.Windows;
using HotkeyP.Commands;
using HotkeyP.Engine;
using HotkeyP.Model;

namespace HotkeyP.App;

/// <summary>Owns the document, settings, engine, command runner and windows. Implements the services commands use.</summary>
public sealed class AppController : IAppHost, IDisposable
{
    readonly App app;
    MainWindow? mainWindow;
    OsdWindow? osd;
    TrayIcon? tray;
    volatile Hotkey[] snapshot = [];
    bool exiting;

    public AppSettings Settings { get; private set; } = new();
    public HotkeyDocument Document { get; private set; } = new();
    public string FilePath { get; private set; } = AppSettings.DefaultHotkeysPath;
    public HotkeyEngine Engine { get; private set; } = null!;
    public CommandRunner Runner { get; private set; } = null!;

    /// <summary>Raised on the UI thread when hotkeys, the file or the enabled state change.</summary>
    public event Action? Changed;

    public AppController(App app) => this.app = app;

    public void Start(bool hidden, string? file)
    {
        Settings = AppSettings.Load();
        // the startup entry in the registry is the truth (it may have been set outside the app)
        if (AppSettings.DataOverride == null) Settings.StartWithWindows = AppSettings.IsStartWithWindows();
        ApplyTheme();
        Runner = new CommandRunner(this, () => snapshot);
        Engine = new HotkeyEngine(Runner.IsActive, Runner.OnTriggered);
        Runner.Engine = Engine;

        Open(file ?? Settings.File ?? AppSettings.DefaultHotkeysPath, showErrors: !hidden);

        tray = new TrayIcon(this);
        tray.Visible = Settings.ShowTrayIcon;

        // hotkeys marked "run at startup"
        foreach (var hk in Document.Hotkeys.Where(h => h.AutoStart).ToList())
            Runner.Post(() => Runner.Execute(hk));

        if (!hidden || !Settings.ShowTrayIcon) ShowMainWindow();
    }

    public void ApplyTheme()
    {
#pragma warning disable WPF0001
        app.ThemeMode = Settings.Theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
    }

    // ------------------------------------------------------------------ document

    public bool Open(string path, bool showErrors = true)
    {
        try
        {
            Document = File.Exists(path) ? HotkeyDocument.Load(path) : new HotkeyDocument();
            FilePath = path;
            Settings.File = path;
            Settings.Save();
            Apply();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (showErrors) ShowMessage($"Cannot open {path}\n{ex.Message}");
            Log.Error(ex);
            return false;
        }
    }

    public void SaveAs(string path)
    {
        FilePath = path;
        Settings.File = path;
        Settings.Save();
        Save();
    }

    /// <summary>Writes the hotkeys file and re-registers the hotkeys.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Document.Save(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Cannot save {FilePath}\n{ex.Message}");
        }
        Apply();
    }

    void Apply()
    {
        snapshot = Document.Hotkeys.ToArray();
        Engine.SetHotkeys(snapshot, new EngineOptions { Hook = Settings.Hook, MouseDelay = Settings.MouseDelay, NotDelayedButtons = Settings.NotDelayedButtons.ToHashSet() });
        Changed?.Invoke();
    }

    public void SaveSettings()
    {
        Settings.Save();
        ApplyTheme();
        if (tray != null) tray.Visible = Settings.ShowTrayIcon && !Runner.TrayIconHidden;
        // a test instance (HOTKEYP_DATA) never touches the startup entry; the setting only changes when it differs
        if (AppSettings.DataOverride == null && Settings.StartWithWindows != AppSettings.IsStartWithWindows())
        try { AppSettings.SetStartWithWindows(Settings.StartWithWindows, Environment.ProcessPath!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Log.Error(ex); }
        Apply();
    }

    public bool HotkeysEnabled
    {
        get => !Engine.DisableAll;
        set
        {
            Engine.DisableAll = !value;
            StateChanged();
        }
    }

    public void Exit()
    {
        exiting = true;
        mainWindow?.Close();
        app.Shutdown();
    }

    public bool IsExiting => exiting;

    // ------------------------------------------------------------------ IAppHost (callable from any thread)

    public IntPtr MainWindowHandle { get; private set; }

    void Ui(Action a)
    {
        if (app.Dispatcher.CheckAccess()) a();
        else app.Dispatcher.BeginInvoke(a);
    }

    T UiSync<T>(Func<T> f) => app.Dispatcher.CheckAccess() ? f() : app.Dispatcher.Invoke(f);

    public void ShowMainWindow() => Ui(() =>
    {
        if (mainWindow == null)
        {
            mainWindow = new MainWindow(this);
            mainWindow.Closed += (_, _) =>
            {
                mainWindow = null;
                MainWindowHandle = IntPtr.Zero;
                if (!exiting) app.Dispatcher.BeginInvoke(TrimMemory, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            mainWindow.Show();
            MainWindowHandle = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
        }
        if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
        mainWindow.Show();
        WindowActivator.Bring(mainWindow);
    });

    public void ShowMessage(string text, bool error = true) => Ui(() =>
        MessageBox.Show(mainWindow is { IsVisible: true } ? mainWindow : null!, text, "HotkeyP", MessageBoxButton.OK,
            error ? MessageBoxImage.Warning : MessageBoxImage.Information));

    public bool Confirm(string text) => UiSync(() =>
        MessageBox.Show(text, "HotkeyP", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    int countdownActive;

    public bool Countdown(string title, string detail)
    {
        // only one countdown at a time; further presses are ignored, as in the original
        if (Interlocked.CompareExchange(ref countdownActive, 1, 0) != 0) return false;
        try
        {
            var fg = Native.NativeMethods.GetForegroundWindow();
            bool run = UiSync(() => CountdownDialog.Run(title, detail));
            Native.NativeMethods.SetForegroundWindow(fg);
            if (run) Thread.Sleep(100);
            return run;
        }
        finally { countdownActive = 0; }
    }

    public void ShowOsd(OsdKind kind, string text, double? level = null, int durationMs = 0) => Ui(() =>
    {
        osd ??= new OsdWindow();
        osd.ShowText(kind, text, level, durationMs);
    });

    public int PickFromList(string title, IReadOnlyList<string> items) => UiSync(() => PickListWindow.Pick(title, items));

    public void SetTrayIconVisible(bool visible) => Ui(() => { if (tray != null) tray.Visible = visible && Settings.ShowTrayIcon; });

    public void HighlightTrayIcon() => Ui(() => tray?.Highlight());

    public void StateChanged() => Ui(() =>
    {
        tray?.Refresh();
        Changed?.Invoke();
    });

    public void ToggleMagnifier(int magnification)
    {
        // the built-in Windows Magnifier replaces HotkeyP's own zoom window
        var running = Process.GetProcessesByName("Magnify");
        if (running.Length > 0)
        {
            foreach (var p in running) { try { p.CloseMainWindow(); } catch (InvalidOperationException) { } p.Dispose(); }
            return;
        }
        try { Process.Start(new ProcessStartInfo("magnify.exe") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowMessage(ex.Message); }
    }

    public void DocumentChanged() => Ui(Save);

    public void AddRestoreTrayIcon(string tooltip, IntPtr icon, Action restore) => Ui(() =>
        TrayIcon.AddRestoreIcon(tooltip, icon, () => Runner.Post(restore)));

    /// <summary>After the main window closes, HotkeyP only waits for hotkeys; give the window's memory back to Windows.</summary>
    static void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    public void Dispose()
    {
        // windows hidden by "Hide window" / "Minimize to tray" must not stay invisible after HotkeyP exits
        try { WindowCommands.UnhideAll(); } catch (Exception ex) { Log.Error(ex); }
        tray?.Dispose();
        Engine?.Dispose();
        Runner?.Dispose();
    }
}
