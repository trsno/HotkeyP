// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Windows;
using HotkeyP.Engine;
using HotkeyP.Model;

namespace HotkeyP.App;

public partial class App : Application
{
    static readonly string InstanceName = "HotkeyP-5-single-instance" + (AppSettings.DataOverride is string d ? "-" + d.Replace('\\', '_').Replace(':', '_') : "");
    Mutex? instanceMutex;
    EventWaitHandle? showEvent;
    AppController? controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception);
            MessageBox.Show(ex.Exception.Message, "HotkeyP", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // one instance: a second start shows the window of the running one
        instanceMutex = new Mutex(true, InstanceName, out bool first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-show");
        if (!first)
        {
            showEvent.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => controller?.ShowMainWindow());
        }) { IsBackground = true }.Start();

        bool hidden = e.Args.Any(a => a.Equals("-hide", StringComparison.OrdinalIgnoreCase) || a.Equals("/hide", StringComparison.OrdinalIgnoreCase));
        string? file = e.Args.FirstOrDefault(a => a.EndsWith(".htk", StringComparison.OrdinalIgnoreCase));
        controller = new AppController(this);
        controller.Start(hidden, file);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
