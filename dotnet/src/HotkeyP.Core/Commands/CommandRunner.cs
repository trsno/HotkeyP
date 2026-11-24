// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using HotkeyP.Engine;
using HotkeyP.Model;
using static HotkeyP.Native.NativeMethods;

namespace HotkeyP.Commands;

/// <summary>Everything a command implementation gets when it runs.</summary>
public sealed class CommandContext
{
    public required CommandRunner Runner { get; init; }
    public required int Command { get; init; }
    /// <summary>The parameter text (Hotkey.Args).</summary>
    public required string Param { get; init; }
    /// <summary>The hotkey being executed; null when the command was run from the command line or a multi command.</summary>
    public Hotkey? Hotkey { get; init; }
    public TriggerKind Kind { get; init; } = TriggerKind.OnlyDown;
    public IAppHost Host => Runner.Host;

    /// <summary>Parameter as an integer, or <paramref name="fallback"/> when it is empty or not a number.</summary>
    public int IntParam(int fallback = 0) => int.TryParse(Param.Trim(), out int v) ? v : fallback;
}

/// <summary>Maps command ids to implementations. Each command group registers its commands here.</summary>
public sealed class CommandTable
{
    readonly Dictionary<int, Action<CommandContext>> map = new();

    public void Add(int id, Action<CommandContext> run) => map[id] = run;
    public void Add(IEnumerable<int> ids, Action<CommandContext> run) { foreach (var id in ids) map[id] = run; }
    public bool TryGet(int id, out Action<CommandContext> run) => map.TryGetValue(id, out run!);
    public bool Contains(int id) => map.ContainsKey(id);
}

/// <summary>
/// Executes hotkeys: starts programs and runs internal commands. Work is done on one STA executor thread in the order
/// the hotkeys were pressed, so the hook thread is never blocked.
/// </summary>
public sealed class CommandRunner : IDisposable
{
    readonly BlockingCollection<Action> work = new();
    readonly Thread executor;
    readonly Func<IReadOnlyList<Hotkey>> hotkeys;

    public IAppHost Host { get; }
    public HotkeyEngine? Engine { get; set; }
    public CommandTable Table { get; } = new();

    /// <summary>Set by commands that toggle state, read by the UI (tray icon look, menu check marks).</summary>
    public bool TrayIconHidden { get; set; }

    public CommandRunner(IAppHost host, Func<IReadOnlyList<Hotkey>> hotkeys)
    {
        Host = host;
        this.hotkeys = hotkeys;
        RegisterAll(Table);
        executor = new Thread(() =>
        {
            foreach (var a in work.GetConsumingEnumerable())
            {
                try { a(); } catch (Exception ex) { Log.Error(ex); }
            }
        }) { IsBackground = true, Name = "HotkeyP executor" };
        executor.SetApartmentState(ApartmentState.STA);
        executor.Start();
    }

    /// <summary>Every command group adds its commands here.</summary>
    internal static void RegisterAll(CommandTable t)
    {
        HotkeyPCommands.Register(t);
        WindowCommands.Register(t);
        SystemCommands.Register(t);
        InputCommands.Register(t);
        MediaCommands.Register(t);
    }

    public void Post(Action a) => work.Add(a);

    /// <summary>Called by the engine when a hotkey is pressed.</summary>
    public void OnTriggered(Hotkey hk, TriggerKind kind)
    {
        if (hk.Command == 94)
        {
            // "Macro to active window" plays key-down and key-up separately, so holding the hotkey holds the keys
            Post(() => Run(hk.Command, hk.Args, hk, kind));
            return;
        }
        if (kind == TriggerKind.Up) return;
        if (!hk.IsLocalCommand()) Host.HighlightTrayIcon();
        Post(() => Execute(hk, fromKeyPress: true));
    }

    /// <summary>Runs a hotkey: asks or waits first if configured, then the program or command, then the sound.</summary>
    public void Execute(Hotkey hk, bool fromKeyPress = false)
    {
        // C++ delayAndExecuteHotKey: "ask" shows a yes/no question; otherwise "delay" shows a 5 s countdown with Cancel
        string detail = hk.IsCommand ? $"{CommandCatalog.NameOf(hk.Command)} {hk.Args}" : $"{hk.Exe} {hk.Args}";
        if (hk.Ask)
        {
            if (!Host.Confirm($"Do you want to execute this command?\n\n{hk.Note}\n{detail}")) return;
        }
        else if (hk.Delay && !Host.Countdown(DisplayName(hk), detail.Trim())) return;
        if (hk.IsCommand) Run(hk.Command, hk.Args, hk, TriggerKind.OnlyDown);
        else Launch(hk);
        if (!string.IsNullOrEmpty(hk.Sound))
            PlaySound(Environment.ExpandEnvironmentVariables(hk.Sound), IntPtr.Zero, SND_FILENAME | SND_ASYNC);
    }

    /// <summary>Runs an internal command directly (multi command, command line, tray menu).</summary>
    public void Run(int cmd, string param, Hotkey? hk = null, TriggerKind kind = TriggerKind.OnlyDown)
    {
        if (!Table.TryGet(cmd, out var run))
        {
            Host.ShowMessage($"The command \"{CommandCatalog.NameOf(cmd)}\" is not available in this version yet.");
            return;
        }
        run(new CommandContext { Runner = this, Command = cmd, Param = param ?? "", Hotkey = hk, Kind = kind });
    }

    public static string DisplayName(Hotkey hk) =>
        !string.IsNullOrEmpty(hk.Note) ? hk.Note : hk.IsCommand ? CommandCatalog.NameOf(hk.Command) : Path.GetFileName(hk.Exe);

    /// <summary>Hotkeys of the current document, numbered from 1 in multi commands.</summary>
    public IReadOnlyList<Hotkey> Hotkeys => hotkeys();

    /// <summary>
    /// A hotkey is active unless it targets a specific window that is not in the foreground. Inactive hotkeys
    /// are passed to the application as ordinary keys.
    /// </summary>
    public bool IsActive(Hotkey hk) => IsActive(hk, 0);

    bool IsActive(Hotkey hk, int depth)
    {
        if (depth > 8) return false;
        if (hk.Command is 73 or 74 or 94)
            return WindowTools.ForegroundMatches(WindowTools.SplitWindowParams(hk.Args).Window);
        if (hk.Command is 61 or 70)
        {
            var list = Hotkeys;
            foreach (var num in Model.MultiCommandParam.Parse(hk.Args))
            {
                int n = num.Value;
                if (n >= 1 && n <= list.Count && !ReferenceEquals(list[n - 1], hk) && IsActive(list[n - 1], depth + 1)) return true;
                if (hk.Command == 70) break;
            }
            return false;
        }
        return true;
    }

    /// <summary>Hotkeys referenced by a multi command (61) or commands list (70), by 1-based number.</summary>
    public List<Hotkey> SubCommands(Hotkey hk)
    {
        var list = Hotkeys;
        var result = new List<Hotkey>();
        foreach (var num in Model.MultiCommandParam.Parse(hk.Args))
        {
            int n = num.Value;
            if (n < 1 || n > list.Count) break; // C++: "Invalid multi-command number" stops the list
            if (!ReferenceEquals(list[n - 1], hk)) result.Add(list[n - 1]);
        }
        return result;
    }

    // ---------------------------------------------------------------- programs

    static readonly int[] ShowCmd = [SW_SHOWNORMAL, SW_SHOWMAXIMIZED, SW_SHOWMINIMIZED];

    static readonly ProcessPriorityClass[] Priorities =
    [
        ProcessPriorityClass.Idle, ProcessPriorityClass.Normal, ProcessPriorityClass.High,
        ProcessPriorityClass.RealTime, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.AboveNormal,
    ];

    static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static bool IsExe(string path)
    {
        var ext = Path.GetExtension(path.Trim('"'));
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".com", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Starts the program. If it is already running (and multiple instances are off) the hotkey toggles it instead:
    /// a foreground window is minimized, otherwise it is restored and brought to the front.
    /// </summary>
    public void Launch(Hotkey hk)
    {
        string exe = Environment.ExpandEnvironmentVariables(hk.Exe).Trim();
        string dir = Environment.ExpandEnvironmentVariables(hk.Dir);
        if (exe.Length == 0) return;

        if (!IsExe(exe))
        {
            // document, folder, URL or shell: path
            if (exe.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) exe = "http://" + exe;
            if (string.IsNullOrEmpty(dir) && !exe.Contains("://") && !exe.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                dir = Path.GetDirectoryName(exe.Trim('"')) ?? "";
            Shell(exe, hk.Args, dir, hk.Admin ? "runas" : null, ShowCmd[(int)hk.Show]);
            return;
        }

        if (!hk.MultipleInstances)
        {
            if (hk.ProcessId != 0 && !ProcessAlive(hk.ProcessId)) hk.ProcessId = 0;
            var w = WindowTools.FindProgramWindow(exe, hk.ProcessId);
            if (w != IntPtr.Zero)
            {
                if (w == GetForegroundWindow() && !IsIconic(w))
                {
                    PostMessage(w, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);
                }
                else
                {
                    if (IsIconic(w)) SendMessageTimeout(w, WM_SYSCOMMAND, SC_RESTORE, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, out _);
                    if (hk.Opacity != 0) WindowTools.SetOpacity(w, hk.Opacity);
                    SetForegroundWindow(w);
                }
                return;
            }
        }

        string args = hk.Args;
        if (args.Length == 0 && exe.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) args = "/S";
        if (string.IsNullOrEmpty(dir))
        {
            var full = Path.IsPathRooted(exe.Trim('"')) ? exe.Trim('"') : SearchPath(exe.Trim('"'));
            dir = full != null ? Path.GetDirectoryName(full) ?? "" : "";
        }
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Host.ShowMessage($"Invalid working directory: {dir}");
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(exe.Trim('"'), args)
            {
                UseShellExecute = true,
                WorkingDirectory = dir,
                WindowStyle = hk.Show switch { ShowMode.Maximized => ProcessWindowStyle.Maximized, ShowMode.Minimized => ProcessWindowStyle.Minimized, _ => ProcessWindowStyle.Normal },
            };
            if (hk.Admin && !IsElevated()) psi.Verb = "runas";
            var p = Process.Start(psi);
            if (p == null) return;
            hk.ProcessId = p.Id;
            try { if (hk.Priority != Priority.Normal) p.PriorityClass = Priorities[(int)hk.Priority]; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            if (hk.Opacity != 0) ApplyOpacityLater(p, hk.Opacity);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // the user cancelled the UAC prompt
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Host.ShowMessage($"Cannot start {hk.Exe}: {ex.Message}");
        }
    }

    void Shell(string file, string args, string dir, string? verb, int show)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = true, WorkingDirectory = dir, Verb = verb ?? "",
                WindowStyle = show switch
                {
                    SW_SHOWMAXIMIZED => ProcessWindowStyle.Maximized,
                    SW_SHOWMINIMIZED => ProcessWindowStyle.Minimized,
                    _ => ProcessWindowStyle.Normal,
                },
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Host.ShowMessage($"Cannot open {file}: {ex.Message}");
        }
    }

    static bool ProcessAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    static string? SearchPath(string file)
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var f = Path.Combine(d.Trim(), file);
                if (File.Exists(f)) return f;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>The window of a new process appears later; set its opacity once it exists.</summary>
    static void ApplyOpacityLater(Process p, int opacity)
    {
        var oldForeground = GetForegroundWindow();
        new Thread(() =>
        {
            try
            {
                try { p.WaitForInputIdle(20000); } catch (InvalidOperationException) { }
                for (int i = 0; i < 20; i++)
                {
                    var w = WindowTools.FindProcessWindow(p.Id);
                    if (w != IntPtr.Zero) { WindowTools.SetOpacity(w, opacity); break; }
                    if (p.HasExited)
                    {
                        // launcher process: the real window belongs to another process
                        var f = GetForegroundWindow();
                        if (f != oldForeground) WindowTools.SetOpacity(f, opacity);
                        break;
                    }
                    Thread.Sleep(i == 0 ? 100 : 500);
                }
            }
            finally { p.Dispose(); }
        }) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        work.CompleteAdding();
        executor.Join(1000);
    }
}

public static class HotkeyExtensions
{
    /// <summary>Command, keys or macro sent to the active window only.</summary>
    public static bool IsLocalCommand(this Hotkey hk) => hk.Command is 73 or 74 or 94;
}
