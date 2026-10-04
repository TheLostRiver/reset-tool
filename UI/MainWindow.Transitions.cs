using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Frostbound.Core;

namespace Frostbound;

public partial class MainWindow
{
    private bool switchingInterface;
    private static readonly TimeSpan PageDuration = TimeSpan.FromMilliseconds(120);
    private bool CanAnimate => IsLoaded && !App.IsRendering && SystemParameters.ClientAreaAnimation;

    private void ShowPage(ContentControl host, FrameworkElement content)
    {
        bool changed = !ReferenceEquals(host.Content, content); host.Content = content;
        if (changed && CanAnimate && !switchingInterface) AnimatePageIn(host);
    }

    private void AnimatePageIn(ContentControl host)
    {
        host.BeginAnimation(OpacityProperty, null); host.Opacity = 1; host.RenderTransform = Transform.Identity;
        host.BeginAnimation(OpacityProperty, new DoubleAnimation(.88, 1, PageDuration) { FillBehavior = FillBehavior.Stop });
    }

    private void SwitchInterfaceStyle(InterfaceStyle style)
    {
        if (switchingInterface || Runtime.Settings.InterfaceStyle == style && displayedStyle == style) return;
        switchingInterface = true;
        try {
            PageHost.BeginAnimation(OpacityProperty, null); CompactHost.BeginAnimation(OpacityProperty, null);
            PageHost.Opacity = CompactHost.Opacity = 1;
            PageHost.RenderTransform = CompactHost.RenderTransform = Transform.Identity;
            Runtime.Settings.InterfaceStyle = style; Runtime.Save(); Rebuild();
            ((FrameworkElement)Content).UpdateLayout();
            if (CanAnimate) AnimatePageIn(style == InterfaceStyle.Compact ? CompactHost : PageHost);
        } finally { switchingInterface = false; }
    }

    private void SetWindowBounds(Rect bounds, double minWidth, double minHeight)
    {
        // Relax the old minimum before shrinking; raise the new minimum after expanding.
        MinWidth = Math.Min(MinWidth, minWidth); MinHeight = Math.Min(MinHeight, minHeight);
        Width = bounds.Width; Height = bounds.Height;
        MinWidth = minWidth; MinHeight = minHeight;
        if (double.IsFinite(bounds.Left) && Math.Abs(Left - bounds.Left) > .1) Left = bounds.Left;
        if (double.IsFinite(bounds.Top) && Math.Abs(Top - bounds.Top) > .1) Top = bounds.Top;
    }
}
