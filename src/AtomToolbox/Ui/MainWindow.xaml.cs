using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Atom.Toolbox.Desktop.ViewModels;

namespace Atom.Toolbox.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _headerTimer;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = shell;
        InitializeComponent();
        AppLog.Info("window: xaml loaded");

        ShellViewModel.WindowStateCommand += OnWindowStateCommand;
        _shell.PageReady += OnPageReady;
        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        PreviewKeyDown += OnPreviewKeyDown;

        _headerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _headerTimer.Tick += async (_, _) => await _shell.RefreshHeaderAsync();
        _headerTimer.Start();
    }

    private void OnPageReady()
    {
        Dispatcher.BeginInvoke(
            new Action(() => UiTransitions.FadeSlideIn(PageHost, PageTranslate)),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _shell.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("startup", ex);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ShellViewModel.WindowStateCommand -= OnWindowStateCommand;
        _shell.PageReady -= OnPageReady;
        _headerTimer.Stop();
    }

    private void OnWindowStateCommand(string command)
    {
        switch (command)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "maximize":
                ToggleMaximize();
                break;
            case "close":
                Close();
                break;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (FindAncestor<ButtonBase>(source) is not null || FindAncestor<TextBox>(source) is not null))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse was released during layout.
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeGlyph() =>
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "[]";

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_shell.IsSearchOpen || _shell.SearchText.Length > 0)
            {
                _shell.SearchText = "";
                _shell.IsSearchOpen = false;
                SearchPopup.IsOpen = false;
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenResult(_shell.SearchResults.FirstOrDefault());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _shell.SearchText = "";
            _shell.IsSearchOpen = false;
            SearchPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _shell.SearchResults.Count > 0)
        {
            SearchList.SelectedIndex = 0;
            SearchList.Focus();
            e.Handled = true;
        }
    }

    private void SearchList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is SearchResultViewModel result)
        {
            OpenResult(result);
        }
    }

    private void OpenResult(SearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        _shell.SearchText = "";
        _shell.IsSearchOpen = false;
        SearchPopup.IsOpen = false;
        _shell.Navigate(result.PageId);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
    }

    private static IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (message == WmGetMinMaxInfo)
        {
            ApplyWorkArea(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ApplyWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition.X = info.WorkArea.Left - info.Monitor.Left;
        minMax.MaxPosition.Y = info.WorkArea.Top - info.Monitor.Top;
        minMax.MaxSize.X = info.WorkArea.Right - info.WorkArea.Left;
        minMax.MaxSize.Y = info.WorkArea.Bottom - info.WorkArea.Top;
        Marshal.StructureToPtr(minMax, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
