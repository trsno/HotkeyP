// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Windows;
using System.Windows.Controls;
using HotkeyP.Commands;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HotkeyP.App;

/// <summary>Notification area icon with a Fluent-styled WPF menu.</summary>
sealed class TrayIcon : IDisposable
{
    readonly AppController app;
    readonly Forms.NotifyIcon icon;
    readonly Drawing.Icon normal, disabled, active;
    readonly System.Windows.Threading.DispatcherTimer highlightTimer;

    public TrayIcon(AppController app)
    {
        this.app = app;
        normal = LoadIcon();
        disabled = Tinted(normal, gray: true);
        active = Tinted(normal, gray: false);
        icon = new Forms.NotifyIcon { Icon = normal, Text = "HotkeyP" };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) app.ShowMainWindow();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        highlightTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        highlightTimer.Tick += (_, _) => { highlightTimer.Stop(); Refresh(); };
        Refresh();
    }

    public bool Visible { get => icon.Visible; set => icon.Visible = value; }

    public void Refresh()
    {
        bool on = app.HotkeysEnabled;
        icon.Icon = on ? normal : disabled;
        icon.Text = on ? "HotkeyP" : "HotkeyP (hotkeys disabled)";
    }

    /// <summary>Flashes the icon briefly when a hotkey runs, as feedback.</summary>
    public void Highlight()
    {
        if (!app.HotkeysEnabled) return;
        icon.Icon = active;
        highlightTimer.Stop();
        highlightTimer.Start();
    }

    void ShowMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string text, string glyph, Action click)
        {
            var mi = new MenuItem { Header = text, Icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.Resources["Icon"] } };
            mi.Click += (_, _) => click();
            return mi;
        }

        menu.Items.Add(Item("Open HotkeyP", "", app.ShowMainWindow));
        var trayHotkeys = app.Document.Hotkeys.Where(h => h.TrayMenu).ToList();
        if (trayHotkeys.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var hk in trayHotkeys)
            {
                var h = hk;
                menu.Items.Add(Item(CommandRunner.DisplayName(h), h.IsCommand ? "" : "", () => app.Runner.Post(() => app.Runner.Execute(h))));
            }
        }
        menu.Items.Add(new Separator());
        var enabled = new MenuItem { Header = "Hotkeys enabled", IsCheckable = true, IsChecked = app.HotkeysEnabled };
        enabled.Click += (_, _) => app.HotkeysEnabled = enabled.IsChecked;
        menu.Items.Add(enabled);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", "", app.Exit));

        // a context menu needs a foreground window of ours to close properly when the user clicks elsewhere
        var host = MenuHost;
        host.Show();
        WindowActivator.Bring(host);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Closed += (_, _) => host.Hide();
        menu.IsOpen = true;
    }

    static Window? menuHost;
    static Window MenuHost => menuHost ??= new Window
    {
        Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = true,
        AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Left = -10000, Top = -10000,
    };

    static Drawing.Icon LoadIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/HotkeyP.ico"));
        using var s = res.Stream;
        return new Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Gray icon when hotkeys are disabled, bright green one as the "hotkey ran" flash.</summary>
    static Drawing.Icon Tinted(Drawing.Icon source, bool gray)
    {
        using var bmp = source.ToBitmap();
        var result = new Drawing.Bitmap(bmp.Width, bmp.Height);
        float[][] m = gray
            ? [[0.3f, 0.3f, 0.3f, 0, 0], [0.59f, 0.59f, 0.59f, 0, 0], [0.11f, 0.11f, 0.11f, 0, 0], [0, 0, 0, 0.8f, 0], [0, 0, 0, 0, 1]]
            : [[0.2f, 0.6f, 0.2f, 0, 0], [0.2f, 0.9f, 0.2f, 0, 0], [0, 0.2f, 0.1f, 0, 0], [0, 0, 0, 1, 0], [0, 0.1f, 0, 0, 1]];
        using (var g = Drawing.Graphics.FromImage(result))
        using (var attr = new Drawing.Imaging.ImageAttributes())
        {
            attr.SetColorMatrix(new Drawing.Imaging.ColorMatrix(m));
            g.DrawImage(bmp, new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, Drawing.GraphicsUnit.Pixel, attr);
        }
        return Drawing.Icon.FromHandle(result.GetHicon());
    }

    /// <summary>Icon for a window or application hidden by a command; clicking it restores it and removes the icon.</summary>
    public static void AddRestoreIcon(string tooltip, IntPtr hIcon, Action restore)
    {
        Drawing.Icon ico;
        try { ico = hIcon != IntPtr.Zero ? (Drawing.Icon)Drawing.Icon.FromHandle(hIcon).Clone() : Drawing.SystemIcons.Application; }
        catch (ArgumentException) { ico = Drawing.SystemIcons.Application; }
        var ni = new Forms.NotifyIcon { Icon = ico, Text = tooltip.Length > 63 ? tooltip[..63] : tooltip, Visible = true };
        ni.MouseUp += (_, _) =>
        {
            ni.Visible = false;
            ni.Dispose();
            restore();
        };
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
