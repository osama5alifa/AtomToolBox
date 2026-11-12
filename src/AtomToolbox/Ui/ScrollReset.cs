using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Atom.Toolbox.Desktop.Ui;

/// <summary>
/// Resets a page's scroll position when its view model changes. The shell reuses a single
/// view instance per page type, so without this a new page would inherit the previous offset.
/// </summary>
public static class ScrollReset
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollReset),
        new PropertyMetadata(false, OnChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.DataContextChanged -= OnDataContextChanged;
        if (e.NewValue is true)
        {
            viewer.DataContextChanged += OnDataContextChanged;
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        viewer.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                viewer.ScrollToTop();
                viewer.ScrollToLeftEnd();
            }),
            DispatcherPriority.Loaded);
    }
}
