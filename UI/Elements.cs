using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Frostbound.Core;

namespace Frostbound.UI;

internal static class Elements
{
    public static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public static TextBlock Text(string text, double size = 13, string color = "#DDE8F0", FontWeight? weight = null) => new() {
        Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };
    public static Border Card(UIElement content, double padding = 20) => new() {
        Child = content, Background = Brush("#18222D"), BorderBrush = Brush("#2B3947"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(padding)
    };
    public static Border Pill(string text, string color = "#89B8CC") => new() {
        Child = Text(text, 9, color), Background = Brush("#20323F"), BorderBrush = Brush("#304B5D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center
    };
    public static Button Button(string text, Action click, bool primary = false, bool ghost = false) {
        var button = new Button { Content = text };
        if (primary || ghost) button.Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton");
        button.Click += (_, _) => click(); return button;
    }
    public static Border Field(string label, UIElement input, string? hint = null) {
        var panel = new StackPanel(); var caption = Text(label, 11, "#91A7B9"); caption.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(caption); panel.Children.Add(input);
        if (!string.IsNullOrWhiteSpace(hint)) { var sub = Text(hint, 9, "#657F93"); sub.Margin = new Thickness(0, 7, 0, 0); panel.Children.Add(sub); }
        return new Border { Child = panel, Margin = new Thickness(0, 0, 0, 16) };
    }
    public static Grid Columns(UIElement left, UIElement right, double ratio = 1, double gap = 16) {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
    }
    public static Grid Three(UIElement a, UIElement b, UIElement c) {
        var grid = new Grid();
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(12) });
        grid.Children.Add(a); Grid.SetColumn(b, 2); grid.Children.Add(b); Grid.SetColumn(c, 4); grid.Children.Add(c); return grid;
    }
    public static Grid Input(string placeholder, out TextBox box) {
        var grid = new Grid(); box = new TextBox { MinHeight = 42 };
        var hint = Text(placeholder, 12, "#627D92"); hint.Margin = new Thickness(13, 0, 13, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false;
        var input = box; input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(input); grid.Children.Add(hint); return grid;
    }
    public static Viewbox Icon(string name, string color = "#819BAE", double size = 18) {
        string data = name switch {
            "dashboard" => "M3 3H10V10H3ZM14 3H21V10H14ZM3 14H10V21H3ZM14 14H21V21H14Z",
            "quests" => "M5 3H16L20 7V21H5Z M15 3V8H20 M8 12H16 M8 16H14",
            "loadouts" => "M12 2 21 7V17L12 22 3 17V7Z M3 7 12 12 21 7 M12 12V22",
            "food" => "M3 12H21Q20 20 12 20Q4 20 3 12Z M6 23H18 M8 2Q5 5 8 8 M13 2Q10 5 13 8 M18 2Q15 5 18 8",
            "hotkeys" => "M3 6H21V19H3Z M6 10H8 M11 10H13 M16 10H18 M6 14H8 M11 14H13 M16 14H18 M8 17H16",
            "settings" => "M9 3 10 1H14L15 3 19 4 21 8 19 11V13L21 16 19 20 15 21 14 23H10L9 21 5 20 3 16 5 13V11L3 8 5 4Z M16 12A4 4 0 1 1 8 12A4 4 0 1 1 16 12",
            "logs" => "M4 3H20V21H4Z M8 7H16 M8 11H16 M8 15H13 M8 18H11",
            "refresh" => "M21 10A9 9 0 1 0 19 18 M21 3V10H14",
            "restart" => "M19 5A9 9 0 1 0 21 14 M19 1V7H13 M10 8 16 12 10 16Z",
            "reset" => "M19 5A9 9 0 1 0 21 14 M19 1V7H13 M8 12H16",
            "search" => "M17 10A7 7 0 1 1 3 10A7 7 0 1 1 17 10 M15 15 22 22",
            "star" => "M12 2 15 8 22 9 17 14 18 21 12 18 6 21 7 14 2 9 9 8Z",
            "controller" => "M7 7H17Q21 7 22 12L23 18Q22 22 19 20L15 16H9L5 20Q2 22 1 18L2 12Q3 7 7 7Z M5 10V14 M3 12H7 M17 10V11 M20 13V14",
            "arrow" => "M3 12H21 M15 6 21 12 15 18",
            "shield" => "M12 2 21 6V12Q20 19 12 22Q4 19 3 12V6Z M8 12 11 15 16 9",
            "link" => "M10 7 12 5Q18 1 21 5Q24 8 20 12L17 15 M7 10 4 13Q0 17 4 21Q8 24 12 20L15 17 M8 16 16 8",
            "snow" => "M12 1V23M2 6 22 18M2 18 22 6 M12 6 7 3M12 6 17 3M12 18 7 21M12 18 17 21",
            _ => "M12 2A10 10 0 1 1 12 22A10 10 0 1 1 12 2 M12 7V13 M12 17V18"
        };
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = Brush(color), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 24, Height = 24 };
        return new Viewbox { Child = path, Width = size, Height = size, Stretch = Stretch.Uniform };
    }
    public static StackPanel IconLabel(string icon, string label, string color = "#89B3C7") {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = Icon(icon, color, 16); mark.Margin = new Thickness(0, 0, 10, 0); panel.Children.Add(mark);
        panel.Children.Add(Text(label, 13, color)); return panel;
    }
    public static ComboBox Choices<T>(Choice<T>[] choices, T value) {
        var combo = new ComboBox { ItemsSource = choices, SelectedValuePath = "Value" };
        combo.SelectedValue = value; if (combo.SelectedIndex < 0 && choices.Length > 0) combo.SelectedIndex = 0; return combo;
    }
}

public sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
public sealed record QuestRow(Quest Quest, string Name, string Subtitle, string Code);
public sealed record LoadoutRow(Loadout Loadout, string Name, string Subtitle, string Association);
