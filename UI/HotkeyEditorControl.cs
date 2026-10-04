using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Frostbound.Core;
using static Frostbound.UI.Elements;

namespace Frostbound.UI;

internal sealed class HotkeyQuickEditor : StackPanel
{
    private readonly MainWindow owner;
    private readonly TextBlock[] values = new TextBlock[4];
    private readonly Button[] bindings = new Button[4];
    private readonly CheckBox enabled;

    public HotkeyQuickEditor(MainWindow owner)
    {
        this.owner = owner;
        string T(string zh, string en, string ja) => owner.T(zh, en, ja);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(T("快捷键设置", "Shortcuts", "キー設定"), 11, "#B6D1E0", FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center; title.TextWrapping = TextWrapping.NoWrap; header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        enabled = new CheckBox { Content = T("启用", "On", "有効"), FontSize = 9, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        enabled.Click += (_, _) => {
            try { var settings = owner.CopyHotkeys(); settings.Enabled = enabled.IsChecked == true; owner.SaveHotkeys(settings); }
            catch (Exception e) { owner.ShowToast(e.Message); Refresh(); }
        }; actions.Children.Add(enabled);
        var edit = Button(T("编辑", "Edit", "編集"), owner.OpenHotkeySettings, ghost: true); edit.Height = 26; edit.FontSize = 10; edit.Padding = new Thickness(7, 3, 7, 3); actions.Children.Add(edit);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); Children.Add(header);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < 4; i++) {
            int index = i;
            var content = new StackPanel();
            var label = Text(owner.HotkeyName(i), 9, "#7797AB"); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(label);
            values[i] = Text("", 12, "#CDE6F0", FontWeights.SemiBold); values[i].FontFamily = new FontFamily("Consolas"); values[i].TextWrapping = TextWrapping.NoWrap; values[i].TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(values[i]);
            bindings[i] = Button("", () => owner.RecordHotkey(index)); bindings[i].Content = content; bindings[i].Height = 46; bindings[i].Padding = new Thickness(9, 4, 9, 4); bindings[i].HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(bindings[i], i / 2 * 2); Grid.SetColumn(bindings[i], i % 2 * 2); grid.Children.Add(bindings[i]);
        }
        Children.Add(grid); Refresh();
        Loaded += (_, _) => { owner.HotkeysChanged += Refresh; Refresh(); };
        Unloaded += (_, _) => owner.HotkeysChanged -= Refresh;
    }

    private void Refresh()
    {
        var settings = owner.Runtime.Settings.Hotkeys;
        string[] current = [settings.RestartKeyboard, settings.RestartController, settings.ResetKeyboard, settings.ResetController];
        enabled.IsChecked = settings.Enabled;
        for (int i = 0; i < 4; i++) {
            values[i].Text = current[i].Length > 0 ? current[i] : owner.T("点击录入", "Click to record", "クリックで記録");
            bindings[i].ToolTip = owner.HotkeyName(i) + " · " + (current[i].Length > 0 ? current[i] : owner.T("未绑定", "Unbound", "未設定")) + "\n" + owner.T("点击录入并自动保存；在录入窗口可清除绑定。", "Click to record and save. Clear a binding in the capture window.", "クリックで記録・保存。記録画面で解除できます。");
        }
    }
}

internal sealed class HotkeyEditorControl : StackPanel
{
    private readonly MainWindow owner;
    private readonly TextBox[] inputs = new TextBox[4];
    private readonly CheckBox enabled, foreground;
    private bool editing, previouslySuspended;

    public HotkeyEditorControl(MainWindow owner)
    {
        this.owner = owner;
        string T(string zh, string en, string ja) => owner.T(zh, en, ja);
        var settings = owner.Runtime.Settings.Hotkeys;
        string[] current = [settings.RestartKeyboard, settings.RestartController, settings.ResetKeyboard, settings.ResetController];
        for (int i = 0; i < 4; i++) {
            int index = i; bool controller = i % 2 == 1;
            var input = new TextBox { Text = current[i], FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 38, Padding = new Thickness(10, 6, 10, 6) }; inputs[i] = input;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            var label = Text(owner.HotkeyName(i), 11, "#AFCADB"); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label); Grid.SetColumn(input, 1); row.Children.Add(input);
            var record = Button(T("录入", "Record", "記録"), () => { var binding = owner.CaptureHotkey(controller, owner.HotkeyName(index)); if (binding != null) input.Text = binding; }); record.FontSize = 11; record.Padding = new Thickness(8, 6, 8, 6); record.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(record, 2); row.Children.Add(record);
            var clear = Button(T("清除", "Clear", "解除"), () => input.Clear(), ghost: true); clear.FontSize = 11; clear.Padding = new Thickness(8, 6, 8, 6); clear.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(clear, 3); row.Children.Add(clear); Children.Add(row);
        }
        enabled = new CheckBox { Content = T("启用快捷键", "Enable shortcuts", "ショートカットを有効化"), IsChecked = settings.Enabled, FontSize = 11, Margin = new Thickness(0, 5, 0, 12) };
        foreground = new CheckBox { Content = T("只在游戏位于前台时触发", "Only trigger while the game is in the foreground", "ゲームが前面のときだけ実行"), IsChecked = settings.ForegroundOnly, FontSize = 11 };
        Children.Add(enabled); Children.Add(foreground);
        var hint = Text(T("可直接输入组合键，也可点击录入。留空表示不绑定。", "Type a chord or click Record. Leave a field empty to unbind it.", "直接入力・記録のどちらにも対応。空欄にすると解除。"), 10, "#708EA4"); hint.Margin = new Thickness(0, 18, 0, 0); Children.Add(hint);
        GotKeyboardFocus += (_, e) => {
            if (inputs.Contains(e.NewFocus) && !editing) { previouslySuspended = owner.Runtime.Hotkeys.Suspended; owner.Runtime.Hotkeys.Suspended = true; editing = true; }
        };
        LostKeyboardFocus += (_, e) => { if (!inputs.Contains(e.NewFocus)) Resume(); };
        Unloaded += (_, _) => Resume();
    }

    private void Resume() { if (editing) { owner.Runtime.Hotkeys.Suspended = previouslySuspended; editing = false; } }

    public HotkeySettings Read() => new() {
        RestartKeyboard = inputs[0].Text.Trim(), RestartController = inputs[1].Text.Trim(), ResetKeyboard = inputs[2].Text.Trim(), ResetController = inputs[3].Text.Trim(),
        Enabled = enabled.IsChecked == true, ForegroundOnly = foreground.IsChecked == true
    };
}

internal sealed class HotkeySettingsDialog : StudioDialog
{
    public HotkeySettingsDialog(MainWindow owner) : base(owner, owner.T("快捷键设置", "Shortcut settings", "ショートカット設定"), 700, 465)
    {
        var editor = new HotkeyEditorControl(owner); Body(editor, true); Cancel();
        var save = Button(T("保存快捷键", "Save shortcuts", "保存"), () => {
            try { owner.SaveHotkeys(editor.Read()); DialogResult = true; }
            catch (Exception e) { Error.Text = e.Message; }
        }, primary: true); Actions.Children.Add(save);
    }
}
