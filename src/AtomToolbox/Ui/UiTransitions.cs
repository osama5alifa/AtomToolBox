using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Atom.Toolbox.Desktop.Ui;

/// <summary>Small, allocation-conscious transitions used by the shell.</summary>
public static class UiTransitions
{
    public static void FadeSlideIn(FrameworkElement element, TranslateTransform? transform)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.15,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        });

        if (transform is null)
        {
            return;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = 14,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        });
    }

    public static void ThemePulse(Window? window)
    {
        if (window is null || window.Content is not FrameworkElement root)
        {
            return;
        }

        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.88,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }
}
