// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Windows;
using System.Windows.Controls;

namespace HotkeyP.App;

/// <summary>Asks for one line of text.</summary>
static class InputDialog
{
    public static string? Ask(Window owner, string title, string label, string value)
    {
        var box = new TextBox { Text = value, Padding = new Thickness(8, 5, 8, 5), MinWidth = 320 };
        var ok = new Button { Content = "OK", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        var cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true };
        var w = new Window
        {
            Title = title, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20),
                Children =
                {
                    new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text : null;
    }
}
