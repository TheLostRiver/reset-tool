using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Frostbound.Core;

namespace Frostbound;

public partial class MainWindow
{
    private bool switchingInterface, animateWindowResize;
    private double finalMinWidth, finalMinHeight;
    private static readonly TimeSpan PageDuration = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(200);

    private bool CanAnimate => IsLoaded && !App.IsRendering && SystemParameters.ClientAreaAnimation;

    private void ShowPage(ContentControl host, FrameworkElement content)
    {
        bool changed = !ReferenceEquals(host.Content, content); host.Content = content;
        if (changed && CanAnimate && !switchingInterface) AnimatePageIn(host);
    }

    private void AnimatePageIn(ContentControl host)
    {
        host.Opacity = 1;
        host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, PageDuration) { FillBehavior = FillBehavior.Stop });
        var offset = new TranslateTransform(); host.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, PageDuration) {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        });
    }

    private async void SwitchInterfaceStyle(InterfaceStyle style)
    {
        if (switchingInterface || Runtime.Settings.InterfaceStyle == style && displayedStyle == style) return;
        if (!CanAnimate) { Runtime.Settings.InterfaceStyle = style; Runtime.Save(); Rebuild(); return; }
        switchingInterface = true; InterfaceSwitch.IsEnabled = ThemeSwitch.IsEnabled = false;
        PageHost.IsHitTestVisible = CompactHost.IsHitTestVisible = false;
        try {
            var root = (FrameworkElement)Content; var dpi = VisualTreeHelper.GetDpi(root);
            var snapshot = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            snapshot.Render(root); snapshot.Freeze(); InterfaceSnapshot.Source = snapshot; InterfaceSnapshot.Opacity = 1; InterfaceSnapshot.Visibility = Visibility.Visible;
            PageHost.BeginAnimation(OpacityProperty, null); CompactHost.BeginAnimation(OpacityProperty, null);
            PageHost.Opacity = CompactHost.Opacity = Sidebar.Opacity = 1;
            Runtime.Settings.InterfaceStyle = style; Runtime.Save(); animateWindowResize = true; Rebuild();
            await Task.Delay(220);
            FinishWindowResize(); root.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (!shuttingDown) {
                InterfaceSnapshot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, PageDuration));
                await Task.Delay(180);
            }
        } catch (Exception e) { ShowToast(e.Message); }
        finally {
            FinishWindowResize(); animateWindowResize = false; switchingInterface = false;
            PageHost.Opacity = CompactHost.Opacity = 1; PageHost.IsHitTestVisible = CompactHost.IsHitTestVisible = true; InterfaceSwitch.IsEnabled = true;
            Sidebar.Opacity = 1;
            ThemeSwitch.IsEnabled = true; InterfaceSnapshot.BeginAnimation(OpacityProperty, null); InterfaceSnapshot.Visibility = Visibility.Collapsed; InterfaceSnapshot.Source = null; InterfaceSnapshot.Opacity = 1;
        }
    }

    private void SetWindowBounds(Rect bounds, double minWidth, double minHeight)
    {
        double startWidth = Width, startHeight = Height, startLeft = Left, startTop = Top;
        finalMinWidth = minWidth; finalMinHeight = minHeight;
        MinWidth = animateWindowResize ? Math.Min(MinWidth, minWidth) : minWidth;
        MinHeight = animateWindowResize ? Math.Min(MinHeight, minHeight) : minHeight;
        Width = bounds.Width; Height = bounds.Height;
        if (double.IsFinite(bounds.Left)) Left = bounds.Left;
        if (double.IsFinite(bounds.Top)) Top = bounds.Top;
        if (!animateWindowResize) return;
        void Animate(DependencyProperty property, double from, double to) {
            if (!double.IsFinite(from) || !double.IsFinite(to) || Math.Abs(from - to) < .1) return;
            BeginAnimation(property, new DoubleAnimation(from, to, ResizeDuration) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
            });
        }
        Animate(WidthProperty, startWidth, bounds.Width); Animate(HeightProperty, startHeight, bounds.Height);
        Animate(LeftProperty, startLeft, bounds.Left); Animate(TopProperty, startTop, bounds.Top);
    }

    private void FinishWindowResize()
    {
        if (!animateWindowResize) return;
        BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null); BeginAnimation(LeftProperty, null); BeginAnimation(TopProperty, null);
        MinWidth = finalMinWidth; MinHeight = finalMinHeight;
    }
}
