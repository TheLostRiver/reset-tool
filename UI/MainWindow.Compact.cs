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
    private Button? compactApply;
    private SearchChoicePicker<Loadout?>? compactLoadout;
    private InterfaceStyle? displayedStyle;
    private Rect? fullBounds, compactBounds;

    private void InterfaceSwitchClick(object sender, RoutedEventArgs e) => SetInterfaceStyle(
        Runtime.Settings.InterfaceStyle == InterfaceStyle.Full ? InterfaceStyle.Compact : InterfaceStyle.Full);

    public void SetInterfaceStyle(InterfaceStyle style)
    {
        if (Runtime.Settings.InterfaceStyle == style && displayedStyle == style) return;
        Runtime.Settings.InterfaceStyle = style;
        Runtime.Save(); Rebuild();
    }

    private void ApplyInterfaceLayout()
    {
        bool compact = Runtime.Settings.InterfaceStyle == InterfaceStyle.Compact;
        InterfaceSwitch.Content = compact ? T("完整界面", "Full UI", "通常表示") : T("精简界面", "Compact UI", "コンパクト");
        InterfaceSwitch.ToolTip = T("切换界面，任务与套装选择会保留。", "Switch layouts while keeping your selections.", "選択を保持して表示を切り替えます。");
        Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
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
        MinWidth = Math.Min(compact ? 440 : 1000, workArea.Width - 32);
        MinHeight = Math.Min(compact ? 540 : 740, workArea.Height - 32);
        Width = Math.Clamp(saved?.Width ?? (compact ? 480 : 1210), MinWidth, Math.Max(MinWidth, workArea.Width - 32));
        Height = Math.Clamp(saved?.Height ?? (compact ? 620 : 850), MinHeight, Math.Max(MinHeight, workArea.Height - 32));
        if (!App.IsRendering && displayedStyle != null) {
            double x = saved?.X ?? Left + (ActualWidth - Width) / 2;
            double y = saved?.Y ?? Top;
            Left = Math.Clamp(double.IsFinite(x) ? x : workArea.Left + 16, workArea.Left, workArea.Right - Width);
            Top = Math.Clamp(double.IsFinite(y) ? y : workArea.Top + 16, workArea.Top, workArea.Bottom - Height);
        }
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

    internal void PreparePreview(InterfaceStyle style, string? language = null, bool foodPage = false)
    {
        if (!App.IsRendering) return;
        Runtime.Settings.InterfaceStyle = style;
        if (language is "zh" or "en" or "ja") Runtime.Settings.Language = language;
        Rebuild();
        if (foodPage) FoodNav.IsChecked = true;
    }

    private FrameworkElement CompactView()
    {
        var panel = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        var heading = Text(T("快捷狩猎", "Quick hunt", "狩猟クイック操作"), 21, "#DDECF5", FontWeights.SemiBold);
        heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading);
        var retry = Button(T("检测游戏", "Detect game", "ゲーム検出"), async () => await Runtime.Retry(), ghost: true);
        retry.FontSize = 11; retry.Padding = new Thickness(12, 8, 12, 8); retry.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(retry); panel.Children.Add(header);

        var status = new StackPanel(); var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        compactDot = new Ellipse { Width = 7, Height = 7, Fill = Brush("#718FA4"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        compactStatus = Text("", 12, "#B8D5E3", FontWeights.SemiBold);
        statusRow.Children.Add(compactDot); statusRow.Children.Add(compactStatus); status.Children.Add(statusRow);
        compactDetail = Text("", 10, "#7D9BB0"); compactDetail.Margin = new Thickness(16, 7, 0, 0); status.Children.Add(compactDetail);
        var statusCard = Card(status, 14); statusCard.Background = Brush("#192B36"); statusCard.BorderBrush = Brush("#304A5C");
        statusCard.Margin = new Thickness(0, 0, 0, 18); panel.Children.Add(statusCard);

        taskTarget = Text(TargetName(), 13, "#D5E8F1"); taskTarget.TextWrapping = TextWrapping.NoWrap; taskTarget.TextTrimming = TextTrimming.CharacterEllipsis;
        var target = new Button { Content = taskTarget, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush("#15232F"), BorderBrush = Brush("#3B5364"), Padding = new Thickness(12, 12, 12, 12) };
        target.Click += (_, _) => PickQuest(); panel.Children.Add(Field(T("重启任务", "Quest to restart", "再開するクエスト"), target));

        var mode = Choices(new[] {
            new Choice<RestartMode>(RestartMode.Stable, T("稳定重启", "Stable restart", "安定リスタート")),
            new(RestartMode.Quick, T("快速重启", "Quick restart", "高速リスタート")),
            new(RestartMode.AcceptOnly, T("仅受理", "Accept only", "受注のみ"))
        }, Runtime.Settings.Mode);
        mode.ToolTip = T("稳定重启会先返回据点；仅受理需要手动出发。", "Stable returns to base first. Accept-only leaves departure to you.", "安定モードは先に帰還。受注のみは手動で出発。");
        mode.SelectionChanged += (_, _) => { if (mode.SelectedValue is RestartMode value) { Runtime.Settings.Mode = value; Runtime.Save(); } };
        var wing = new CheckBox { Content = T("翼龙出发", "Wingdrake", "翼竜開始"), IsChecked = Runtime.Settings.Wingdrake,
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        wing.Click += (_, _) => { Runtime.Settings.Wingdrake = wing.IsChecked == true; Runtime.Save(); };
        panel.Children.Add(Field(T("重启方式", "Restart mode", "再開方式"), Columns(mode, wing, 2.5, 12)));

        var restart = Button(T("重新开始", "Restart quest", "クエスト再開"), async () => await Runtime.Restart(), primary: true);
        var reset = Button(T("仅重置", "Reset quest", "リセット"), async () => await Runtime.Reset(), ghost: true);
        restart.Height = reset.Height = 45; restart.FontSize = reset.FontSize = 13;
        gameButtons.Add(restart); gameButtons.Add(reset); panel.Children.Add(Columns(restart, reset, 1.3, 11));
        string keys = Runtime.Settings.Hotkeys.Enabled ? T("重启：", "Restart: ", "再開：") +
            string.Join(" / ", new[] { Runtime.Settings.Hotkeys.RestartKeyboard, Runtime.Settings.Hotkeys.RestartController }.Where(x => !string.IsNullOrWhiteSpace(x))) +
            "    ·    " + T("重置：", "Reset: ", "リセット：") +
            string.Join(" / ", new[] { Runtime.Settings.Hotkeys.ResetKeyboard, Runtime.Settings.Hotkeys.ResetController }.Where(x => !string.IsNullOrWhiteSpace(x))) : T("快捷键已关闭", "Shortcuts disabled", "ショートカット無効");
        var shortcut = Text(keys, 9, "#6F8FA4"); shortcut.Margin = new Thickness(0, 10, 0, 19); shortcut.TextWrapping = TextWrapping.NoWrap; shortcut.TextTrimming = TextTrimming.CharacterEllipsis; shortcut.ToolTip = keys; panel.Children.Add(shortcut);

        compactLoadout = homeLoadout = LoadoutSelector();
        compactApply = Button(T("应用", "Apply", "適用"), async () => { if (compactLoadout.SelectedValue is Loadout preset) await Runtime.Apply(preset, false); });
        compactApply.FontSize = 11; gameButtons.Add(compactApply);
        panel.Children.Add(Field(T("综合配置 · 重启时自动应用", "Loadout · applied on restart", "総合設定 · 再開時に自動適用"), Columns(compactLoadout, compactApply, 3.2, 10)));
        var utilities = new Grid(); utilities.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); utilities.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var food = Button(T("猫饭编辑", "Food editor", "食事を編集"), OpenFoodEditor, ghost: true); food.Content = IconLabel("food", T("猫饭编辑", "Food editor", "食事を編集"), "#91BCCC"); food.HorizontalAlignment = HorizontalAlignment.Left; food.FontSize = 11; food.Padding = new Thickness(11, 8, 11, 8); utilities.Children.Add(food);
        var studio = Button(T("编辑套装", "Edit loadout", "セットを編集"), () => EditLoadout(compactLoadout.SelectedValue as Loadout), ghost: true);
        studio.HorizontalAlignment = HorizontalAlignment.Right; studio.FontSize = 11; studio.Padding = new Thickness(11, 8, 11, 8); Grid.SetColumn(studio, 1); utilities.Children.Add(studio); panel.Children.Add(utilities);
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
        }
        if (compactApply != null) compactApply.IsEnabled = state.CanAct && !busy && compactLoadout?.SelectedValue is Loadout;
        if (Runtime.Settings.InterfaceStyle == InterfaceStyle.Compact) {
            FooterStatus.Text = busy ? T("正在处理，请稍候…", "Working, please wait…", "処理中…") : state.Detail.Length > 0 ? state.Detail :
                state.State == ConnectionState.Ready ? T("游戏已连接", "Game connected", "ゲーム接続済み") : T("自动检测已开启", "Automatic detection is active", "自動検出中");
        }
    }
}
