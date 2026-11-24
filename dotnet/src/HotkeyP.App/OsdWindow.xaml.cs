// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HotkeyP.Commands;

namespace HotkeyP.App;

/// <summary>Non-activating, click-through overlay for volume, disk space and "Show text".</summary>
public partial class OsdWindow : Window
{
    readonly DispatcherTimer hideTimer = new();
    string? lastText;

    public OsdWindow()
    {
        InitializeComponent();
        hideTimer.Tick += (_, _) => FadeOut();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            SetWindowLongPtr(h, GWL_EXSTYLE, GetWindowLongPtr(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    public void ShowText(OsdKind kind, string text, double? level, int durationMs = 0)
    {
        // pressing "Show text" or "Disk free space" again while it is shown hides it, like the original
        if (kind != OsdKind.Volume && IsVisible && Opacity > 0 && text == lastText) { FadeOut(); lastText = null; return; }
        lastText = text;
        Glyph.Text = kind switch
        {
            OsdKind.Volume => level is 0 ? "\uE74F" : "\uE767",
            OsdKind.DiskFree => "\uEDA2",
            _ => "\uE8BD",
        };
        if (kind == OsdKind.DiskFree)
        {
            Message.Text = "Free disk space";
            Drives.ItemsSource = DiskFreeOsd.Parse(text);
            Drives.Visibility = Visibility.Visible;
            Bar.Visibility = Visibility.Collapsed;
        }
        else
        {
            Message.Text = text;
            Drives.Visibility = Visibility.Collapsed;
            Bar.Visibility = level.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (level.HasValue) Bar.Value = Math.Clamp(level.Value, 0, 1);
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Show();
        // the drive rows are generated during layout; size the window again once they exist
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();
        PlaceOnActiveScreen();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { UpdateLayout(); PlaceOnActiveScreen(); });
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(durationMs > 0 ? durationMs : kind switch
        {
            OsdKind.Text => 6000,
            OsdKind.DiskFree => 4000,
            _ => 1800,
        });
        hideTimer.Start();
    }

    void PlaceOnActiveScreen()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double left = screen.Left / dpi.DpiScaleX, width = screen.Width / dpi.DpiScaleX;
        double bottom = screen.Bottom / dpi.DpiScaleY;
        Left = left + (width - ActualWidth) / 2;
        Top = bottom - ActualHeight - 64;
    }

    void FadeOut()
    {
        hideTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (_, _) => { if (Opacity == 0) Hide(); };
        BeginAnimation(OpacityProperty, anim);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtrRaw(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtrRaw(IntPtr h, int i, IntPtr v);
    static int GetWindowLongPtr(IntPtr h, int i) => (int)GetWindowLongPtrRaw(h, i);
    static void SetWindowLongPtr(IntPtr h, int i, int v) => SetWindowLongPtrRaw(h, i, v);
}
