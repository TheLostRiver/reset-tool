using System.Windows;
using System.Windows.Controls;
using Frostbound.Core;
using Frostbound.UI;
using static Frostbound.UI.Elements;

namespace Frostbound;

public partial class MainWindow
{
    private ComboBox? themeChoice;
    private string ThemeName(AppearanceTheme mode) => mode switch {
        AppearanceTheme.Dark => T("深色", "Dark", "ダーク"), AppearanceTheme.Light => T("浅色", "Light", "ライト"), _ => T("跟随系统", "System", "システム")
    };
    private Choice<AppearanceTheme>[] ThemeChoices() => [new(AppearanceTheme.Dark, ThemeName(AppearanceTheme.Dark)), new(AppearanceTheme.Light, ThemeName(AppearanceTheme.Light)), new(AppearanceTheme.System, ThemeName(AppearanceTheme.System))];

    private void UpdateThemeControls()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(Runtime.Settings.Theme == AppearanceTheme.System ? "system" : ThemeManager.IsDark ? "moon" : "sun", "#8FA8BB", 13);
        icon.Margin = new Thickness(0, 0, 5, 0); content.Children.Add(icon); content.Children.Add(Text(ThemeName(Runtime.Settings.Theme), 10, "#A8BFCD"));
        ThemeSwitch.Content = content; ThemeSwitch.ToolTip = T("选择深色、浅色或跟随 Windows 应用颜色", "Choose dark, light, or follow Windows app colors", "ダーク・ライト・Windows のアプリ設定に従う");
        if (themeChoice != null) themeChoice.SelectedValue = Runtime.Settings.Theme;
    }

    private void SetTheme(AppearanceTheme mode)
    {
        if (Runtime.Settings.Theme == mode) return;
        Runtime.Settings.Theme = mode; Runtime.Save(); ThemeManager.SetMode(mode);
    }

    internal ContextMenu CreateThemeMenu()
    {
        var menu = new ContextMenu { PlacementTarget = ThemeSwitch, Background = Brush("#18222D"), Foreground = Brush("#E3ECF3"), BorderBrush = Brush("#2B3947"), BorderThickness = new Thickness(1), Padding = new Thickness(5) };
        foreach (var choice in ThemeChoices()) {
            var item = new MenuItem { Header = choice.Label, IsCheckable = true, IsChecked = Runtime.Settings.Theme == choice.Value };
            item.Click += (_, _) => SetTheme(choice.Value); menu.Items.Add(item);
        }
        return menu;
    }

    private void ThemeSwitchClick(object sender, RoutedEventArgs e)
    {
        var menu = CreateThemeMenu(); ThemeSwitch.ContextMenu = menu; menu.IsOpen = true;
    }
}
