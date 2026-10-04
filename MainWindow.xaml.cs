using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Frostbound.Core;
using Frostbound.UI;
using static Frostbound.UI.Elements;
using Path = System.IO.Path;
using Forms = System.Windows.Forms;

namespace Frostbound;

public partial class MainWindow : Window
{
    public AppRuntime Runtime { get; }
    public string T(string zh, string en, string ja) => Runtime.Settings.Language switch { "en" => en, "ja" => ja, _ => zh };
    private readonly Dictionary<int, FrameworkElement> pages = [];
    private readonly List<Button> gameButtons = [];
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly Forms.NotifyIcon tray;
    private int currentPage;
    private bool rebuilding, shuttingDown, initialized;
    private TextBlock? heroStatus, heroDetail, phaseValue, pidValue, questValue, saveValue, taskTarget, operationValue;
    private Border? statusBadge;
    private SearchChoicePicker<Loadout?>? homeLoadout;
    private ListBox? questList, loadoutList;
    private TextBox? questSearch;
    private ComboBox? questCategory;
    private TextBlock? questCount, loadoutDescription;
    private int? questPageSelection;

    public MainWindow()
    {
        InitializeComponent();
        Runtime = new(Dispatcher);
        Runtime.StateChanged += UpdateState;
        Runtime.Toast += ShowToast;
        Runtime.Engine.BusyChanged += _ => Dispatcher.BeginInvoke(() => UpdateState(Runtime.Engine.Snapshot));
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); ToastBar.Visibility = Visibility.Collapsed; };
        tray = new Forms.NotifyIcon { Text = "Frostbound · MHWI Reset", Visible = false };
        using var icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
        if (icon != null) tray.Icon = new System.Drawing.Icon(icon);
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreWindow);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 / Open", null, (_, _) => Dispatcher.BeginInvoke(RestoreWindow));
        menu.Items.Add("退出 / Exit", null, (_, _) => Dispatcher.BeginInvoke(async () => await ExitAsync())); tray.ContextMenuStrip = menu;
        initialized = true; ConfigureNavigation(); ApplyInterfaceLayout(); DashboardNav.IsChecked = true;
        if (PageHost.Content == null) Navigate(0);
        UpdateState(Runtime.Engine.Snapshot);
        Loaded += (_, _) => { if (!App.IsRendering) Runtime.Start(); };
        Closing += OnClosing;
    }

    private void ConfigureNavigation()
    {
        BrandTitle.Text = T("霜序", "Frostbound", "霜序"); BrandTitle.FontSize = Runtime.Settings.Language == "en" ? 16 : 23;
        NavHeading.Text = T("工作台", "WORKSPACE", "ワークスペース");
        var controls = new[] { DashboardNav, QuestsNav, LoadoutsNav, FoodNav, HotkeysNav, SettingsNav, LogsNav };
        string[] labels = [T("狩猎控制台", "Hunt dashboard", "狩猟ダッシュボード"), T("任务目录", "Quest library", "クエスト一覧"), T("综合套装", "Loadout studio", "総合セット"), T("猫饭编辑", "Food editor", "食事エディター"), T("快捷键", "Shortcuts", "ショートカット"), T("偏好设置", "Preferences", "設定"), T("运行日志", "Activity log", "実行ログ")];
        string[] icons = ["dashboard", "quests", "loadouts", "food", "hotkeys", "settings", "logs"];
        for (int i = 0; i < controls.Length; i++) {
            var row = new StackPanel { Orientation = Orientation.Horizontal }; var mark = Icon(icons[i]); mark.Margin = new Thickness(0, 0, 13, 0); row.Children.Add(mark);
            var text = Text(labels[i], 12); text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = controls[i] }); row.Children.Add(text); controls[i].Content = row;
        }
    }
    private void NavigationChecked(object sender, RoutedEventArgs e) { if (initialized && !rebuilding) Navigate(int.Parse(((RadioButton)sender).Tag.ToString()!)); }
    private void Navigate(int page)
    {
        if (Runtime.Settings.InterfaceStyle == InterfaceStyle.Compact) {
            PageHost.Content = compactPage ??= CompactView();
            PageScroll.ScrollToTop(); UpdateState(Runtime.Engine.Snapshot); return;
        }
        currentPage = page;
        if (!pages.TryGetValue(page, out var content)) {
            content = page switch { 0 => Dashboard(), 1 => QuestLibrary(), 2 => LoadoutStudio(), 3 => Shortcuts(), 4 => Preferences(), 6 => FoodEditorPage(), _ => ActivityLog() }; pages[page] = content;
        }
        PageHost.Content = content; PageScroll.ScrollToTop(); UpdateState(Runtime.Engine.Snapshot);
    }
    private void Rebuild()
    {
        rebuilding = true; pages.Clear(); compactPage = null; gameButtons.Clear(); ConfigureNavigation(); rebuilding = false;
        Runtime.ReloadQuests(); ApplyInterfaceLayout(); Navigate(currentPage);
    }
    private StackPanel Page(string eyebrow, string title, string description, UIElement? action = null)
    {
        var page = new StackPanel(); var header = new Grid { Margin = new Thickness(0, 0, 0, 23) }; var text = new StackPanel();
        text.Children.Add(Text(eyebrow, 8, "#6E91A6")); var heading = Text(title, 27, "#E2EEF5", FontWeights.SemiBold); heading.Margin = new Thickness(0, 9, 0, 7); text.Children.Add(heading);
        text.Children.Add(Text(description, 11, "#7F99AC")); if (action != null) { text.Margin = new Thickness(0, 0, 150, 0); ((FrameworkElement)action).HorizontalAlignment = HorizontalAlignment.Right; ((FrameworkElement)action).VerticalAlignment = VerticalAlignment.Center; header.Children.Add(action); }
        header.Children.Add(text); page.Children.Add(header); return page;
    }
    private FrameworkElement Dashboard()
    {
        var retry = Button(T("重新检测", "Refresh", "再検出"), async () => await Runtime.Retry(), ghost: true);
        retry.Content = IconLabel("refresh", T("重新检测", "Refresh", "再検出"), "#A5C7D8");
        retry.Padding = new Thickness(12, 8, 12, 8); retry.VerticalAlignment = VerticalAlignment.Center;
        var page = new StackPanel();
        var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var title = Text(T("狩猎控制台", "Hunt dashboard", "狩猟ダッシュボード"), 23, "#E2EEF5", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.NoWrap; title.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(title);
        heroStatus = Text(T("等待猎人归来", "Waiting for your hunter", "ハンターを待っています"), 11, "#A8C9DB"); heroStatus.TextWrapping = TextWrapping.NoWrap;
        statusBadge = Pill(""); statusBadge.Child = heroStatus; statusBadge.Margin = new Thickness(14, 0, 0, 0); heading.Children.Add(statusBadge); copy.Children.Add(heading);
        heroDetail = Text(T("启动游戏并载入存档，工具将自动连接。", "Launch the game and load a save to connect.", "ゲームを起動してセーブデータを読み込んでください。"), 10, "#83A6BC");
        heroDetail.Margin = new Thickness(0, 8, 0, 0); heroDetail.TextWrapping = TextWrapping.NoWrap; heroDetail.TextTrimming = TextTrimming.CharacterEllipsis; copy.Children.Add(heroDetail);
        hero.Children.Add(copy); Grid.SetColumn(retry, 2); hero.Children.Add(retry);
        var heroCard = Card(hero, 18); heroCard.Background = new LinearGradientBrush(Color.FromRgb(27, 48, 63), Color.FromRgb(22, 36, 49), 0); heroCard.BorderBrush = Brush("#365469"); heroCard.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(heroCard);

        var questPanel = new StackPanel(); var questHeading = new Grid(); questHeading.Children.Add(IconLabel("restart", T("任务控制", "Quest control", "クエスト操作"), "#C6DDEB")); var number = Text("01 / QUEST", 8, "#57768D"); number.HorizontalAlignment = HorizontalAlignment.Right; number.VerticalAlignment = VerticalAlignment.Center; questHeading.Children.Add(number); questPanel.Children.Add(questHeading);
        var label = Text(T("重启目标", "TARGET QUEST", "対象クエスト"), 9, "#7D98AC"); label.Margin = new Thickness(0, 16, 0, 8); questPanel.Children.Add(label);
        taskTarget = Text(TargetName(), 13, "#D4E7F2"); taskTarget.TextWrapping = TextWrapping.NoWrap; taskTarget.TextTrimming = TextTrimming.CharacterEllipsis;
        var choose = new Button { Content = taskTarget, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brush("#111C27"), BorderBrush = Brush("#354E61"), Padding = new Thickness(13, 12, 13, 12) };
        choose.Click += (_, _) => PickQuest(); questPanel.Children.Add(choose);
        var modeLabel = Text(T("重启方式", "RESTART MODE", "リスタート方式"), 9, "#7D98AC"); modeLabel.Margin = new Thickness(0, 14, 0, 9); questPanel.Children.Add(modeLabel);
        var modes = new Grid(); for (int i = 0; i < 5; i++) modes.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(8) });
        var buttons = new List<Button>();
        string[] titles = [T("稳定重启", "Stable", "安定"), T("快速重启", "Quick", "高速"), T("仅受理", "Accept only", "受注のみ")];
        string[] hints = [T("返回后再出发", "Return & depart", "帰還して再出発"), T("在当前场景重启", "Restart in place", "その場で再開"), T("手动选择出发", "Depart manually", "手動で出発")];
        void RefreshModes() { for (int i = 0; i < buttons.Count; i++) { bool selected = (int)Runtime.Settings.Mode == i; buttons[i].BorderBrush = Brush(selected ? "#7EBCCF" : "#314452"); buttons[i].Background = Brush(selected ? "#28424F" : "#172630"); } }
        for (int i = 0; i < 3; i++) {
            int index = i; var content = new StackPanel(); content.Children.Add(Text(titles[i], 11, "#C6E3ED", FontWeights.SemiBold)); var hint = Text(hints[i], 8, "#7D9BAE"); hint.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(hint);
            var button = new Button { Content = content, Padding = new Thickness(9, 11, 7, 11), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { Runtime.Settings.Mode = (RestartMode)index; Runtime.Save(); RefreshModes(); }; buttons.Add(button); Grid.SetColumn(button, i * 2); modes.Children.Add(button);
        }
        RefreshModes(); questPanel.Children.Add(modes);
        var wing = new CheckBox { Content = T("翼龙出发", "Wingdrake start", "翼竜で開始"), IsChecked = Runtime.Settings.Wingdrake, FontSize = 10, Margin = new Thickness(0, 16, 0, 16) };
        wing.Click += (_, _) => { Runtime.Settings.Wingdrake = wing.IsChecked == true; Runtime.Save(); }; questPanel.Children.Add(wing);
        var restart = Button(T("重新开始任务", "Restart quest", "クエスト再開") + "   ↗", async () => await Runtime.Restart(), primary: true);
        var reset = Button(T("仅重置 / 跳过结算", "Reset / skip results", "リセット / 結果へ"), async () => await Runtime.Reset(), ghost: true); gameButtons.Add(restart); gameButtons.Add(reset);
        questPanel.Children.Add(Columns(restart, reset, 1.2, 10));
        var keys = Text($"{Runtime.Settings.Hotkeys.RestartKeyboard}  /  {Runtime.Settings.Hotkeys.RestartController}      ·      {Runtime.Settings.Hotkeys.ResetKeyboard}", 9, "#607D93"); keys.Margin = new Thickness(0, 11, 0, 0); questPanel.Children.Add(keys);

        var details = new StackPanel(); details.Children.Add(IconLabel("link", T("连接详情", "Connection", "接続情報"), "#C6DDEB"));
        var divider = new Border { Height = 1, Background = Brush("#2A3E4D"), Margin = new Thickness(0, 19, 0, 8) }; details.Children.Add(divider);
        details.Children.Add(Metric(T("当前阶段", "Game state", "ゲーム状態"), out phaseValue)); details.Children.Add(Metric(T("进程 ID", "Process ID", "プロセス ID"), out pidValue)); details.Children.Add(Metric(T("当前任务", "Current quest", "現在のクエスト"), out questValue)); details.Children.Add(Metric(T("猎人存档", "Hunter save", "セーブデータ"), out saveValue)); details.Children.Add(Metric(T("操作状态", "Operation", "操作状態"), out operationValue));
        var compatible = new Border { Background = Brush("#1E303A"), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 18, 0, 0) };
        var compatibility = new StackPanel(); compatibility.Children.Add(IconLabel("shield", T("版本指纹校验", "Build fingerprint", "バージョン検証"), "#91BCC8")); var compatibleNote = Text(T("匹配当前适配版本后才允许操作。", "Actions unlock after the build matches.", "対応するバージョンでのみ操作できます。"), 9, "#6C91A4"); compatibleNote.Margin = new Thickness(0, 9, 0, 0); compatibility.Children.Add(compatibleNote); compatible.Child = compatibility; details.Children.Add(compatible);
        var row = Columns(Card(questPanel), Card(details), 1.85); row.Margin = new Thickness(0, 0, 0, 17); page.Children.Add(row);

        var quick = new StackPanel(); var quickHeader = Text(T("综合配置 · 每次重启自动应用", "LOADOUT · APPLIED ON EVERY RESTART", "総合設定 · 再開時に自動適用"), 10, "#9EBACC"); quick.Children.Add(quickHeader);
        homeLoadout = LoadoutSelector(); homeLoadout.Margin = new Thickness(0, 11, 0, 0);
        var apply = Button(T("应用套装", "Apply", "適用"), async () => { if (homeLoadout.SelectedValue is Loadout loadout) await Runtime.Apply(loadout, false); else ShowToast(T("请先创建并选择综合套装。", "Create and select a loadout first.", "総合セットを作成・選択してください。")); }); apply.Margin = new Thickness(0, 11, 0, 0); gameButtons.Add(apply); quick.Children.Add(Columns(homeLoadout, apply, 2.5, 10));
        var tip = new StackPanel(); tip.Children.Add(Text(T("让下一场，保持专注。", "Keep your next hunt in focus.", "次の狩猟に、集中を。"), 15, "#CBB990")); var tipText = Text(T("装备、道具、猫饭与任务可以组合保存。\n快捷键支持键盘与 XInput 手柄。", "Save equipment, items, food and quests together.\nKeyboard and XInput controller shortcuts.", "装備・アイテム・食事・クエストを一括保存。\nキーボードと XInput コントローラーに対応。"), 10, "#7F96A7"); tipText.Margin = new Thickness(0, 11, 0, 0); tip.Children.Add(tipText);
        page.Children.Add(Columns(Card(quick, 18), Card(tip, 18), 1.5)); return page;
    }
    private Border Metric(string label, out TextBlock value)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.Children.Add(Text(label, 10, "#718EA3")); value = Text("—", 11, "#C3D8E7"); value.TextAlignment = TextAlignment.Right; Grid.SetColumn(value, 1); grid.Children.Add(value); return new Border { Child = grid, Margin = new Thickness(0, 12, 0, 0) };
    }
    private string TargetName() => Runtime.Settings.SelectedQuestId == 0 ? T("沿用最后受理的任务  ›", "Use the last accepted quest  ›", "最後に受注したクエストを使用  ›") : (Runtime.Quests.FirstOrDefault(q => q.Id == Runtime.Settings.SelectedQuestId)?.Name(Runtime.Settings.Language) ?? Runtime.Settings.SelectedQuestId.ToString()) + "  ›";
    public QuestRow Row(Quest quest) => new(quest, quest.Name(Runtime.Settings.Language), CategoryName(quest.Category) + (Runtime.Settings.Favorites.Contains(quest.Id) ? "  ·  ★" : "") + (quest.Wingdrake ? T("  ·  翼龙", "  ·  Wingdrake", "  ·  翼竜") : ""), quest.Id.ToString("D5"));
    private string CategoryName(string value) => value switch {
        "任务" => T(value, "Assigned", "任務"), "下位上位自由" => T(value, "LR / HR Optional", "下位・上位フリー"), "下位上位活动" => T(value, "LR / HR Event", "下位・上位イベント"), "大师自由" => T(value, "Master Optional", "マスターフリー"), "大师活动" => T(value, "Master Event", "マスターイベント"), "特别任务" => T(value, "Special assignments", "特別任務"), "其他" => T(value, "Other", "その他"), _ => value
    };
    private FrameworkElement QuestLibrary()
    {
        var import = Button(T("导入任务名称", "Import names", "名前をインポート"), ImportQuestNames, ghost: true);
        var page = Page("FIND YOUR NEXT CHALLENGE", T("任务目录", "Quest library", "クエスト一覧"), T("按名称、编号或类别查找，收藏经常挑战的任务。", "Search names, IDs and categories. Pin your regular hunts.", "名前・ID・分類で検索し、お気に入りに登録。"), import);
        var search = Input(T("搜索任务名称或 ID…", "Search quest names or IDs…", "クエスト名・ID を検索…"), out var box); questSearch = box;
        var categories = new List<Choice<string>> { new("", T("全部任务", "All quests", "すべて")), new("★", T("我的收藏", "Favorites", "お気に入り")) };
        categories.AddRange(Runtime.Quests.Select(q => q.Category).Distinct().Select(c => new Choice<string>(c, CategoryName(c)))); questCategory = Choices(categories.ToArray(), "");
        var toolbar = Columns(search, questCategory, 2.5); toolbar.Margin = new Thickness(0, 0, 0, 17); page.Children.Add(toolbar);
        questCount = Text("", 9, "#6D8DA3"); questCount.Margin = new Thickness(4, 0, 0, 10); page.Children.Add(questCount);
        questList = new ListBox { Height = 440, ItemTemplate = (DataTemplate)FindResource("QuestTemplate") };
        questList.MouseDoubleClick += (_, _) => SetSelectedQuest(); questList.SelectionChanged += (_, _) => { if (questList.SelectedItem is QuestRow row) questPageSelection = row.Quest.Id; };
        page.Children.Add(Card(questList, 6));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var select = Button(T("设为重启目标", "Set as target", "対象に設定"), SetSelectedQuest, primary: true); select.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(select);
        var favorite = Button(T("收藏 / 取消收藏", "Toggle favorite", "お気に入り切替"), () => { if (questList.SelectedItem is QuestRow row) { if (!Runtime.Settings.Favorites.Remove(row.Quest.Id)) Runtime.Settings.Favorites.Add(row.Quest.Id); Runtime.Save(); FilterQuests(); } }); favorite.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(favorite);
        actions.Children.Add(Button(T("修改名称", "Rename", "名前を変更"), RenameQuest)); page.Children.Add(actions);
        box.TextChanged += (_, _) => FilterQuests(); questCategory.SelectionChanged += (_, _) => FilterQuests(); FilterQuests(); return page;
    }
    private void FilterQuests()
    {
        if (questList == null) return;
        string search = questSearch?.Text.Trim() ?? "", category = (questCategory?.SelectedItem as Choice<string>)?.Value ?? "";
        var quests = Runtime.Quests.Where(q => (category.Length == 0 || category == "★" && Runtime.Settings.Favorites.Contains(q.Id) || q.Category == category) &&
            (search.Length == 0 || new[] { q.Chinese, q.English, q.Japanese, q.Id.ToString() }.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)))).Select(Row).ToList();
        questList.ItemsSource = quests; questList.SelectedItem = quests.FirstOrDefault(q => q.Quest.Id == questPageSelection);
        if (questCount != null) questCount.Text = T($"{quests.Count} 个任务  /  双击可设为目标", $"{quests.Count} quests  /  Double-click to select", $"{quests.Count} 件  /  ダブルクリックで選択");
    }
    private void SetSelectedQuest()
    {
        if (questList?.SelectedItem is not QuestRow row) { ShowToast(T("先选择一个任务。", "Select a quest first.", "クエストを選択してください。")); return; }
        Runtime.Settings.SelectedQuestId = row.Quest.Id; Runtime.Save(); if (taskTarget != null) taskTarget.Text = TargetName(); ShowToast(T("已设为重启目标。", "Target quest updated.", "対象を設定しました。"));
    }
    private void PickQuest()
    {
        var dialog = new QuestPickerDialog(this); Runtime.Hotkeys.Suspended = true;
        try { if (dialog.ShowDialog() == true) { Runtime.Settings.SelectedQuestId = dialog.SelectedId; Runtime.Save(); if (taskTarget != null) taskTarget.Text = TargetName(); } } finally { Runtime.Hotkeys.Suspended = false; }
    }
    private void RenameQuest()
    {
        if (questList?.SelectedItem is not QuestRow row) return;
        var dialog = new TextEntryDialog(this, T("修改任务名称", "Rename quest", "クエスト名を変更"), row.Quest.Name(Runtime.Settings.Language));
        Runtime.Hotkeys.Suspended = true;
        try { if (dialog.ShowDialog() == true) {
            var custom = JsonSerializer.Deserialize<Quest>(JsonSerializer.Serialize(row.Quest))!;
            if (Runtime.Settings.Language == "en") custom.English = dialog.Value; else if (Runtime.Settings.Language == "ja") custom.Japanese = dialog.Value; else custom.Chinese = dialog.Value;
            Runtime.Settings.CustomQuests.RemoveAll(q => q.Id == custom.Id); Runtime.Settings.CustomQuests.Add(custom); Runtime.Save(); Runtime.ReloadQuests(); FilterQuests(); if (taskTarget != null) taskTarget.Text = TargetName();
        } } finally { Runtime.Hotkeys.Suspended = false; }
    }
    private void ImportQuestNames()
    {
        var dialog = new OpenFileDialog { Filter = "Task names (*.txt)|*.txt", Title = T("导入任务名称", "Import quest names", "クエスト名をインポート") };
        if (dialog.ShowDialog(this) != true) return;
        try {
            var quests = SettingsStore.ParseQuestNames(SettingsStore.ReadLegacyText(dialog.FileName)); if (quests.Count == 0) throw new FormatException("没有找到 ID:任务名称 格式的有效任务。");
            foreach (var quest in quests) { Runtime.Settings.CustomQuests.RemoveAll(x => x.Id == quest.Id); Runtime.Settings.CustomQuests.Add(quest); }
            Runtime.Save(); Rebuild(); ShowToast(T($"已导入 {quests.Count} 个任务名称。", $"Imported {quests.Count} names.", $"{quests.Count} 件インポートしました。"));
        } catch (Exception e) { ShowToast(e.Message); }
    }
    private FrameworkElement LoadoutStudio()
    {
        var add = Button(T("＋ 新建套装", "+ New loadout", "＋ 新規セット"), () => EditLoadout(null), primary: true);
        var page = Page("ONE SET. EVERYTHING READY.", T("综合套装", "Loadout studio", "総合セット"), T("自由组合任务、配装、道具与猫饭；选中配置后，每次重启自动应用。", "Combine quests, gear, items and food. Your selection applies on every restart.", "クエスト・装備・アイテム・食事を自由に組み合わせ、再開時に自動適用。"), add);
        loadoutList = new ListBox { Height = 375, ItemTemplate = (DataTemplate)FindResource("LoadoutTemplate") };
        loadoutList.MouseDoubleClick += (_, _) => { if (loadoutList.SelectedItem is LoadoutRow row) EditLoadout(row.Loadout); };
        var list = Runtime.Settings.Loadouts.Select(p => new LoadoutRow(p, p.Name,
            T("装备", "Equipment", "装備") + ": " + (p.EquipmentName.Length > 0 ? p.EquipmentName : p.EquipmentSlot.ToString()) + "    ·    " + T("道具", "Items", "アイテム") + ": " + (p.ItemName.Length > 0 ? p.ItemName : p.ItemSlot.ToString()) + "    ·    " + (p.QuestId > 0 ? (Runtime.Quests.FirstOrDefault(q => q.Id == p.QuestId)?.Name(Runtime.Settings.Language) ?? p.QuestId.ToString()) : T("沿用任务", "Keep quest", "クエストを保持")),
            p.SaveSlot < 0 ? T("通用套装 · 未绑定存档", "Shared preset · not bound to a save", "共通セット · セーブ未指定") : T($"猎人存档 {p.SaveSlot + 1:00} · 已绑定", $"Hunter save {p.SaveSlot + 1:00} · bound", $"セーブ {p.SaveSlot + 1:00} · 指定済み"))).ToList();
        loadoutList.ItemsSource = list; if (list.Count > 0) loadoutList.SelectedIndex = 0;
        var content = new StackPanel();
        if (list.Count == 0) {
            var empty = new StackPanel { Margin = new Thickness(20, 70, 20, 68), HorizontalAlignment = HorizontalAlignment.Center }; var mark = Icon("loadouts", "#547B92", 48); mark.Margin = new Thickness(0, 0, 0, 20); empty.Children.Add(mark);
            var title = Text(T("你的第一套狩猎准备", "Your first hunt preset", "最初の狩猟セット"), 19, "#C7DBE7"); title.TextAlignment = TextAlignment.Center; empty.Children.Add(title);
            var description = Text(T("新建一套，或从偏好设置迁移本地旧配置。", "Create a loadout, or migrate local settings in Preferences.", "新規作成、または設定からローカルの旧設定を移行。"), 11, "#758FA3"); description.Margin = new Thickness(0, 12, 0, 0); description.TextAlignment = TextAlignment.Center; empty.Children.Add(description); content.Children.Add(empty);
        } else content.Children.Add(loadoutList);
        page.Children.Add(Card(content, 7));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 17, 0, 0) };
        var apply = Button(T("应用套装", "Apply loadout", "セットを適用"), async () => { if (loadoutList.SelectedItem is LoadoutRow row) await Runtime.Apply(row.Loadout, false); }, primary: true); gameButtons.Add(apply); apply.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(apply);
        var depart = Button(T("应用并出发", "Apply & depart", "適用して出発"), async () => { if (loadoutList.SelectedItem is LoadoutRow row) await Runtime.Apply(row.Loadout, true); }); gameButtons.Add(depart); depart.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(depart);
        var edit = Button(T("编辑", "Edit", "編集"), () => { if (loadoutList.SelectedItem is LoadoutRow row) EditLoadout(row.Loadout); }); edit.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(edit);
        actions.Children.Add(Button(T("删除", "Delete", "削除"), () => { if (loadoutList.SelectedItem is LoadoutRow row) { Runtime.Settings.Loadouts.Remove(row.Loadout); Runtime.Save(); Rebuild(); } })); page.Children.Add(actions);
        loadoutDescription = Text(T("装备以游戏中的预设套装编号为准；绑定存档可避免给其他猎人应用错误套装。", "Equipment uses in-game loadout slots. Save binding avoids using another hunter's preset.", "装備はゲーム内のマイセット番号を使用。セーブ指定で誤適用を防ぎます。"), 10, "#708EA4"); loadoutDescription.Margin = new Thickness(0, 20, 0, 0); page.Children.Add(loadoutDescription); return page;
    }
    private void EditLoadout(Loadout? original)
    {
        var dialog = new LoadoutEditorDialog(this, original); Runtime.Hotkeys.Suspended = true;
        try { if (dialog.ShowDialog() == true && dialog.Result != null) {
            var index = Runtime.Settings.Loadouts.FindIndex(x => x.Id == dialog.Result.Id);
            if (index >= 0) Runtime.Settings.Loadouts[index] = dialog.Result; else Runtime.Settings.Loadouts.Add(dialog.Result);
            Runtime.Settings.SelectedLoadoutId = dialog.Result.Id; Runtime.Save(); Rebuild();
        } } finally { Runtime.Hotkeys.Suspended = false; }
    }
    private FrameworkElement Shortcuts()
    {
        var page = Page("STAY IN THE HUNT", T("快捷键", "Shortcuts", "ショートカット"), T("键盘与手柄都可以操作，按住按键只触发一次。", "Keyboard and controller support, with one trigger per press.", "キーボードとコントローラーに対応。長押しでも一度だけ実行。"));
        var settings = Runtime.Settings.Hotkeys; var inputs = new List<TextBox>();
        string[] names = [T("任务重启 · 键盘", "Restart · keyboard", "再開 · キーボード"), T("任务重启 · 手柄", "Restart · controller", "再開 · コントローラー"), T("任务重置 · 键盘", "Reset · keyboard", "リセット · キーボード"), T("任务重置 · 手柄", "Reset · controller", "リセット · コントローラー")];
        string[] values = [settings.RestartKeyboard, settings.RestartController, settings.ResetKeyboard, settings.ResetController];
        for (int i = 0; i < 4; i++) {
            bool controller = i % 2 == 1; var input = new TextBox { Text = values[i], FontFamily = new FontFamily("Consolas"), FontSize = 13, MinHeight = 40 }; inputs.Add(input);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            var label = IconLabel(controller ? "controller" : "hotkeys", names[i], "#AFCADB"); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label); Grid.SetColumn(input, 1); row.Children.Add(input);
            var record = Button(T("录制", "Record", "記録"), () => { var dialog = new KeyCaptureDialog(this, controller); Runtime.Hotkeys.Suspended = true; try { if (dialog.ShowDialog() == true) input.Text = dialog.Binding; } finally { Runtime.Hotkeys.Suspended = false; } }); record.Margin = new Thickness(9, 0, 0, 0); Grid.SetColumn(record, 2); row.Children.Add(record);
            var clear = Button(T("清除", "Clear", "解除"), () => input.Text = "", ghost: true); clear.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(clear, 3); row.Children.Add(clear); page.Children.Add(row);
        }
        var options = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; var enabled = new CheckBox { Content = T("启用快捷键", "Enable shortcuts", "ショートカットを有効化"), IsChecked = settings.Enabled, Margin = new Thickness(0, 0, 0, 15) }; var foreground = new CheckBox { Content = T("只在游戏位于前台时触发", "Only trigger while the game is in the foreground", "ゲームが前面のときだけ実行"), IsChecked = settings.ForegroundOnly }; options.Children.Add(enabled); options.Children.Add(foreground); page.Children.Add(options);
        var save = Button(T("保存快捷键", "Save shortcuts", "ショートカットを保存"), () => {
            try {
                for (int i = 0; i < 4; i++) HotkeyBinding.Parse(inputs[i].Text, i % 2 == 1);
                if (inputs[0].Text.Trim().Length > 0 && inputs[0].Text.Equals(inputs[2].Text, StringComparison.OrdinalIgnoreCase) || inputs[1].Text.Trim().Length > 0 && inputs[1].Text.Equals(inputs[3].Text, StringComparison.OrdinalIgnoreCase)) throw new FormatException(T("重启与重置不能使用相同的快捷键。", "Restart and reset need different bindings.", "再開とリセットは別のキーにしてください。"));
                settings.RestartKeyboard = inputs[0].Text.Trim(); settings.RestartController = inputs[1].Text.Trim(); settings.ResetKeyboard = inputs[2].Text.Trim(); settings.ResetController = inputs[3].Text.Trim(); settings.Enabled = enabled.IsChecked == true; settings.ForegroundOnly = foreground.IsChecked == true;
                Runtime.Hotkeys.Reload(); Runtime.Save(); pages.Remove(0); ShowToast(T("快捷键已保存。", "Shortcuts saved.", "保存しました。"));
            } catch (FormatException e) { ShowToast(e.Message); }
        }, primary: true); save.HorizontalAlignment = HorizontalAlignment.Left; save.Margin = new Thickness(0, 25, 0, 25); page.Children.Add(save);
        var help = new StackPanel(); help.Children.Add(Text(T("常用按键写法", "Binding examples", "キー記述の例"), 13, "#B7CFDF")); var helpText = Text("Ctrl+R   ·   Alt+R   ·   Enter   ·   Num+   ·   F8\nLS+RS   ·   LB+RB   ·   LT+Y   ·   Start+Back", 12, "#83A7BE"); helpText.FontFamily = new FontFamily("Consolas"); helpText.Margin = new Thickness(0, 13, 0, 12); help.Children.Add(helpText);
        help.Children.Add(Text(T("LS / RS 为摇杆按下，LT / RT 为扳机。支持 4 个 XInput 手柄，PlayStation 手柄可通过 Steam Input 使用。", "LS / RS are stick clicks; LT / RT are triggers. Supports four XInput controllers, including PlayStation pads through Steam Input.", "LS / RS はスティック押し込み、LT / RT はトリガー。4 台の XInput に対応。PS パッドは Steam Input を使用。"), 10, "#6E8CA2")); page.Children.Add(Card(help)); return page;
    }
    private FrameworkElement Preferences()
    {
        var page = Page("MAKE IT YOUR OWN", T("偏好设置", "Preferences", "設定"), T("所有配置只保存在这台电脑。", "Your settings stay on this computer.", "設定はこの PC に保存されます。"));
        var launch = new StackPanel(); launch.Children.Add(IconLabel("link", T("游戏位置", "Game location", "ゲームの場所"), "#C3DBE9"));
        var path = new TextBox { Text = Runtime.Settings.GamePath, FontSize = 11, Margin = new Thickness(0, 16, 0, 0) };
        var browse = Button(T("浏览…", "Browse…", "参照…"), () => { var dialog = new OpenFileDialog { Filter = "MonsterHunterWorld.exe|MonsterHunterWorld.exe", FileName = "MonsterHunterWorld.exe" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; }); browse.Margin = new Thickness(0, 16, 0, 0); launch.Children.Add(Columns(path, browse, 4, 10));
        var savePath = Button(T("保存位置", "Save path", "保存"), () => { var value = path.Text.Trim().Trim('"'); if (Directory.Exists(value)) value = Path.Combine(value, "MonsterHunterWorld.exe"); if (!File.Exists(value) || !Path.GetFileName(value).Equals("MonsterHunterWorld.exe", StringComparison.OrdinalIgnoreCase)) { ShowToast(T("请选择 MonsterHunterWorld.exe 或所在目录。", "Select MonsterHunterWorld.exe or its folder.", "MonsterHunterWorld.exe またはそのフォルダーを選択。")); return; } Runtime.Settings.GamePath = value; path.Text = value; Runtime.Save(); ShowToast(T("游戏位置已保存。", "Game path saved.", "保存しました。")); }); savePath.Margin = new Thickness(0, 13, 10, 0);
        var launchGame = Button(T("启动游戏", "Launch game", "ゲームを起動"), LaunchGame, primary: true); launchGame.Margin = new Thickness(0, 13, 0, 0); var launchActions = new StackPanel { Orientation = Orientation.Horizontal }; launchActions.Children.Add(savePath); launchActions.Children.Add(launchGame); launch.Children.Add(launchActions); var launchCard = Card(launch); launchCard.Margin = new Thickness(0, 0, 0, 17); page.Children.Add(launchCard);
        var options = new StackPanel(); options.Children.Add(Text(T("界面与操作", "Interface & behavior", "表示と操作"), 14, "#C4DCEA"));
        var language = Choices(new[] { new Choice<string>("zh", "简体中文"), new("en", "English"), new("ja", "日本語") }, Runtime.Settings.Language); var languageField = Field(T("界面语言", "Interface language", "表示言語"), language); languageField.Margin = new Thickness(0, 17, 0, 17); options.Children.Add(languageField);
        var interfaceStyle = Choices(new[] { new Choice<InterfaceStyle>(InterfaceStyle.Full, T("完整界面", "Full interface", "通常表示")), new(InterfaceStyle.Compact, T("精简界面", "Compact interface", "コンパクト表示")) }, Runtime.Settings.InterfaceStyle);
        options.Children.Add(Field(T("界面风格", "Interface style", "表示スタイル"), interfaceStyle, T("也可以点击窗口顶部的切换按钮。", "You can also switch using the title bar button.", "タイトルバーのボタンでも切り替えできます。")));
        interfaceStyle.SelectionChanged += (_, _) => { if (interfaceStyle.SelectedValue is InterfaceStyle style) SetInterfaceStyle(style); };
        var trayOption = new CheckBox { Content = T("关闭窗口时收起到通知区域", "Keep running in the notification area when closed", "閉じると通知領域に格納"), IsChecked = Runtime.Settings.MinimizeToTray, FontSize = 11, Margin = new Thickness(0, 0, 0, 17) }; options.Children.Add(trayOption);
        var fade = new CheckBox { Content = T("缩短重置过程中的淡入淡出", "Shorten fade transitions during resets", "リセット時のフェードを短縮"), IsChecked = Runtime.Settings.FastFade, FontSize = 11, Margin = new Thickness(0, 0, 0, 17) }; options.Children.Add(fade);
        var focus = new CheckBox { Content = T("执行操作时回到游戏窗口", "Focus the game when running an action", "操作時にゲームを前面へ"), IsChecked = Runtime.Settings.FocusGameOnAction, FontSize = 11, Margin = new Thickness(0, 0, 0, 17) }; options.Children.Add(focus);
        focus.Click += (_, _) => { Runtime.Settings.FocusGameOnAction = focus.IsChecked == true; Runtime.Save(); };
        var chat = new CheckBox { Content = T("启用游戏内聊天快捷指令", "Enable in-game chat commands", "ゲーム内チャットコマンドを有効化"), IsChecked = Runtime.Settings.ChatCommands, FontSize = 11 }; options.Children.Add(chat);
        var chatHelp = Text(T("格式：任务名称，综合套装名称\n例如：传说中的黑龙，日常大剑。也可以只发送任务名称。", "Format: quest name,loadout name\nExample: Fade to Black,My Greatsword. A quest name alone also works.", "形式：クエスト名、セット名\n例：伝説の黒龍、大剣セット。クエスト名のみでも使用可能。"), 10, "#6B8AA0"); chatHelp.Margin = new Thickness(27, 10, 0, 0); options.Children.Add(chatHelp);
        trayOption.Click += (_, _) => { Runtime.Settings.MinimizeToTray = trayOption.IsChecked == true; Runtime.Save(); }; fade.Click += (_, _) => { Runtime.Settings.FastFade = fade.IsChecked == true; Runtime.Save(); }; chat.Click += (_, _) => { Runtime.Settings.ChatCommands = chat.IsChecked == true; Runtime.Save(); }; language.SelectionChanged += (_, _) => { if (language.SelectedValue is string value && value != Runtime.Settings.Language) { Runtime.Settings.Language = value; Runtime.Save(); Rebuild(); } };
        var optionsCard = Card(options); optionsCard.Margin = new Thickness(0, 0, 0, 17); page.Children.Add(optionsCard);
        var files = new StackPanel(); files.Children.Add(Text(T("配置迁移与备份", "Settings & migration", "設定の移行とバックアップ"), 14, "#C4DCEA"));
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 17, 0, 15) };
        foreach (var button in new[] { Button(T("迁移本地旧配置", "Migrate local settings", "旧設定を移行"), MigrateLegacy), Button(T("导入配置", "Import", "インポート"), ImportSettings), Button(T("导出配置", "Export", "エクスポート"), ExportSettings) }) { button.Margin = new Thickness(0, 0, 10, 0); fileActions.Children.Add(button); }
        files.Children.Add(fileActions); var folder = Button(T("打开配置文件夹  ↗", "Open settings folder  ↗", "設定フォルダーを開く  ↗"), () => OpenFolder(Runtime.Store.DirectoryPath), ghost: true); folder.HorizontalAlignment = HorizontalAlignment.Left; files.Children.Add(folder);
        var footer = Text(Runtime.Store.DirectoryPath, 9, "#5F7E94"); footer.Margin = new Thickness(0, 12, 0, 0); files.Children.Add(footer); page.Children.Add(Card(files)); return page;
    }
    private void MigrateLegacy()
    {
        var dialog = new OpenFolderDialog { Title = T("选择本地旧配置目录", "Select local settings folder", "旧設定フォルダーを選択") }; if (dialog.ShowDialog(this) != true) return;
        try {
            var candidate = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Runtime.Settings, SettingsStore.Json), SettingsStore.Json)!;
            int imported = Runtime.Store.ImportLegacy(dialog.FolderName, candidate);
            HotkeyBinding.Parse(candidate.Hotkeys.RestartKeyboard, false); HotkeyBinding.Parse(candidate.Hotkeys.RestartController, true); HotkeyBinding.Parse(candidate.Hotkeys.ResetKeyboard, false); HotkeyBinding.Parse(candidate.Hotkeys.ResetController, true);
            Runtime.Settings = candidate; Runtime.Hotkeys.Reload(); Runtime.Save(); Rebuild(); ShowToast(T($"迁移完成，新增 {imported} 套综合套装。", $"Migration complete: {imported} new loadouts.", $"移行完了：{imported} セット追加。"));
        } catch (Exception e) { ShowToast(e.Message); }
    }
    private void ImportSettings()
    {
        var dialog = new OpenFileDialog { Filter = "Frostbound settings (*.json)|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try { var settings = Runtime.Store.Import(dialog.FileName); HotkeyBinding.Parse(settings.Hotkeys.RestartKeyboard, false); HotkeyBinding.Parse(settings.Hotkeys.RestartController, true); HotkeyBinding.Parse(settings.Hotkeys.ResetKeyboard, false); HotkeyBinding.Parse(settings.Hotkeys.ResetController, true); Runtime.Settings = settings; Runtime.Hotkeys.Reload(); Runtime.Save(); Rebuild(); ShowToast(T("配置已导入。", "Settings imported.", "インポートしました。")); } catch (Exception e) { ShowToast(e.Message); }
    }
    private void ExportSettings()
    {
        var dialog = new SaveFileDialog { Filter = "Frostbound settings (*.json)|*.json", FileName = "frostbound-settings.json" }; if (dialog.ShowDialog(this) != true) return;
        try { Runtime.Store.Export(Runtime.Settings, dialog.FileName); ShowToast(T("配置已导出。", "Settings exported.", "エクスポートしました。")); } catch (Exception e) { ShowToast(e.Message); }
    }
    private FrameworkElement ActivityLog()
    {
        var export = Button(T("导出日志", "Export log", "ログを出力"), () => {
            var dialog = new SaveFileDialog { Filter = "Log (*.log)|*.log", FileName = $"frostbound-{DateTime.Now:yyyyMMdd-HHmmss}.log" }; if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllLines(dialog.FileName, Runtime.Logs.Reverse().Select(x => x.ToString()), new UTF8Encoding(false)); } catch (Exception e) { ShowToast(e.Message); }
        }, ghost: true);
        var page = Page("EVERY ACTION, ACCOUNTED FOR", T("运行日志", "Activity log", "実行ログ"), T("记录真实连接状态、操作结果与失败原因。", "Connection changes, actions and their results.", "実際の接続状態・操作結果・エラーを記録。"), export);
        var list = new ListBox { Height = 505, ItemsSource = Runtime.Logs, ItemTemplate = (DataTemplate)FindResource("LogTemplate") }; page.Children.Add(Card(list, 5));
        var footer = Text(T("日志按日期保存在本地配置目录中。", "Daily logs are stored with your local settings.", "ログは日付別にローカル設定フォルダーに保存。"), 10, "#68879C"); footer.Margin = new Thickness(0, 17, 0, 0); page.Children.Add(footer); return page;
    }
    private void UpdateState(GameSnapshot state)
    {
        bool ready = state.State == ConnectionState.Ready, busy = Runtime.Engine.Busy;
        string status = state.State switch { ConnectionState.Ready => state.Loading ? T("正在加载", "LOADING", "読み込み中") : T("已连接", "CONNECTED", "接続済み"), ConnectionState.Reading => T("等待载入", "INITIALIZING", "準備中"), ConnectionState.Unsupported => T("版本不匹配", "BUILD MISMATCH", "バージョン不一致"), ConnectionState.AccessDenied => T("权限不足", "ACCESS DENIED", "アクセス不可"), ConnectionState.Faulted => T("连接异常", "CONNECTION ERROR", "接続エラー"), _ => T("等待游戏", "WAITING FOR GAME", "ゲームを待機") };
        SidebarStatus.Text = status; SidebarDot.Fill = Brush(ready ? "#96D6CB" : state.State is ConnectionState.Unsupported or ConnectionState.AccessDenied ? "#D5AF78" : "#718FA4");
        SidebarDetail.Text = ready ? T($"猎人存档 {state.SaveSlot + 1:00}", $"Hunter save {state.SaveSlot + 1:00}", $"セーブ {state.SaveSlot + 1:00}") : T("连接后自动读取猎人存档", "A hunter save is needed to connect", "セーブ読み込み後に接続");
        FooterStatus.Text = busy ? T("正在执行操作，请稍候…", "An operation is in progress…", "操作を実行中…") : state.Detail.Length > 0 ? state.Detail : ready ? $"PID {state.ProcessId}   ·   {state.Build}" : "MonsterHunterWorld.exe  ·  " + status;
        foreach (var button in gameButtons) button.IsEnabled = state.CanAct && !busy;
        if (heroStatus != null) heroStatus.Text = ready ? busy ? T("正在准备下一场狩猎", "Preparing your next hunt", "次の狩猟を準備中") : state.Loading ? T("正在加载", "Loading", "読み込み中") : PhaseName(state.QuestState) : state.State == ConnectionState.Waiting ? T("等待猎人归来", "Waiting for your hunter", "ハンターを待っています") : status;
        if (heroDetail != null) heroDetail.Text = ready ? T($"猎人存档 {state.SaveSlot + 1:00}  ·  {state.Build}", $"Hunter save {state.SaveSlot + 1:00}  ·  {state.Build}", $"セーブ {state.SaveSlot + 1:00}  ·  {state.Build}") : state.Detail.Length > 0 ? state.Detail : T("启动游戏并载入存档，工具将自动连接。", "Launch the game and load a save to connect.", "ゲームを起動してセーブデータを読み込んでください。");
        if (heroDetail != null) heroDetail.ToolTip = heroDetail.Text;
        if (heroStatus != null) heroStatus.Foreground = Brush(ready ? "#A0DECE" : state.State is ConnectionState.Unsupported or ConnectionState.AccessDenied or ConnectionState.Faulted ? "#E0BA85" : "#A8C9DB");
        if (statusBadge != null) {
            statusBadge.Background = Brush(ready ? "#24403F" : "#20323F");
            statusBadge.BorderBrush = Brush(ready ? "#3F6661" : "#304B5D");
        }
        if (phaseValue != null) phaseValue.Text = ready ? PhaseName(state.QuestState) : "—";
        if (pidValue != null) pidValue.Text = state.ProcessId > 0 ? state.ProcessId.ToString() : "—";
        if (questValue != null) questValue.Text = ready && state.QuestId > 0 ? state.QuestId.ToString("D5") : "—";
        if (saveValue != null) saveValue.Text = ready && state.SaveSlot >= 0 ? $"{state.SaveSlot + 1:00}" : "—";
        if (operationValue != null) operationValue.Text = busy ? T("执行中", "In progress", "実行中") : ready ? T("就绪", "Ready", "準備完了") : T("未连接", "Offline", "未接続");
        UpdateCompactState(state, status, busy);
    }
    private string PhaseName(int phase) => phase switch { 1 => T("据点待命", "At base", "拠点で待機"), 2 => T("任务进行中", "Quest in progress", "クエスト進行中"), 4 or 5 => T("任务结算中", "Quest results", "クエスト結果"), 7 => T("正在返回", "Returning", "帰還中"), 13 => T("探索中", "Expedition", "探索中"), _ => T("游戏菜单", "Game menu", "ゲームメニュー") };
    public void ShowToast(string text) { ToastText.Text = text; ToastBar.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
    private void DismissToast(object sender, RoutedEventArgs e) { toastTimer.Stop(); ToastBar.Visibility = Visibility.Collapsed; }
    private void LaunchGame()
    {
        try { var path = Runtime.Settings.GamePath; if (!File.Exists(path)) throw new FileNotFoundException(T("请在偏好设置中选择游戏位置。", "Select your game path in Preferences.", "設定でゲームの場所を選択してください。")); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! }); }
        catch (Exception e) { ShowToast(e.Message); }
    }
    private void OpenFolder(string path) { try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { ShowToast(e.Message); } }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); tray.Visible = false; }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shuttingDown) return; e.Cancel = true;
        if (Runtime.Settings.MinimizeToTray && !App.IsRendering) { tray.Visible = true; Hide(); }
        else await ExitAsync();
    }
    private async Task ExitAsync()
    {
        if (shuttingDown) return; shuttingDown = true; IsEnabled = false;
        try { await Runtime.Stop(); } finally { tray.Dispose(); Runtime.Dispose(); Close(); Application.Current.Shutdown(); }
    }
    public async Task CloseRender() { shuttingDown = true; await Runtime.Stop(); tray.Dispose(); Runtime.Dispose(); }
}
