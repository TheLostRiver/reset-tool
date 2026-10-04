using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Frostbound.Core;
using static Frostbound.UI.Elements;

namespace Frostbound.UI;

internal sealed class SearchChoicePicker<T> : Button
{
    private readonly MainWindow owner;
    private readonly Choice<T>[] choices;
    private readonly string title;
    private readonly Func<Choice<T>, string> searchText;
    private readonly TextBlock label;
    public T SelectedValue { get; private set; }
    public event EventHandler? SelectionChanged;

    public SearchChoicePicker(MainWindow owner, string title, Choice<T>[] choices, T value, Func<Choice<T>, string>? searchText = null)
    {
        this.owner = owner; this.title = title; this.choices = choices; this.searchText = searchText ?? (choice => choice.Label);
        SelectedValue = value;
        Style = (Style)Application.Current.FindResource(typeof(Button));
        Background = Brush("#111B25"); BorderBrush = Brush("#2B3947"); Height = 42; Padding = new Thickness(12, 8, 12, 8);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        label = Text(choices.FirstOrDefault(choice => Equals(choice.Value, value))?.Label ?? owner.T("点击搜索选择…", "Click to search…", "検索して選択…"), 12);
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(label);
        var icon = Icon("search", "#7DA3B8", 15); Grid.SetColumn(icon, 1); content.Children.Add(icon); Content = content;
        ToolTip = owner.T("点击按名称或编号搜索", "Click to search by name or ID", "名前・ID で検索");
        Click += (_, _) => OpenPicker();
    }

    private void OpenPicker()
    {
        var dialog = new SearchChoiceDialog<T>(owner, title, choices, SelectedValue, searchText);
        bool wasSuspended = owner.Runtime.Hotkeys.Suspended; owner.Runtime.Hotkeys.Suspended = true;
        try {
            if (dialog.ShowDialog() == true && dialog.Selected != null) {
                SelectedValue = dialog.Selected.Value; label.Text = dialog.Selected.Label;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        } finally { owner.Runtime.Hotkeys.Suspended = wasSuspended; }
    }
}

internal sealed class SearchChoiceDialog<T> : StudioDialog
{
    public Choice<T>? Selected { get; private set; }
    public SearchChoiceDialog(MainWindow owner, string title, Choice<T>[] choices, T selected, Func<Choice<T>, string> searchText)
        : base(owner, title, 580, 565)
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        var input = Input(T("搜索名称或编号…", "Search names or IDs…", "名前・ID を検索…"), out var search); panel.Children.Add(input);
        var count = Text("", 10, "#7998AE"); count.Margin = new Thickness(0, 10, 0, 8); Grid.SetRow(count, 1); panel.Children.Add(count);
        var list = new ListBox { DisplayMemberPath = "Label" }; Grid.SetRow(list, 2); panel.Children.Add(list);
        void Filter() {
            string query = search.Text.Trim();
            var filtered = choices.Where(choice => query.Length == 0 || searchText(choice).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = filtered;
            list.SelectedItem = filtered.FirstOrDefault(choice => Equals(choice.Value, selected)) ?? filtered.FirstOrDefault();
            count.Text = T($"{filtered.Count} 个结果", $"{filtered.Count} results", $"{filtered.Count} 件");
        }
        void Choose() { if (list.SelectedItem is Choice<T> choice) { Selected = choice; DialogResult = true; } }
        search.TextChanged += (_, _) => Filter(); list.MouseDoubleClick += (_, _) => Choose(); Filter(); Body(panel); Cancel();
        var apply = Button(T("选择", "Select", "選択"), Choose, primary: true); apply.IsDefault = true; Actions.Children.Add(apply);
        Loaded += (_, _) => search.Focus();
    }
}
