using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Frostbound.Core;
using Frostbound.UI;
using static Frostbound.UI.Elements;

namespace Frostbound;

public partial class MainWindow
{
    private FrameworkElement? compactPage;
    private TextBlock? compactStatus, compactDetail;
    private Ellipse? compactDot;
    private Button? compactApply, compactEdit;
    private SearchChoicePicker<Loadout?>? compactLoadout;
    private InterfaceStyle? displayedStyle;
    private Rect? fullBounds, compactBounds;

    private void InterfaceSwitchClick(object sender, RoutedEventArgs e) => SetInterfaceStyle(
        Runtime.Settings.InterfaceStyle == InterfaceStyle.Full ? InterfaceStyle.Compact : InterfaceStyle.Full);

    public void SetInterfaceStyle(InterfaceStyle style)
    {
        SwitchInterfaceStyle(style);
    }

    private void ApplyInterfaceLayout()
    {
        bool compact = Runtime.Settings.InterfaceStyle == InterfaceStyle.Compact;
        InterfaceSwitch.Content = compact ? T("完整界面", "Full UI", "通常表示") : T("精简界面", "Compact UI", "コンパクト");
        InterfaceSwitch.ToolTip = T("切换界面，任务与套装选择会保留。", "Switch layouts while keeping your selections.", "選択を保持して表示を切り替えます。");
        Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PageScroll.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactViewport.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(compact ? 0 : 212);
        TitleSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FooterBrand.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FooterLayout.Margin = compact ? new Thickness(20, 0, 20, 0) : new Thickness(31, 0, 27, 0);
        PageHost.Margin = compact ? new Thickness(20, 20, 15, 18) : new Thickness(31, 28, 26, 22);
        ToastBar.Margin = compact ? new Thickness(18, 0, 18, 12) : new Thickness(36, 0, 34, 19);

        if (displayedStyle == Runtime.Settings.InterfaceStyle) return;
        bool wasMaximized = WindowState == WindowState.Maximized;
        if (displayedStyle != null && !App.IsRendering) {
            var bounds = wasMaximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)) {
                if (displayedStyle == InterfaceStyle.Full) fullBounds = bounds; else compactBounds = bounds;
            }
        }
        if (wasMaximized) WindowState = WindowState.Normal;
        Rect workArea = App.IsRendering ? new Rect(0, 0, 1920, 1080) : SystemParameters.WorkArea;
        Rect? saved = compact ? compactBounds : fullBounds;
        double minWidth = Math.Min(compact ? 440 : 1000, workArea.Width - 32), minHeight = Math.Min(compact ? 620 : 740, workArea.Height - 32);
        double width = Math.Clamp(saved?.Width ?? (compact ? 480 : 1210), minWidth, Math.Max(minWidth, workArea.Width - 32));
        double height = Math.Clamp(saved?.Height ?? (compact ? 640 : 850), minHeight, Math.Max(minHeight, workArea.Height - 32));
        double left = Left, top = Top;
        if (!App.IsRendering && displayedStyle != null) {
            double x = saved?.X ?? Left + (Width - width) / 2;
            double y = saved?.Y ?? Top;
            left = Math.Clamp(double.IsFinite(x) ? x : workArea.Left + 16, workArea.Left, workArea.Right - width);
            top = Math.Clamp(double.IsFinite(y) ? y : workArea.Top + 16, workArea.Top, workArea.Bottom - height);
        }
        SetWindowBounds(new Rect(double.IsFinite(left) ? left : workArea.Left + 16, double.IsFinite(top) ? top : workArea.Top + 16, width, height), minWidth, minHeight);
        if (compact) CompactHost.Width = Math.Max(200, width - 38);
        displayedStyle = Runtime.Settings.InterfaceStyle;
    }

    private SearchChoicePicker<Loadout?> LoadoutSelector()
    {
        var choices = new List<Choice<Loadout?>> { new(null, T("不使用综合套装", "No loadout selected", "セット指定なし")) };
        choices.AddRange(Runtime.Settings.Loadouts.Select(p => new Choice<Loadout?>(p, p.Name)));
        var selectedValue = Runtime.Settings.Loadouts.FirstOrDefault(p => p.Id == Runtime.Settings.SelectedLoadoutId);
        var combo = new SearchChoicePicker<Loadout?>(this, T("搜索综合配置", "Search loadout configurations", "総合設定を検索"), choices.ToArray(), selectedValue);
        combo.SelectionChanged += (_, _) => {
            var selected = combo.SelectedValue as Loadout;
            Runtime.Settings.SelectedLoadoutId = selected?.Id;
            if (selected?.QuestId > 0) {
                Runtime.Settings.SelectedQuestId = selected.QuestId;
                if (taskTarget != null) taskTarget.Text = TargetName();
            }
            Runtime.Save(); UpdateState(Runtime.Engine.Snapshot);
        };
        return combo;
    }

    internal void PreparePreview(InterfaceStyle style, string? language = null, bool foodPage = false, bool shortcutsPage = false, AppearanceTheme theme = AppearanceTheme.Dark)
    {
        if (!App.IsRendering) return;
        Runtime.Settings.InterfaceStyle = style;
        Runtime.Settings.Theme = theme; ThemeManager.SetMode(theme);
        if (language is "zh" or "en" or "ja") Runtime.Settings.Language = language;
        Rebuild();
        if (foodPage) FoodNav.IsChecked = true;
        if (shortcutsPage) HotkeysNav.IsChecked = true;
    }

    private FrameworkElement CompactView()
    {
        var panel = new StackPanel();
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        var heading = Text(T("快捷狩猎", "Quick hunt", "狩猟クイック操作"), 18, "#DDECF5", FontWeights.SemiBold);
        heading.TextWrapping = TextWrapping.NoWrap; copy.Children.Add(heading);
        var retry = Button(T("检测游戏", "Detect game", "ゲーム検出"), async () => await Runtime.Retry(), ghost: true);
        retry.FontSize = 10; retry.Height = 32; retry.Padding = new Thickness(9, 6, 9, 6); retry.VerticalAlignment = VerticalAlignment.Center;
        var status = new Grid { Margin = new Thickness(0, 5, 0, 0) }; status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); status.ColumnDefinitions.Add(new ColumnDefinition());
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        compactDot = new Ellipse { Width = 6, Height = 6, Fill = Brush("#718FA4"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        compactStatus = Text("", 11, "#B8D5E3", FontWeights.SemiBold); compactStatus.TextWrapping = TextWrapping.NoWrap;
        statusRow.Children.Add(compactDot); statusRow.Children.Add(compactStatus); status.Children.Add(statusRow);
        compactDetail = Text("", 9, "#7D9BB0"); compactDetail.TextWrapping = TextWrapping.NoWrap; compactDetail.TextTrimming = TextTrimming.CharacterEllipsis; compactDetail.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(compactDetail, 2); status.Children.Add(compactDetail); copy.Children.Add(status);
        header.Children.Add(copy); Grid.SetColumn(retry, 2); header.Children.Add(retry);
        var statusCard = Card(header, 12); statusCard.Background = Brush("#192B36"); statusCard.BorderBrush = Brush("#304A5C"); statusCard.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(statusCard);

        Border CompactField(string label, UIElement input) {
            var field = new StackPanel(); var caption = Text(label, 10, "#91A7B9"); caption.Margin = new Thickness(0, 0, 0, 6); field.Children.Add(caption); field.Children.Add(input);
            return new Border { Child = field, Margin = new Thickness(0, 0, 0, 12) };
        }

        taskTarget = Text(TargetName(), 13, "#D5E8F1"); taskTarget.TextWrapping = TextWrapping.NoWrap; taskTarget.TextTrimming = TextTrimming.CharacterEllipsis;
        var target = new Button { Content = taskTarget, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush("#15232F"), BorderBrush = Brush("#3B5364"), Height = 38, Padding = new Thickness(10, 6, 10, 6) };
        taskPicker = target; target.Click += (_, _) => PickQuest(); panel.Children.Add(CompactField(T("重启任务", "Quest to restart", "再開するクエスト"), target));

        var mode = Choices(new[] {
            new Choice<RestartMode>(RestartMode.Stable, T("稳定重启", "Stable restart", "安定リスタート")),
            new(RestartMode.Quick, T("快速重启（推荐）", "Quick restart · default", "高速リスタート・推奨")),
            new(RestartMode.AcceptOnly, T("仅受理", "Accept only", "受注のみ"))
        }, Runtime.Settings.Mode);
        mode.Height = 34; mode.FontSize = 12;
        mode.ToolTip = T("稳定重启会先返回据点；仅受理需要手动出发。", "Stable returns to base first. Accept-only leaves departure to you.", "安定モードは先に帰還。受注のみは手動で出発。");
        mode.SelectionChanged += (_, _) => { if (mode.SelectedValue is RestartMode value) { Runtime.Settings.Mode = value; Runtime.Save(); } };
        var wing = new CheckBox { Content = T("翼龙出发", "Wingdrake", "翼竜開始"), IsChecked = Runtime.Settings.Wingdrake,
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        wing.Click += (_, _) => { Runtime.Settings.Wingdrake = wing.IsChecked == true; Runtime.Save(); };
        panel.Children.Add(new Border { Child = Columns(mode, wing, 2.5, 12), Margin = new Thickness(0, 0, 0, 12) });

        var restart = Button(T("重新开始", "Restart quest", "クエスト再開"), async () => await Runtime.Restart(), primary: true);
        var reset = Button(T("仅重置", "Reset quest", "リセット"), async () => await Runtime.Reset(), ghost: true);
        restart.Height = reset.Height = 40; restart.FontSize = reset.FontSize = 12; restart.Padding = reset.Padding = new Thickness(10, 6, 10, 6);
        gameButtons.Add(restart); gameButtons.Add(reset); var questActions = Columns(restart, reset, 1.3, 10); questActions.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(questActions);

        compactLoadout = homeLoadout = LoadoutSelector();
        compactLoadout.Height = 36;
        compactApply = Button(T("应用", "Apply", "適用"), async () => await Runtime.ApplySelected());
        compactApply.FontSize = 11; compactApply.Height = 36; compactApply.Padding = new Thickness(10, 6, 10, 6); gameButtons.Add(compactApply);
        var configuration = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 6) }; toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = Text(T("综合配置", "Loadout", "総合設定"), 11, "#91A7B9"); caption.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(caption);
        var utilities = new StackPanel { Orientation = Orientation.Horizontal };
        var add = Button(T("＋ 新增套装", "+ New", "＋ 新規"), () => EditLoadout(null), ghost: true); add.ToolTip = T("创建新的任务、配装与猫饭组合", "Create a quest, equipment and food configuration", "クエスト・装備・食事の新しい組み合わせを作成"); utilities.Children.Add(add);
        compactEdit = Button(T("编辑", "Edit", "編集"), () => { if (compactLoadout.SelectedValue is Loadout selected) EditLoadout(selected); }, ghost: true); utilities.Children.Add(compactEdit);
        var food = Button(T("猫饭编辑", "Food", "食事"), OpenFoodEditor, ghost: true); utilities.Children.Add(food);
        foreach (Button utility in utilities.Children) { utility.Height = 26; utility.FontSize = 10; utility.Padding = new Thickness(7, 3, 7, 3); utility.Margin = new Thickness(4, 0, 0, 0); }
        Grid.SetColumn(utilities, 1); toolbar.Children.Add(utilities); configuration.Children.Add(toolbar); configuration.Children.Add(Columns(compactLoadout, compactApply, 3.2, 10)); panel.Children.Add(configuration);
        panel.Children.Add(Card(new HotkeyQuickEditor(this), 12));
        return panel;
    }

    private void UpdateCompactState(GameSnapshot state, string status, bool busy)
    {
        if (compactStatus != null) compactStatus.Text = busy ? T("正在执行操作…", "Working…", "操作中…") : status;
        if (compactDot != null) compactDot.Fill = Brush(state.State == ConnectionState.Ready ? "#96D6CB" : state.State is ConnectionState.Unsupported or ConnectionState.AccessDenied ? "#D5AF78" : "#718FA4");
        if (compactDetail != null) {
            var current = Runtime.Quests.FirstOrDefault(q => q.Id == state.QuestId)?.Name(Runtime.Settings.Language);
            compactDetail.Text = state.Detail.Length > 0 ? state.Detail : state.State == ConnectionState.Ready ?
                PhaseName(state.QuestState) + (current != null ? " · " + current : "") :
                T("启动游戏并载入存档后自动连接。", "Connects after you launch the game and load a save.", "ゲーム起動・セーブ読み込み後に自動接続。");
            compactDetail.ToolTip = compactDetail.Text;
        }
        if (compactApply != null) compactApply.IsEnabled = state.CanAct && !busy && compactLoadout?.SelectedValue is Loadout;
        if (compactEdit != null) compactEdit.IsEnabled = compactLoadout?.SelectedValue is Loadout;
        if (Runtime.Settings.InterfaceStyle == InterfaceStyle.Compact) {
            FooterStatus.Text = busy ? T("正在处理，请稍候…", "Working, please wait…", "処理中…") : state.Detail.Length > 0 ? state.Detail :
                state.State == ConnectionState.Ready ? T("游戏已连接", "Game connected", "ゲーム接続済み") : T("自动检测已开启", "Automatic detection is active", "自動検出中");
        }
    }
}
