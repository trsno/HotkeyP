// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotkeyP.Commands;
using HotkeyP.Model;
using Microsoft.Win32;

namespace HotkeyP.App;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand NewCommand = new(), SearchCommand = new();

    readonly AppController app;
    List<NavItem> navItems = [];
    bool loading;

    public MainWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        var s = app.Settings;
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        if (s.WindowLeft is double l && s.WindowTop is double t && IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        app.Changed += Reload;
        Closed += (_, _) => app.Changed -= Reload;
        Drop += OnDrop;
        Reload();
        Nav.SelectedIndex = 0;
    }

    static bool IsOnScreen(double l, double t) =>
        l >= SystemParameters.VirtualScreenLeft - 50 && t >= SystemParameters.VirtualScreenTop - 50 &&
        l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    protected override void OnClosing(CancelEventArgs e)
    {
        var s = app.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width; s.WindowHeight = Height; s.WindowLeft = Left; s.WindowTop = Top;
        }
        s.Save();
        if (!app.IsExiting && s.CloseToTray && s.ShowTrayIcon)
        {
            // keep running in the notification area
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        if (!app.IsExiting && !(s.CloseToTray && s.ShowTrayIcon)) app.Exit();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && app.Settings.MinimizeToTray && app.Settings.ShowTrayIcon) Hide();
    }

    // ------------------------------------------------------------------ list and navigation

    Hotkey? Selected => (List.SelectedItem as HotkeyRow)?.Hotkey;
    IEnumerable<HotkeyRow> SelectedRows => List.SelectedItems.Cast<HotkeyRow>().OrderBy(r => r.Index);
    NavItem? CurrentNav => Nav.SelectedItem as NavItem;

    void BuildNav()
    {
        var doc = app.Document;
        static bool IsWeb(Hotkey h) => h.Exe.Contains("://") || h.Exe.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        static bool IsProgram(Hotkey h) => !h.IsCommand && h.Exe.Trim().Trim('"').EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var items = new List<NavItem>
        {
            new() { Name = "All hotkeys", Glyph = "\uE8FD" },
            new() { Name = "Filters", IsHeader = true },
            new() { Name = "Keyboard", Glyph = "\uE765", Filter = h => h.VKey is > 0 and < 256 },
            new() { Name = "Mouse", Glyph = "\uE962", Filter = h => h.VKey == Hotkey.VkMouse },
            new() { Name = "Commands", Glyph = "\uE756", Filter = h => h.IsCommand },
            new() { Name = "Programs", Glyph = "\uE7AC", Filter = IsProgram },
            new() { Name = "Documents", Glyph = "\uE8A5", Filter = h => !h.IsCommand && !IsProgram(h) && !IsWeb(h) },
            new() { Name = "Web links", Glyph = "\uE774", Filter = IsWeb },
            new() { Name = "Run at startup", Glyph = "\uE7B5", Filter = h => h.AutoStart },
            new() { Name = "Tray menu", Glyph = "\uE8A7", Filter = h => h.TrayMenu },
            new() { Name = "Categories", IsHeader = true },
        };
        for (int i = 0; i < doc.Categories.Count; i++)
        {
            int c = i;
            items.Add(new NavItem { Name = c == 0 ? "General" : doc.Categories[c], Glyph = "\uE8B7", Category = c, Filter = h => h.Category == c });
        }
        foreach (var n in items) n.Count = n.IsHeader ? 0 : doc.Hotkeys.Count(n.Filter);

        var prev = CurrentNav;
        navItems = items;
        loading = true;
        Nav.ItemsSource = items;
        Nav.SelectedItem = items.FirstOrDefault(n => prev != null && n.Name == prev.Name && n.Category == prev.Category) ?? items[0];
        loading = false;
    }

    void Reload()
    {
        BuildNav();
        RefreshList();
        EnabledToggle.IsChecked = app.HotkeysEnabled;
        FileText.Text = app.FilePath;
        FileText.ToolTip = app.FilePath;
    }

    void RefreshList(Hotkey? select = null)
    {
        var keep = select != null ? [select] : SelectedRows.Select(r => r.Hotkey).ToHashSet();
        var nav = CurrentNav ?? navItems[0];
        var search = SearchBox.Text;
        var rows = app.Document.Hotkeys
            .Select((h, i) => new HotkeyRow { Hotkey = h, Index = i, Icon = FileIcons.For(h) })
            .Where(r => nav.Filter(r.Hotkey) && r.Matches(search))
            .ToList();
        List.ItemsSource = rows;
        foreach (var r in rows.Where(r => keep.Contains(r.Hotkey))) List.SelectedItems.Add(r);
        if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        ViewTitle.Text = nav.Name;
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = app.Document.Hotkeys.Count == 0 ? "No hotkeys yet" : string.IsNullOrWhiteSpace(search) ? "Nothing here" : "No matching hotkeys";
        UpdateToolbar();
    }

    void UpdateToolbar() => Toolbar.IsEnabled = List.SelectedItem != null;

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading) RefreshList();
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateToolbar();

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }

    void Search_Execute(object sender, ExecutedRoutedEventArgs e) => SearchBox.Focus();

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: HotkeyRow }) Edit_Click(sender, e);
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Enter) Edit_Click(sender, e);
        else if (e.Key == Key.Delete) Delete_Click(sender, e);
        else if (mods == ModifierKeys.Alt && e.SystemKey == Key.Up) MoveUp_Click(sender, e);
        else if (mods == ModifierKeys.Alt && e.SystemKey == Key.Down) MoveDown_Click(sender, e);
        else return;
        e.Handled = true;
    }

    // ------------------------------------------------------------------ actions

    void New_Click(object sender, RoutedEventArgs e) => CreateHotkey(new Hotkey { Category = CurrentNav?.Category ?? 0 });

    void CreateHotkey(Hotkey hk)
    {
        if (!HotkeyEditorWindow.Edit(this, app, hk, isNew: true)) return;
        // new hotkeys go after the selected one, like the original "insert after current" option
        int at = List.SelectedItem is HotkeyRow r ? r.Index + 1 : app.Document.Hotkeys.Count;
        app.Document.Insert(at, hk);
        app.Save();
        RefreshList(hk);
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not HotkeyRow row) return;
        var copy = row.Hotkey.Clone();
        if (!HotkeyEditorWindow.Edit(this, app, copy, isNew: false)) return;
        int i = app.Document.Hotkeys.IndexOf(row.Hotkey);
        if (i < 0) return;
        app.Document.Hotkeys[i] = copy;
        app.Save();
        RefreshList(copy);
    }

    void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is Hotkey hk) app.Runner.Post(() => app.Runner.Execute(hk));
    }

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not HotkeyRow row) return;
        // the copy has no shortcut, so it does not clash with the original
        var copy = row.Hotkey.Clone();
        copy.VKey = 0;
        copy.ScanCode = 0;
        copy.Modifiers = Modifiers.None;
        copy.Note = string.IsNullOrEmpty(copy.Note) ? "" : copy.Note + " (copy)";
        app.Document.Insert(row.Index + 1, copy);
        app.Save();
        RefreshList(copy);
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows.ToList();
        if (rows.Count == 0) return;
        string what = rows.Count == 1 ? $"\"{rows[0].Title}\"" : $"{rows.Count} hotkeys";
        if (MessageBox.Show(this, $"Delete {what}?", "HotkeyP", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        foreach (var r in rows.OrderByDescending(r => r.Index)) app.Document.RemoveAt(r.Index);
        app.Save();
    }

    void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    /// <summary>Moves the selected hotkey past its neighbour in the current view (which may be filtered).</summary>
    void MoveSelected(int dir)
    {
        if (List.SelectedItem is not HotkeyRow row || List.ItemsSource is not List<HotkeyRow> rows) return;
        int pos = rows.IndexOf(row) + dir;
        if (pos < 0 || pos >= rows.Count) return;
        app.Document.Move(row.Index, rows[pos].Index);
        app.Save();
        RefreshList(row.Hotkey);
        (List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as ListViewItem)?.Focus();
    }

    void Category_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows.ToList();
        if (rows.Count == 0) return;
        var menu = new ContextMenu { PlacementTarget = CategoryButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        for (int i = 0; i < app.Document.Categories.Count; i++)
        {
            int c = i;
            var mi = new MenuItem
            {
                Header = c == 0 ? "General" : app.Document.Categories[c],
                IsCheckable = true,
                IsChecked = rows.All(r => r.Hotkey.Category == c),
            };
            mi.Click += (_, _) =>
            {
                foreach (var r in rows) r.Hotkey.Category = c;
                app.Save();
            };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "New category", "Name", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        app.Document.Categories.Add(name.Trim());
        app.Save();
        Nav.SelectedItem = navItems.LastOrDefault();
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        // rename or delete a category from the navigation pane
        if (e.OriginalSource is FrameworkElement { DataContext: NavItem { Category: > 0 } item })
        {
            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "Rename" };
            rename.Click += (_, _) =>
            {
                var name = InputDialog.Ask(this, "Rename category", "Name", item.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                app.Document.Categories[item.Category!.Value] = name.Trim();
                app.Save();
            };
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += (_, _) => DeleteCategory(item.Category!.Value);
            menu.Items.Add(rename);
            menu.Items.Add(delete);
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    void DeleteCategory(int c)
    {
        var doc = app.Document;
        int n = doc.Hotkeys.Count(h => h.Category == c);
        if (MessageBox.Show(this, $"Delete category \"{doc.Categories[c]}\"? Its {n} hotkey(s) move to General.", "HotkeyP",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        foreach (var h in doc.Hotkeys)
        {
            if (h.Category == c) h.Category = 0;
            else if (h.Category > c) h.Category--;
        }
        doc.Categories.RemoveAt(c);
        app.Save();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(app) { Owner = this }.ShowDialog();

    void Enabled_Click(object sender, RoutedEventArgs e) => app.HotkeysEnabled = EnabledToggle.IsChecked == true;

    void FileMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        void Add(string header, Action a)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Add("Open hotkeys file…", () =>
        {
            var dlg = new OpenFileDialog { Filter = "Hotkeys (*.htk)|*.htk|All files|*.*", FileName = app.FilePath };
            if (dlg.ShowDialog(this) == true) app.Open(dlg.FileName);
        });
        Add("Save as…", () =>
        {
            var dlg = new SaveFileDialog { Filter = "Hotkeys (*.htk)|*.htk", FileName = Path.GetFileName(app.FilePath), DefaultExt = ".htk" };
            if (dlg.ShowDialog(this) == true) app.SaveAs(dlg.FileName);
        });
        Add("Show in Explorer", () =>
        {
            if (File.Exists(app.FilePath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{app.FilePath}\"")?.Dispose();
        });
        menu.Items.Add(new Separator());
        Add("Exit HotkeyP", app.Exit);
        menu.IsOpen = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        var f = files[0];
        CreateHotkey(new Hotkey
        {
            Exe = f,
            Note = Path.GetFileNameWithoutExtension(f),
            Category = CurrentNav?.Category ?? 0,
        });
    }
}
