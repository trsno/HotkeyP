// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using HotkeyP.Commands;
using HotkeyP.Engine;
using HotkeyP.Input;
using HotkeyP.Model;
using Microsoft.Win32;
using MouseButton = HotkeyP.Input.MouseButton;

namespace HotkeyP.App;

public partial class HotkeyEditorWindow : Window
{
    readonly AppController app;
    readonly Hotkey hk;
    readonly bool isNew;
    int vkey, scan;
    Modifiers mods;
    bool recording;

    static readonly (string Name, MouseButton? Button)[] MouseChoices =
    [
        ("\u2014", null), ("Left", MouseButton.Left), ("Right", MouseButton.Right), ("Middle", MouseButton.Middle),
        ("X1 (back)", MouseButton.X1), ("X2 (forward)", MouseButton.X2), ("Wheel up", MouseButton.WheelUp),
        ("Wheel down", MouseButton.WheelDown), ("Wheel left", MouseButton.WheelLeft), ("Wheel right", MouseButton.WheelRight),
    ];

    HotkeyEditorWindow(AppController app, Hotkey hk, bool isNew)
    {
        this.app = app;
        this.hk = hk;
        this.isNew = isNew;
        InitializeComponent();
        Title = isNew ? "New hotkey" : "Edit hotkey";
        Heading.Text = Title;

        foreach (var cb in new[] { Mouse1, Mouse2, Mouse3 })
        {
            cb.ItemsSource = MouseChoices.Select(m => m.Name).ToList();
            cb.SelectedIndex = 0;
        }
        var view = new ListCollectionView(CommandCatalog.All.Where(c => !c.Hidden).OrderBy(c => c.Group).ThenBy(c => c.Name).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandInfo.Group), new GroupNameConverter()));
        CommandList.ItemsSource = view;
        CategoryBox.ItemsSource = app.Document.Categories.Select((c, i) => i == 0 ? "General" : c).ToList();

        Load();
        Closed += (_, _) => StopRecording();
        Deactivated += (_, _) => StopRecording();
    }

    /// <summary>Shows the editor; returns true when the user saved (the hotkey object is then updated).</summary>
    public static bool Edit(Window owner, AppController app, Hotkey hk, bool isNew)
        => new HotkeyEditorWindow(app, hk, isNew) { Owner = owner }.ShowDialog() == true;

    void Load()
    {
        NameBox.Text = hk.Note;
        vkey = hk.VKey; scan = hk.ScanCode; mods = hk.Modifiers;
        if (hk.VKey == Hotkey.VkMouse)
        {
            TriggerMouse.IsChecked = true;
            var buttons = MouseButtons.Enumerate(hk.ScanCode).ToList();
            var boxes = new[] { Mouse1, Mouse2, Mouse3 };
            for (int i = 0; i < Math.Min(3, buttons.Count); i++)
                boxes[i].SelectedIndex = Math.Max(0, Array.FindIndex(MouseChoices, m => (int?)m.Button == buttons[i]));
            MouseCtrl.IsChecked = (mods & Modifiers.Control) != 0;
            MouseShift.IsChecked = (mods & Modifiers.Shift) != 0;
            MouseAlt.IsChecked = (mods & Modifiers.Alt) != 0;
            MouseWin.IsChecked = (mods & Modifiers.Win) != 0;
        }
        else if (hk.VKey == 0 && !isNew) TriggerNone.IsChecked = true;
        else TriggerKeyboard.IsChecked = true;
        ShowKeys();

        if (hk.IsCommand)
        {
            ActionCommand.IsChecked = true;
            CommandList.SelectedItem = CommandCatalog.Get(hk.Command);
            CommandList.ScrollIntoView(CommandList.SelectedItem);
            ParamBox.Text = hk.Args.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
        else
        {
            ActionProgram.IsChecked = true;
            ArgsBox.Text = hk.Args;
        }
        ExeBox.Text = hk.Exe;
        DirBox.Text = hk.Dir;
        ShowBox.SelectedIndex = (int)hk.Show;
        PriorityBox.SelectedIndex = (int)hk.Priority;
        OpacitySlider.Value = hk.Opacity == 0 ? 0 : Math.Round(hk.Opacity * 100 / 255.0);
        Opacity_Changed(this, null!);
        AdminBox.IsChecked = hk.Admin;
        MultiBox.IsChecked = hk.MultipleInstances;
        CategoryBox.SelectedIndex = Math.Clamp(hk.Category, 0, app.Document.Categories.Count - 1);
        SoundBox.Text = hk.Sound;
        AskBox.IsChecked = hk.Ask;
        DelayBox.IsChecked = hk.Delay;
        AutoStartBox.IsChecked = hk.AutoStart;
        TrayBox.IsChecked = hk.TrayMenu;
        UpdateCommandSearchHint();
    }

    // ------------------------------------------------------------------ shortcut

    void Trigger_Changed(object sender, RoutedEventArgs e)
    {
        if (KeyboardPanel == null) return;
        KeyboardPanel.Visibility = TriggerKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MousePanel.Visibility = TriggerMouse.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StopRecording();
    }

    void ShowKeys()
    {
        bool hasKey = vkey is > 0 and < 256;
        KeyCaps.ItemsSource = hasKey ? KeyFormatter.ModifierNames(mods).Append(KeyFormatter.KeyName(vkey, scan)).ToList() : null;
        RecordText.Text = recording ? (mods != Modifiers.None ? "" : "Press the keys\u2026")
                        : hasKey ? "" : "Click here, then press the keys";
        if (recording && mods != Modifiers.None && !hasKey)
            KeyCaps.ItemsSource = KeyFormatter.ModifierNames(mods).Append("\u2026").ToList();
        RecordIcon.Text = recording ? "\uE7C8" : "\uE765";
        ClearKeyButton.IsEnabled = hasKey;
        ShowConflict();
    }

    void ShowConflict()
    {
        var clash = TriggerKeyboard.IsChecked == true && vkey is > 0 and < 256
            ? app.Document.Hotkeys.Select((h, i) => (h, i)).FirstOrDefault(x =>
                !ReferenceEquals(x.h, hk) && x.h.VKey == vkey && x.h.Modifiers == mods && (isNew || !SameOriginal(x.h)))
            : default;
        ConflictText.Visibility = clash.h != null ? Visibility.Visible : Visibility.Collapsed;
        if (clash.h != null)
            ConflictText.Text = $"Also used by #{clash.i + 1} \"{CommandRunner.DisplayName(clash.h)}\". Both will run.";
    }

    bool SameOriginal(Hotkey other) => !isNew && other.Note == hk.Note && other.Exe == hk.Exe && other.Command == hk.Command
        && other.VKey == hk.VKey && other.Modifiers == hk.Modifiers && other.Args == hk.Args;

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (recording) { StopRecording(); return; }
        recording = true;
        mods = Modifiers.None;
        vkey = 0;
        app.Engine.Paused = true;
        app.Engine.Capture((vk, sc, m) => Dispatcher.BeginInvoke(() => OnCapturedKey(vk, sc, m)));
        ShowKeys();
    }

    void OnCapturedKey(int vk, int sc, Modifiers m)
    {
        if (!recording) return;
        mods = m;
        bool isModifier = vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
        if (!isModifier)
        {
            vkey = vk;
            scan = sc & 0x1ff0000;
            StopRecording();
        }
        ShowKeys();
    }

    void StopRecording()
    {
        if (!recording) return;
        recording = false;
        app.Engine.Capture(null);
        app.Engine.Paused = false;
        if (vkey == 0)
        {
            // nothing pressed: keep the original shortcut
            vkey = hk.VKey is > 0 and < 256 ? hk.VKey : 0;
            scan = hk.ScanCode;
            mods = hk.Modifiers;
        }
        ShowKeys();
    }

    void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        vkey = 0; scan = 0; mods = Modifiers.None;
        ShowKeys();
    }

    void Mouse_Changed(object sender, SelectionChangedEventArgs e) { }

    int MouseSequence() => MouseButtons.FromButtons(new[] { Mouse1, Mouse2, Mouse3 }
        .Select(cb => MouseChoices[Math.Max(0, cb.SelectedIndex)].Button)
        .Where(b => b.HasValue).Select(b => b!.Value).Distinct());

    // ------------------------------------------------------------------ action

    void Action_Changed(object sender, RoutedEventArgs e)
    {
        if (ProgramPanel == null) return;
        ProgramPanel.Visibility = ActionProgram.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CommandPanel.Visibility = ActionCommand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void CommandSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var text = CommandSearch.Text.Trim();
        if (CommandList.ItemsSource is ListCollectionView view)
            view.Filter = o => text.Length == 0 || o is CommandInfo c &&
                (c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || c.Group.ToString().Contains(text, StringComparison.CurrentCultureIgnoreCase));
        UpdateCommandSearchHint();
        if (CommandList.SelectedItem == null && CommandList.Items.Count > 0) CommandList.SelectedIndex = 0;
    }

    void UpdateCommandSearchHint() => CommandSearchHint.Visibility = CommandSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void CommandSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up && CommandList.Items.Count > 0)
        {
            int i = CommandList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            CommandList.SelectedIndex = Math.Clamp(i, 0, CommandList.Items.Count - 1);
            CommandList.ScrollIntoView(CommandList.SelectedItem);
            e.Handled = true;
        }
    }

    void CommandList_Changed(object sender, SelectionChangedEventArgs e)
    {
        var c = CommandList.SelectedItem as CommandInfo;
        ParamHint.Text = c?.ParamHint is string h ? h : c != null ? "No parameter needed." : "";
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText == null) return;
        OpacityText.Text = OpacitySlider.Value == 0 ? "Unchanged" : $"{OpacitySlider.Value:0} %";
    }

    void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Programs|*.exe;*.com;*.bat;*.cmd;*.lnk|All files|*.*", DereferenceLinks = false };
        if (dlg.ShowDialog(this) != true) return;
        ExeBox.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true) DirBox.Text = dlg.FolderName;
    }

    void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Sounds (*.wav)|*.wav|All files|*.*", InitialDirectory = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Media") };
        if (dlg.ShowDialog(this) == true) SoundBox.Text = dlg.FileName;
    }

    // ------------------------------------------------------------------ save

    /// <summary>Copies the form into <paramref name="target"/>; returns an error message or null.</summary>
    string? Store(Hotkey target)
    {
        target.Note = NameBox.Text.Trim();
        if (TriggerKeyboard.IsChecked == true)
        {
            target.VKey = vkey;
            target.ScanCode = vkey != 0 ? (scan != 0 ? scan : KeyFormatter.InputScan(vkey)) : 0;
            target.Modifiers = vkey != 0 ? mods : Modifiers.None;
        }
        else if (TriggerMouse.IsChecked == true)
        {
            int seq = MouseSequence();
            if (seq == MouseButtons.Empty) return "Choose at least one mouse button.";
            if (seq == MouseButtons.FromButtons([MouseButton.Left]) && MouseCtrl.IsChecked != true && MouseShift.IsChecked != true
                && MouseAlt.IsChecked != true && MouseWin.IsChecked != true)
                return "A plain left click cannot be a hotkey. Add another button or a key.";
            target.VKey = Hotkey.VkMouse;
            target.ScanCode = seq;
            target.Modifiers = (MouseCtrl.IsChecked == true ? Modifiers.Control : 0) | (MouseShift.IsChecked == true ? Modifiers.Shift : 0)
                | (MouseAlt.IsChecked == true ? Modifiers.Alt : 0) | (MouseWin.IsChecked == true ? Modifiers.Win : 0);
        }
        else
        {
            target.VKey = 0; target.ScanCode = 0; target.Modifiers = Modifiers.None;
        }

        if (ActionCommand.IsChecked == true)
        {
            if (CommandList.SelectedItem is not CommandInfo c) return "Choose a command.";
            target.Command = c.Id;
            target.Args = ParamBox.Text.Replace("\r\n", "\n").Trim('\n');
            target.Exe = "";
            if (target.Command is 27 or 26 or 74 or 94)
            {
                try { MacroParser.Parse(target.Command == 27 ? target.Args : WindowTools.SplitWindowParams(target.Args).First); }
                catch (MacroSyntaxException ex) { return ex.Message; }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ExeBox.Text)) return "Enter a program, file or web address.";
            target.Command = -1;
            target.Exe = ExeBox.Text.Trim();
            target.Args = ArgsBox.Text;
        }
        target.Dir = DirBox.Text.Trim();
        target.Show = (ShowMode)Math.Max(0, ShowBox.SelectedIndex);
        target.Priority = (Priority)Math.Max(0, PriorityBox.SelectedIndex);
        target.Opacity = OpacitySlider.Value == 0 ? 0 : (int)Math.Round(OpacitySlider.Value * 255 / 100);
        target.Admin = AdminBox.IsChecked == true;
        target.MultipleInstances = MultiBox.IsChecked == true;
        target.Category = Math.Max(0, CategoryBox.SelectedIndex);
        target.Sound = SoundBox.Text.Trim();
        target.Ask = AskBox.IsChecked == true;
        target.Delay = DelayBox.IsChecked == true;
        target.AutoStart = AutoStartBox.IsChecked == true;
        target.TrayMenu = TrayBox.IsChecked == true;
        return null;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        var error = Store(hk);
        if (error != null)
        {
            MessageBox.Show(this, error, "HotkeyP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    void Test_Click(object sender, RoutedEventArgs e)
    {
        var test = hk.Clone();
        var error = Store(test);
        if (error != null)
        {
            MessageBox.Show(this, error, "HotkeyP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        test.Ask = false;
        app.Runner.Post(() => app.Runner.Execute(test));
    }

    sealed class GroupNameConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value switch
        {
            CommandGroup.HotkeyP => "HotkeyP",
            CommandGroup g => g.ToString(),
            _ => value,
        };
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }
}
