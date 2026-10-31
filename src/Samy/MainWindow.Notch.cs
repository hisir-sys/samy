using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Samy;

// Top-edge "notch" behaviour: a borderless window docked flush to the top of the screen that shows a small pill
// and opens when the pointer touches it (or a file is dragged onto it), then closes after the pointer leaves.
public sealed partial class MainWindow
{
    private const int NotchCollapsedWidth = 230;   // device-independent pixels, scaled by monitor DPI
    private const int NotchCollapsedHeight = 34;
    private const int NotchOpenTicks = 2;          // 2 x 80 ms of hovering before it opens
    private const int NotchCloseTicks = 8;         // ~0.64 s outside before it closes

    private bool _notchMode = true;
    private bool _keepOpen;
    private bool _suppressReopen;
    private int _insideTicks;
    private int _outsideTicks;
    private nint _dockMonitor;
    private DispatcherQueueTimer? _notchTimer;

    private void InitializeNotch()
    {
        _notchTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _notchTimer.Interval = TimeSpan.FromMilliseconds(80);
        _notchTimer.Tick += (_, _) => NotchTimer_Tick();
        if (_notchMode) _expanded = false; // start tucked into the top edge
        ApplyWindowChrome();
    }

    /// <summary>Switches between notch mode (borderless, docked) and the original floating window.</summary>
    private void ApplyWindowChrome()
    {
        try
        {
            if (_appWindow?.Presenter is OverlappedPresenter presenter)
            {
                bool framed = !_notchMode;
                presenter.SetBorderAndTitleBar(framed, framed);
                presenter.IsResizable = framed;
                presenter.IsMaximizable = framed;
                presenter.IsMinimizable = framed;
            }
        }
        catch (Exception ex) { Logger_NotchError(ex); }

        nint hwnd = WindowNative.GetWindowHandle(this);
        if (_notchMode)
        {
            Root.Background = new SolidColorBrush(Colors.Black);
            PinButton.Visibility = Visibility.Collapsed;
            KeepOpenButton.Visibility = Visibility.Visible;
            _notchTimer?.Start();
        }
        else
        {
            _notchTimer?.Stop();
            SetWindowRgn(hwnd, 0, true);
            Root.Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x0B, 0x0D, 0x12));
            Root.Padding = new Thickness(14);
            DragRegion.Visibility = Visibility.Visible;
            NotchPill.Visibility = Visibility.Collapsed;
            PinButton.Visibility = Visibility.Visible;
            KeepOpenButton.Visibility = Visibility.Collapsed;
            _expanded = true;
            ExpandedContent.Visibility = Visibility.Visible;
        }
        PositionWindow(ExpandedWidth, ExpandedHeight);
    }

    private void ApplyNotchBounds()
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        nint monitor = _dockMonitor != 0 ? _dockMonitor : MonitorFromWindow(hwnd, 2);
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            _dockMonitor = 0; // that display may have been unplugged
            monitor = MonitorFromWindow(hwnd, 2);
            info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return;
        }

        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale < 1) scale = 1;
        int screenWidth = info.Monitor.Right - info.Monitor.Left;
        int usableHeight = info.Work.Bottom - info.Monitor.Top;
        int width = Math.Min((int)((_expanded ? ExpandedWidth : NotchCollapsedWidth) * scale), screenWidth - 16);
        int height = Math.Min((int)((_expanded ? ExpandedHeight : NotchCollapsedHeight) * scale), usableHeight - 8);
        int x = info.Monitor.Left + (screenWidth - width) / 2;
        int y = info.Monitor.Top; // flush with the top edge, like a hardware notch

        SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);

        // Round only the bottom corners: the region starts above the window so the top corners fall off-screen.
        int radius = (int)((_expanded ? 22 : 14) * scale);
        nint region = CreateRoundRectRgn(0, -radius, width + 1, height + 1, radius * 2, radius * 2);
        if (region != 0) SetWindowRgn(hwnd, region, true); // the system owns the region after this call

        Root.Padding = _expanded ? new Thickness(14) : new Thickness(0);
        DragRegion.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        NotchPill.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotchPill();
    }

    private void NotchTimer_Tick()
    {
        if (!_notchMode) return;
        nint hwnd = WindowNative.GetWindowHandle(this);
        if (!GetCursorPos(out NativePoint pt) || !GetWindowRect(hwnd, out NativeRect rc)) return;
        bool inside = pt.X >= rc.Left && pt.X < rc.Right && pt.Y >= rc.Top && pt.Y < rc.Bottom;

        if (_suppressReopen)
        {
            if (!inside) _suppressReopen = false; // the pointer left, so hovering may open it again
            return;
        }

        if (!_expanded)
        {
            _outsideTicks = 0;
            if (inside) { if (++_insideTicks >= NotchOpenTicks) ExpandNotch(); }
            else { _insideTicks = 0; MaybeRedock(pt); }
            return;
        }

        _insideTicks = 0;
        if (inside || _keepOpen || NotchShouldStayOpen(hwnd)) { _outsideTicks = 0; return; }
        if (++_outsideTicks >= NotchCloseTicks) CollapseNotch(suppressReopen: false);
    }

    /// <summary>Stay open while a dropdown is showing or a dialog/file picker owned by SAMY is in front.</summary>
    private bool NotchShouldStayOpen(nint hwnd)
    {
        try
        {
            if (Content?.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0) return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        nint foreground = GetForegroundWindow();
        return foreground != 0 && foreground != hwnd && GetWindow(foreground, 4 /* GW_OWNER */) == hwnd;
    }

    /// <summary>Move the notch to another display when the pointer is pushed against that display's top edge.</summary>
    private void MaybeRedock(NativePoint pt)
    {
        nint other = MonitorFromPoint(pt, 2);
        if (other == 0) return;
        nint current = _dockMonitor != 0 ? _dockMonitor : MonitorFromWindow(WindowNative.GetWindowHandle(this), 2);
        if (other == current) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(other, ref info) || pt.Y > info.Monitor.Top + 1) return;
        _dockMonitor = other;
        ApplyNotchBounds();
    }

    private void ExpandNotch()
    {
        _insideTicks = 0;
        _outsideTicks = 0;
        if (_expanded) return;
        _expanded = true;
        ApplyNotchBounds();
    }

    private void CollapseNotch(bool suppressReopen)
    {
        _outsideTicks = 0;
        _suppressReopen = suppressReopen;
        if (!_expanded) return;
        _expanded = false;
        ApplyNotchBounds();
    }

    private void UpdateNotchPill()
    {
        if (!_notchMode || _expanded) return;
        NotchPillText.Text = _upgradesReady && _pomodoro.IsRunning
            ? $"{PomodoroEngine_Label()} {_pomodoro.RemainingSeconds / 60:00}:{_pomodoro.RemainingSeconds % 60:00}"
            : DateTime.Now.ToString("HH:mm");
    }

    private string PomodoroEngine_Label() => Samy.Core.PomodoroEngine.Label(_pomodoro.Phase);

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        // A file dragged onto the collapsed notch opens it straight on the shelf page.
        if (!_notchMode || _expanded) return;
        ExpandNotch();
        ShowPage(FilesPage);
    }

    private void KeepOpenButton_Click(object sender, RoutedEventArgs e)
    {
        _keepOpen = !_keepOpen;
        KeepOpenButton.Content = _keepOpen ? "KEEPING OPEN" : "KEEP OPEN";
    }

    private void NotchModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _notchMode = NotchModeToggle.IsOn;
        ApplyWindowChrome();
        SaveSettings();
    }

    private static void Logger_NotchError(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAMY");
            File.AppendAllText(Path.Combine(dir, "startup-error.log"), $"[{DateTimeOffset.Now:O}] Notch chrome fallback{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
}
