using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Frostbound.Core;
using static Frostbound.UI.Elements;

namespace Frostbound.UI;

internal class StudioDialog : Window
{
    protected readonly MainWindow Main;
    protected readonly Grid Layout;
    protected readonly StackPanel Actions;
    protected readonly TextBlock Error;
    protected string T(string zh, string en, string ja) => Main.T(zh, en, ja);
    public StudioDialog(MainWindow owner, string title, double width = 700, double height = 660)
    {
        Main = owner; Owner = owner; Title = title; Width = width; Height = height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = owner.FontFamily; FontSize = 13; Foreground = Brush("#DDE8F0");
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 40);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Layout = new Grid(); Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) }); Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var shell = Card(Layout, 0); shell.CornerRadius = new CornerRadius(12); shell.Background = Brush("#121D27"); shell.BorderBrush = Brush("#426174"); Content = shell;
        var header = new Grid { Margin = new Thickness(23, 0, 18, 0), Background = Brush("#121D27") };
        var caption = Text(title, 18, "#D5EAF5", FontWeights.SemiBold); caption.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(caption);
        var close = Button("×", () => DialogResult = false, ghost: true); close.Width = 32; close.Height = 31; close.Padding = new Thickness(0); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock || ReferenceEquals(e.OriginalSource, header)) DragMove(); }; Layout.Children.Add(header);
        var footer = new Grid { Margin = new Thickness(24, 15, 24, 20) }; footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Error = Text("", 10, "#E0AE91"); Error.VerticalAlignment = VerticalAlignment.Center; Error.Margin = new Thickness(0, 0, 15, 0); footer.Children.Add(Error);
        Actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(Actions, 1); footer.Children.Add(Actions); Grid.SetRow(footer, 2); Layout.Children.Add(footer);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
    protected void Body(UIElement content, bool scroll = false)
    {
        FrameworkElement body = scroll ? new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : (FrameworkElement)content;
        body.Margin = new Thickness(24, 5, 24, 0); Grid.SetRow(body, 1); Layout.Children.Add(body);
    }
    protected void Cancel() { var cancel = Button(T("取消", "Cancel", "キャンセル"), () => DialogResult = false, ghost: true); cancel.Margin = new Thickness(0, 0, 10, 0); Actions.Children.Add(cancel); }
}

internal sealed class QuestPickerDialog : StudioDialog
{
    public int SelectedId { get; private set; }
    public QuestPickerDialog(MainWindow owner) : base(owner, owner.T("选择重启任务", "Select a quest", "クエストを選択"), 680, 630)
    {
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var input = Input(T("搜索名称或 ID…", "Search names or IDs…", "名前・ID を検索…"), out var search); input.Margin = new Thickness(0, 0, 0, 14); panel.Children.Add(input);
        var list = new ListBox { ItemTemplate = (DataTemplate)Application.Current.FindResource("QuestTemplate") }; Grid.SetRow(list, 1); panel.Children.Add(list);
        void Filter() {
            string query = search.Text.Trim();
            var rows = owner.Runtime.Quests.Where(q => query.Length == 0 || new[] { q.Chinese, q.English, q.Japanese, q.Id.ToString() }.Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(q => owner.Runtime.Settings.Favorites.Contains(q.Id)).Select(owner.Row).ToList();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Quest.Id == owner.Runtime.Settings.SelectedQuestId) ?? rows.FirstOrDefault();
        }
        search.TextChanged += (_, _) => Filter(); Filter(); Body(panel); Cancel();
        var last = Button(T("沿用上次任务", "Use last quest", "前回のクエスト"), () => { SelectedId = 0; DialogResult = true; }); last.Margin = new Thickness(0, 0, 10, 0); Actions.Children.Add(last);
        void Choose() { if (list.SelectedItem is QuestRow row) { SelectedId = row.Quest.Id; DialogResult = true; } }
        var select = Button(T("选择任务", "Select quest", "選択"), Choose, primary: true); select.IsDefault = true; Actions.Children.Add(select);
        list.MouseDoubleClick += (_, _) => Choose(); Loaded += (_, _) => search.Focus();
    }
}

internal sealed class TextEntryDialog : StudioDialog
{
    public string Value { get; private set; } = "";
    public TextEntryDialog(MainWindow owner, string title, string initial) : base(owner, title, 520, 260)
    {
        var box = new TextBox { Text = initial, MaxLength = 100, MinHeight = 43 }; Body(Field(T("显示名称", "Display name", "表示名"), box)); Cancel();
        var save = Button(T("保存", "Save", "保存"), () => { if (string.IsNullOrWhiteSpace(box.Text)) { Error.Text = T("名称不能为空。", "A name is required.", "名前を入力してください。"); return; } Value = box.Text.Trim(); DialogResult = true; }, primary: true); save.IsDefault = true; Actions.Children.Add(save); Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}

internal sealed class KeyCaptureDialog : StudioDialog
{
    public string Binding { get; private set; } = "";
    public KeyCaptureDialog(MainWindow owner, bool controller) : base(owner, owner.T("录制快捷键", "Record shortcut", "ショートカットを記録"), 500, 305)
    {
        var panel = new StackPanel(); var icon = Icon(controller ? "controller" : "hotkeys", "#91D8E7", 34); icon.Margin = new Thickness(0, 4, 0, 20); panel.Children.Add(icon);
        var text = Text(T(controller ? "按下组合键，再松开手柄按钮。" : "按下需要绑定的键盘组合键。", controller ? "Press a controller chord, then release it." : "Press the keyboard shortcut you want to use.", controller ? "組み合わせを押してから離してください。" : "登録したいキーの組み合わせを押してください。"), 13, "#BCD7E6"); text.TextAlignment = TextAlignment.Center; panel.Children.Add(text);
        var live = Text(T("等待输入…", "Waiting for input…", "入力待ち…"), 20, "#8FD9E7", FontWeights.SemiBold); live.TextAlignment = TextAlignment.Center; live.Margin = new Thickness(0, 18, 0, 0); panel.Children.Add(live); Body(panel); Cancel();
        if (!controller) PreviewKeyDown += (_, e) => {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape) return;
            Binding = HotkeyBinding.FromKey(key, Keyboard.Modifiers); DialogResult = true; e.Handled = true;
        };
        else {
            string chord = ""; bool armed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            timer.Tick += (_, _) => {
                string value = Controller.CurrentChord();
                if (!armed) { if (value.Length == 0) armed = true; return; }
                if (value.Length > 0) { chord = string.Join("+", chord.Split('+', StringSplitOptions.RemoveEmptyEntries).Concat(value.Split('+')).Distinct()); live.Text = chord; }
                else if (chord.Length > 0) { Binding = chord; timer.Stop(); DialogResult = true; }
            };
            Loaded += (_, _) => timer.Start(); Closed += (_, _) => timer.Stop();
        }
    }
}

internal sealed class LoadoutEditorDialog : StudioDialog
{
    public Loadout? Result { get; private set; }
    public LoadoutEditorDialog(MainWindow owner, Loadout? original) : base(owner, owner.T(original == null ? "新建综合套装" : "编辑综合套装", original == null ? "New loadout" : "Edit loadout", original == null ? "総合セットを作成" : "総合セットを編集"), 760, 790)
    {
        var preset = original == null ? new Loadout() : JsonSerializer.Deserialize<Loadout>(JsonSerializer.Serialize(original, SettingsStore.Json), SettingsStore.Json)!;
        var form = new StackPanel();
        var name = new TextBox { Text = preset.Name, MaxLength = 64 }; form.Children.Add(Field(T("套装名称 · 也用于聊天指令", "Name · also used by chat commands", "セット名 · チャットコマンドにも使用"), name));
        var equipNumber = new TextBox { Text = preset.EquipmentSlot.ToString() }; var itemNumber = new TextBox { Text = preset.ItemSlot.ToString() };
        var equipmentName = new TextBox { Text = preset.EquipmentName }; var itemName = new TextBox { Text = preset.ItemName };
        form.Children.Add(Columns(Field(T("装备预设编号", "Equipment slot", "装備マイセット番号"), equipNumber, T("0 表示按名称查找或不更改；范围 0–224", "0: resolve by name or skip. Range: 0–224", "0 は名前検索・変更なし。範囲：0–224")), Field(T("道具预设编号", "Item slot", "アイテムマイセット番号"), itemNumber, T("0 表示按名称查找或不更改；范围 0–80", "0: resolve by name or skip. Range: 0–80", "0 は名前検索・変更なし。範囲：0–80"))));
        form.Children.Add(Columns(Field(T("装备预设名称（可选）", "Equipment name (optional)", "装備マイセット名（任意）"), equipmentName), Field(T("道具预设名称（可选）", "Item name (optional)", "アイテムマイセット名（任意）"), itemName)));
        var liveSlots = new StackPanel();
        var read = Button(T("从当前猎人读取预设套装", "Read this hunter's loadout slots", "現在のマイセットを読み込む"), () => {
            try {
                var slots = owner.Runtime.Engine.ReadLoadoutSlots(); liveSlots.Children.Clear();
                var equipments = new ComboBox { ItemsSource = slots.Equipment }; var items = new ComboBox { ItemsSource = slots.Items };
                equipments.SelectionChanged += (_, _) => { if (equipments.SelectedItem is GameLoadoutSlot slot) { equipNumber.Text = slot.Number.ToString(); equipmentName.Text = slot.Name; } };
                items.SelectionChanged += (_, _) => { if (items.SelectedItem is GameLoadoutSlot slot) { itemNumber.Text = slot.Number.ToString(); itemName.Text = slot.Name; } };
                liveSlots.Children.Add(Columns(Field(T("游戏内装备套装", "In-game equipment", "装備マイセット"), equipments), Field(T("游戏内道具套装", "In-game items", "アイテムマイセット"), items)));
                Error.Text = T("预设列表已读取。", "Slots loaded.", "読み込みました。");
            } catch (Exception e) { Error.Text = e.Message; }
        }, ghost: true); read.HorizontalAlignment = HorizontalAlignment.Left; read.FontSize = 11; read.Margin = new Thickness(0, 0, 0, 15); form.Children.Add(read); form.Children.Add(liveSlots);
        var quests = new List<Choice<int>> { new(0, T("不指定任务", "Do not select a quest", "クエスト指定なし")) };
        quests.AddRange(owner.Runtime.Quests.Select(q => new Choice<int>(q.Id, $"{q.Id:D5}  ·  {q.Name(owner.Runtime.Settings.Language)}")));
        var quest = Choices(quests.ToArray(), preset.QuestId); form.Children.Add(Field(T("绑定任务", "Quest", "クエスト"), quest));
        var wing = new CheckBox { Content = T("翼龙起始", "Wingdrake start", "翼竜で開始"), IsChecked = preset.Wingdrake, FontSize = 11, Margin = new Thickness(0, 0, 0, 17) }; form.Children.Add(wing);
        var radial = Choices(new[] { new Choice<int>(0, T("遵循游戏设置", "Follow game preference", "ゲーム設定に従う")), new(1, T("同时应用道具转盘", "Apply the item's radial menu", "アイテムのパレットを適用")), new(2, T("保留当前道具转盘", "Keep the current radial menu", "現在のパレットを維持")) }, preset.LinkRadialMenu is null ? 0 : preset.LinkRadialMenu == true ? 1 : 2);
        form.Children.Add(Field(T("道具转盘联动", "Radial menu link", "パレット連動"), radial));
        var foodEnabled = new CheckBox { Content = T("启用猫饭效果", "Enable food effects", "食事効果を有効化"), IsChecked = preset.Food.Enabled, FontSize = 12, Margin = new Thickness(0, 5, 0, 16) }; form.Children.Add(foodEnabled);
        var health = Choices(Enumerable.Range(0, 6).Select(n => new Choice<int>(n, $"+{n * 10} HP")).ToArray(), preset.Food.Health);
        var stamina = Choices(Enumerable.Range(0, 3).Select(n => new Choice<int>(n, $"+{n * 25} Stamina")).ToArray(), preset.Food.Stamina);
        Choice<int>[] Buffs() => [new(0, T("无", "None", "なし")), new(1, T("小", "Small", "小")), new(2, T("中", "Medium", "中")), new(3, T("大", "Large", "大"))];
        var attack = Choices(Buffs(), preset.Food.Attack); var defense = Choices(Buffs(), preset.Food.Defense); var resistance = Choices(Buffs(), preset.Food.Resistance);
        var food = new StackPanel(); food.Children.Add(Three(Field(T("生命值", "Health", "体力"), health), Field(T("耐力", "Stamina", "スタミナ"), stamina), Field(T("攻击力", "Attack", "攻撃力"), attack)));
        food.Children.Add(Columns(Field(T("防御力", "Defense", "防御力"), defense), Field(T("属性耐性", "Element resistance", "属性耐性"), resistance)));
        Choice<int>[] Skills() => owner.Runtime.FoodSkills.Select(skill => new Choice<int>(skill.Id, skill.Name(owner.Runtime.Settings.Language))).ToArray();
        var skill1 = Choices(Skills(), preset.Food.Skill1); var skill2 = Choices(Skills(), preset.Food.Skill2); var skill3 = Choices(Skills(), preset.Food.Skill3);
        food.Children.Add(Three(Field(T("技能 1", "Skill 1", "スキル 1"), skill1), Field(T("技能 2", "Skill 2", "スキル 2"), skill2), Field(T("技能 3", "Skill 3", "スキル 3"), skill3))); form.Children.Add(food);
        food.IsEnabled = foodEnabled.IsChecked == true; foodEnabled.Click += (_, _) => food.IsEnabled = foodEnabled.IsChecked == true;
        var bind = new CheckBox { Content = T("仅用于当前猎人存档", "Bind to the current hunter save", "現在のセーブに指定"), IsChecked = preset.SaveSlot >= 0, FontSize = 11, Margin = new Thickness(0, 7, 0, 12) }; form.Children.Add(bind);
        form.Children.Add(Text(T("装备外观会随下一次场景载入刷新。编号和名称均为空时，保留对应配置。", "Equipment appearance refreshes on the next scene load. An empty name and slot 0 keep the current equipment/items.", "装備の外観は次のシーン読み込みで反映。名前が空で番号 0 の項目は維持。"), 10, "#6E8EA4"));
        Body(form, true); Cancel();
        var save = Button(T("保存套装", "Save loadout", "セットを保存"), () => {
            try {
                if (!int.TryParse(equipNumber.Text, out int equip) || !int.TryParse(itemNumber.Text, out int item)) throw new FormatException(T("套装编号必须是整数。", "Slot numbers must be integers.", "番号は整数で入力してください。"));
                preset.Name = name.Text.Trim(); preset.EquipmentSlot = equip; preset.ItemSlot = item; preset.EquipmentName = equipmentName.Text.Trim(); preset.ItemName = itemName.Text.Trim();
                preset.QuestId = (int)quest.SelectedValue; preset.Wingdrake = wing.IsChecked == true; preset.LinkRadialMenu = (int)radial.SelectedValue switch { 1 => true, 2 => false, _ => null };
                preset.Food = new() { Enabled = foodEnabled.IsChecked == true, Health = (int)health.SelectedValue, Stamina = (int)stamina.SelectedValue, Attack = (int)attack.SelectedValue, Defense = (int)defense.SelectedValue, Resistance = (int)resistance.SelectedValue, Skill1 = (int)skill1.SelectedValue, Skill2 = (int)skill2.SelectedValue, Skill3 = (int)skill3.SelectedValue };
                if (bind.IsChecked == true) {
                    var state = owner.Runtime.Engine.Snapshot;
                    if (state.CanAct) { preset.SaveSlot = state.SaveSlot; preset.UserId = state.UserId; }
                    else if (original?.SaveSlot >= 0) { preset.SaveSlot = original.SaveSlot; preset.UserId = original.UserId; }
                    else throw new InvalidOperationException(T("绑定存档前，请先启动游戏并载入猎人。", "Load a hunter save before binding.", "セーブ指定の前にハンターを読み込んでください。"));
                } else { preset.SaveSlot = -1; preset.UserId = 0; }
                preset.Validate();
                if (owner.Runtime.Settings.Loadouts.Any(p => p.Id != preset.Id && p.Name == preset.Name && p.SaveSlot == preset.SaveSlot && p.UserId == preset.UserId)) throw new InvalidOperationException(T("同一存档的套装名称不能重复。", "Names must be unique within a save.", "同じセーブで同名のセットは使用できません。"));
                Result = preset; DialogResult = true;
            } catch (Exception e) { Error.Text = e.Message; }
        }, primary: true); Actions.Children.Add(save);
    }
}
