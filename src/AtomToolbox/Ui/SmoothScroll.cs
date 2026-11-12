using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Atom.Toolbox.Desktop.Ui;

/// <summary>
/// Frame-rate independent smooth wheel scrolling. A single CompositionTarget.Rendering
/// subscription animates every active ScrollViewer, so wheel bursts allocate no timers and
/// nested scrollable areas (console, combo popups) still behave correctly.
/// </summary>
public static class SmoothScroll
{
    private sealed class ScrollState
    {
        public double Target;
        public double Position;
        public long LastTicks;
    }

    private static readonly Dictionary<ScrollViewer, ScrollState> Active = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Func<bool> _enabled = static () => false;
    private static bool _handlerInstalled;
    private static bool _rendering;

    public static void Enable(Func<bool> enabled)
    {
        _enabled = enabled;
        if (_handlerInstalled)
        {
            return;
        }

        _handlerInstalled = true;
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            Mouse.MouseWheelEvent,
            new MouseWheelEventHandler(OnMouseWheel),
            handledEventsToo: true);
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer || !_enabled() || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;

        if (!Active.TryGetValue(viewer, out var state))
        {
            state = new ScrollState
            {
                Position = viewer.VerticalOffset,
                Target = viewer.VerticalOffset
            };
            Active[viewer] = state;
            viewer.Unloaded += OnViewerUnloaded;
        }
        else if (!_rendering)
        {
            // A previous animation ended between wheel events; rebase on the live offset.
            state.Position = viewer.VerticalOffset;
            state.Target = viewer.VerticalOffset;
        }

        var delta = e.Delta * 1.15;
        state.Target = Math.Clamp(state.Target - delta, 0, viewer.ScrollableHeight);
        state.LastTicks = Clock.ElapsedTicks;

        if (!_rendering)
        {
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (Active.Count == 0)
        {
            StopRendering();
            return;
        }

        var now = Clock.ElapsedTicks;
        List<ScrollViewer>? finished = null;

        foreach (var (viewer, state) in Active)
        {
            var elapsed = (now - state.LastTicks) / (double)Stopwatch.Frequency;
            state.LastTicks = now;

            if (elapsed <= 0)
            {
                continue;
            }

            // Exponential smoothing is stable across 60/120/144 Hz displays.
            var factor = 1 - Math.Exp(-18 * Math.Min(elapsed, 0.05));
            state.Position += (state.Target - state.Position) * factor;

            if (Math.Abs(state.Target - state.Position) < 0.25)
            {
                state.Position = state.Target;
                viewer.ScrollToVerticalOffset(state.Position);
                (finished ??= new List<ScrollViewer>()).Add(viewer);
            }
            else
            {
                viewer.ScrollToVerticalOffset(state.Position);
            }
        }

        if (finished is not null)
        {
            foreach (var viewer in finished)
            {
                Active.Remove(viewer);
                viewer.Unloaded -= OnViewerUnloaded;
            }
        }

        if (Active.Count == 0)
        {
            StopRendering();
        }
    }

    private static void OnViewerUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Active.Remove(viewer);
            viewer.Unloaded -= OnViewerUnloaded;
        }
    }

    private static void StopRendering()
    {
        if (!_rendering)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }
}
