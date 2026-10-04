using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Frostbound.Core;

namespace Frostbound.UI;

internal static class ThemeManager
{
    private static readonly Dictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PaletteColor> colors = new(StringComparer.OrdinalIgnoreCase);
    private sealed class PaletteColor : DependencyObject
    {
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(PaletteColor));
        public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    }
    private static DispatcherTimer? watcher;
    private static Dispatcher? dispatcher;
    public static AppearanceTheme Mode { get; private set; } = AppearanceTheme.Dark;
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;
    private static readonly Dictionary<string, string> light = new(StringComparer.OrdinalIgnoreCase) {
        ["#10171F"] = "#F3F6FA", ["#101B24"] = "#FFFFFF", ["#102C35"] = "#FFFFFF", ["#112B37"] = "#FFFFFF",
        ["#111B25"] = "#F5F8FB", ["#111C27"] = "#F5F8FB", ["#121B24"] = "#F8FAFC", ["#121D27"] = "#F8FAFC",
        ["#141E28"] = "#EDF3F8", ["#15232F"] = "#F5F8FB", ["#172630"] = "#F0F5F9", ["#18222D"] = "#FFFFFF",
        ["#192735"] = "#FFFFFF", ["#192934"] = "#E4EEF4", ["#192B36"] = "#E8F1F6", ["#1E2C38"] = "#E3EDF5",
        ["#1E303A"] = "#EDF5F6", ["#1E3040"] = "#EAF2F7", ["#20323F"] = "#EAF2F7", ["#203640"] = "#DFEFF2",
        ["#203949"] = "#E1EEF6", ["#213442"] = "#DAEAF3", ["#22313F"] = "#E7EEF5", ["#243441"] = "#D4DFE8",
        ["#24403F"] = "#E2F1ED", ["#253340"] = "#D4DEE8", ["#263544"] = "#FFFFFF", ["#273542"] = "#CBD7E2",
        ["#28424F"] = "#DAEFF5", ["#29434F"] = "#E0F0F5", ["#294556"] = "#D7EAF3", ["#2A3E4D"] = "#D6E1E8",
        ["#2B3947"] = "#CCD8E3", ["#2B404E"] = "#C5D6E1", ["#304A5C"] = "#B4CDD9", ["#304B5D"] = "#C3D6E2",
        ["#314452"] = "#CCD9E2", ["#334452"] = "#CCD8E3", ["#344655"] = "#C3D1DF", ["#354858"] = "#C0D1DD",
        ["#354959"] = "#B7C9D7", ["#354E61"] = "#B4CBD9", ["#365469"] = "#B4CDD9", ["#385366"] = "#C1D3DF",
        ["#385668"] = "#A9C5D7", ["#3B5364"] = "#BDD0DD", ["#3D5163"] = "#C0D1DE", ["#3F6661"] = "#AFCDBF",
        ["#426174"] = "#AFC6D6", ["#4C6273"] = "#A7BBCB", ["#456A7B"] = "#A9CFDB", ["#3F5D72"] = "#688093",
        ["#6A9EAD"] = "#2B768D", ["#7EBCCF"] = "#2B788D", ["#81CADA"] = "#2A7891", ["#81CADB"] = "#2A7891",
        ["#8AD8E7"] = "#176B84", ["#9ADCE8"] = "#227188", ["#96D6CB"] = "#258875", ["#A0DECE"] = "#216854",
        ["#E0AE91"] = "#9B4D23", ["#E0BA85"] = "#925D16", ["#D5AF78"] = "#925D16", ["#D5B983"] = "#8A692C",
        ["#1B303F"] = "#E6F0F7", ["#162431"] = "#F3F8FB"
    };

    public static void Initialize(AppearanceTheme mode)
    {
        foreach (var color in KnownColors.Split(' ', StringSplitOptions.RemoveEmptyEntries)) Brush(color);
        foreach (var alias in new Dictionary<string, string> {
            ["BackgroundBrush"] = "#10171F", ["PanelBrush"] = "#18222D", ["InputBrush"] = "#111B25", ["BorderBrush"] = "#2B3947",
            ["TextBrush"] = "#E3ECF3", ["MutedBrush"] = "#8295A8", ["AccentBrush"] = "#8AD8E7", ["GoldBrush"] = "#D5B983"
        }) Application.Current.Resources[alias.Key] = Brush(alias.Value);
        SetMode(mode);
    }

    public static SolidColorBrush Brush(string source)
    {
        if (brushes.TryGetValue(source, out var brush)) return brush;
        var token = new PaletteColor { Color = ColorFor(source) }; colors[source] = token;
        brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Color)) { Source = token }); brushes[source] = brush;
        Application.Current.Resources["Color." + source.TrimStart('#').ToUpperInvariant()] = brush; return brush;
    }

    public static LinearGradientBrush Gradient(string first, string second)
    {
        Brush(first); Brush(second);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        var start = new GradientStop { Offset = 0 }; var end = new GradientStop { Offset = 1 };
        BindingOperations.SetBinding(start, GradientStop.ColorProperty, new Binding(nameof(PaletteColor.Color)) { Source = colors[first] });
        BindingOperations.SetBinding(end, GradientStop.ColorProperty, new Binding(nameof(PaletteColor.Color)) { Source = colors[second] });
        brush.GradientStops.Add(start); brush.GradientStops.Add(end); return brush;
    }

    private static Color ColorFor(string source)
    {
        var color = (Color)ColorConverter.ConvertFromString(source);
        if (IsDark) return color;
        if (light.TryGetValue(source, out string? mapped)) return (Color)ColorConverter.ConvertFromString(mapped);
        string target = color.R > color.B + 30 && color.G > color.B + 15 ? "#8A692C" :
            color.G > color.R + 35 && color.B > color.R + 30 ? "#216E85" :
            (.2126 * color.R + .7152 * color.G + .0722 * color.B) < 166 ? "#60788B" : "#2B4354";
        var result = (Color)ColorConverter.ConvertFromString(target); result.A = color.A; return result;
    }

    public static void SetMode(AppearanceTheme mode)
    {
        Mode = mode; Apply(mode == AppearanceTheme.Dark || mode == AppearanceTheme.System && ReadSystemDark());
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        foreach (var pair in colors) pair.Value.Color = ColorFor(pair.Key);
        Changed?.Invoke();
    }

    private static bool ReadSystemDark()
    {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        } catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { return true; }
    }

    public static void StartMonitoring(Dispatcher owner)
    {
        if (watcher != null) return;
        dispatcher = owner; SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        watcher = new DispatcherTimer(DispatcherPriority.Background, owner) { Interval = TimeSpan.FromSeconds(2) };
        watcher.Tick += (_, _) => RefreshSystem(); watcher.Start();
    }

    private static void SystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (dispatcher?.HasShutdownStarted == false) dispatcher.BeginInvoke((Action)RefreshSystem);
    }
    private static void RefreshSystem() { if (Mode == AppearanceTheme.System && ReadSystemDark() != IsDark) Apply(!IsDark); }
    public static void StopMonitoring()
    {
        watcher?.Stop(); watcher = null; dispatcher = null; SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
    }
    private const string KnownColors = "#10171F #101B24 #102C35 #111B25 #111C27 #112B37 #121B24 #121D27 #141E28 #15232F #172630 #18222D #192735 #192934 #192B36 #1E2C38 #1E303A #1E3040 #20323F #203640 #203949 #213442 #22313F #243441 #24403F #253340 #263544 #273542 #28424F #29434F #294556 #2A3E4D #2B3947 #2B404E #304A5C #304B5D #314452 #334452 #344655 #354858 #354959 #354E61 #365469 #385366 #385668 #3B5364 #3D5163 #3F5D72 #3F6661 #426174 #456A7B #4C6273 #506D7E #546D81 #547B92 #57768D #5D778B #5E7F92 #5F7E94 #627D92 #657F93 #658093 #668296 #68879C #6A9EAD #6B8AA0 #6C91A4 #6D8CA2 #6D8DA3 #6E8CA2 #6E8EA4 #6E91A6 #708A9F #708EA4 #718EA3 #718FA4 #758EA0 #758FA3 #7692A6 #7797AB #7894A8 #7998AE #7D98AC #7D9BAE #7D9BB0 #7DA3B8 #7EBCCF #7F99AC #809CAF #819BAE #81CADA #81CADB #8295A8 #8298AA #83A6BC #83A7BE #89B3C7 #89B8CC #8AD8E7 #8FA2B3 #8FA8BB #8FA9BC #8FADBF #8FD9E7 #91A7B9 #91A8BA #91BCC8 #91D8E7 #95C9DA #96A7B6 #96D6CB #9ADCE8 #9ADFE9 #9AE0ED #9CAFB9 #9DDFEC #9EBACC #A0DECE #A5C7D8 #A8BFCD #A8C9DB #AFBECA #AFCADB #B6D1E0 #B7CFDF #B8D5E3 #BCCBD6 #BCD7E6 #C2AD84 #C3D8E7 #C3DBE9 #C4DCEA #C6DDEB #C6E3ED #C7DBE7 #CDE6F0 #D4E7F2 #D5AF78 #D5B983 #D5E8F1 #D5EAF5 #DBF1F7 #DDE8F0 #DDECF5 #E0AE91 #E0BA85 #E2EEF5 #E3ECF3 #1B303F #162431";
}
