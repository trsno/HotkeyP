// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HotkeyP.App;

/// <summary>
/// "Delay" option (C++ CNTDOWN dialog): shows the command for 5 seconds with only a Cancel button, so a hotkey
/// pressed by mistake can be stopped. There is deliberately no OK button, so typing from habit cannot confirm it.
/// </summary>
static class CountdownDialog
{
    const int Seconds = 5;

    public static bool Run(string title, string detail)
    {
        var bar = new ProgressBar { Height = 6, Maximum = Seconds * 10, Value = Seconds * 10, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 110, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var w = new Window
        {
            Title = "HotkeyP", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = true,
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20), MaxWidth = 460,
                Children =
                {
                    new TextBlock { Text = "This command is about to run. Click Cancel to stop it.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = detail, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) },
                    bar,
                    cancel,
                },
            },
        };
        bool expired = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            bar.Value--;
            if (bar.Value <= 0) { expired = true; timer.Stop(); w.Close(); }
        };
        w.Loaded += (_, _) => { timer.Start(); WindowActivator.Bring(w); };
        w.Closed += (_, _) => timer.Stop();
        w.ShowDialog();
        return expired;
    }
}
