// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using HotkeyP.Engine;
using HotkeyP.Model;

namespace HotkeyP.App;

public partial class SettingsWindow : Window
{
    readonly AppController app;

    public SettingsWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        var s = app.Settings;
        StartBox.IsChecked = AppSettings.IsStartWithWindows();
        TrayIconBox.IsChecked = s.ShowTrayIcon;
        CloseToTrayBox.IsChecked = s.CloseToTray;
        MinToTrayBox.IsChecked = s.MinimizeToTray;
        ThemeBox.SelectedIndex = (int)s.Theme;
        HookBox.SelectedIndex = (int)s.Hook;
        DelaySlider.Value = s.MouseDelay;
        VersionText.Text = $"HotkeyP {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";
        Delay_Changed(this, null!);
    }

    void Delay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DelayText != null) DelayText.Text = $"{DelaySlider.Value:0} ms";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = app.Settings;
        s.StartWithWindows = StartBox.IsChecked == true;
        s.ShowTrayIcon = TrayIconBox.IsChecked == true;
        s.CloseToTray = CloseToTrayBox.IsChecked == true;
        s.MinimizeToTray = MinToTrayBox.IsChecked == true;
        s.Theme = (ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex);
        s.Hook = (HookMode)Math.Max(0, HookBox.SelectedIndex);
        s.MouseDelay = (int)DelaySlider.Value;
        app.SaveSettings();
        DialogResult = true;
    }

    void Link_Click(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }

    void Log_Click(object sender, RequestNavigateEventArgs e)
    {
        if (File.Exists(Log.Path)) Process.Start(new ProcessStartInfo(Log.Path) { UseShellExecute = true })?.Dispose();
        else MessageBox.Show(this, "No errors have been logged.", "HotkeyP");
        e.Handled = true;
    }
}
