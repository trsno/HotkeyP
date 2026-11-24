// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotkeyP.App;

/// <summary>Small list at the mouse cursor to choose one item (e.g. which text to paste). Enter or click chooses, Esc cancels.</summary>
sealed class PickListWindow : Window
{
    readonly ListBox list = new() { BorderThickness = new Thickness(0), MaxHeight = 420, MinWidth = 220 };
    int result = -1;

    PickListWindow(string title, IReadOnlyList<string> items)
    {
        Title = title;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        foreach (var s in items) list.Items.Add(new ListBoxItem { Content = s.Length > 120 ? s[..120] + "…" : s, Padding = new Thickness(10, 6, 10, 6) });
        list.SelectedIndex = 0;
        list.MouseUp += (_, e) => { if (list.SelectedIndex >= 0 && e.ChangedButton == MouseButton.Left) Choose(); };
        Content = list;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Deactivated += (_, _) => { if (IsVisible) Close(); };
        Loaded += (_, _) =>
        {
            var p = System.Windows.Forms.Cursor.Position;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            Left = p.X / dpi.DpiScaleX;
            Top = p.Y / dpi.DpiScaleY;
            WindowActivator.Bring(this);
            (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        };
    }

    void Choose()
    {
        result = list.SelectedIndex;
        Close();
    }

    public static int Pick(string title, IReadOnlyList<string> items)
    {
        var w = new PickListWindow(title, items);
        w.ShowDialog();
        return w.result;
    }
}
