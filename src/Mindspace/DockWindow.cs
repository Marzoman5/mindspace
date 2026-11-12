using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace;

/// <summary>
/// Optional bubble dock. While it is on, the Windows taskbar is hidden with ShowWindow(SW_HIDE) and switched to
/// auto-hide so windows can use the strip it reserved. Both are put back when the dock closes, when Mindspace exits,
/// on any crash path we can hook, and at the next start if the process was killed (see App.RestoreShell).
/// Like the island at the top, the dock gets out of the way: when the window you are working in reaches the bottom
/// of the screen it drops down to a sliver, and comes back when the pointer touches the bottom edge.
/// </summary>
public sealed class DockWindow : Window
{
    readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _time = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock _date = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -1, 0, 0) };
    readonly Dictionary<string, ImageSource?> _icons = new();
    readonly System.Windows.Threading.DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(700) };
    string _signature = "";
    bool _down;
    const double Gap = 8, Sliver = 5;
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public DockWindow()
    {
        Title = "Mindspace Dock";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
        _date.Foreground = (Brush)FindResource("Muted");
        Foreground = (Brush)FindResource("Ink"); FontFamily = (FontFamily)FindResource("UiText"); FontSize = 13;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(StartButton());
        row.Children.Add(Divider());
        row.Children.Add(_apps);
        row.Children.Add(Divider());
        var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        clock.Children.Add(_time); clock.Children.Add(_date);
        row.Children.Add(clock);
        var orb = new AgentOrb { Agent = Agents.Sage, Width = 28, Height = 28, Cursor = Cursors.Hand, ToolTip = "Open Mindspace", Margin = new Thickness(0, 0, 2, 0) };
        orb.MouseLeftButtonUp += (_, _) => App.Mind.Panel.Toggle();
        row.Children.Add(orb);

        Content = new Border
        {
            Child = row, Padding = new Thickness(7, 5, 7, 5), CornerRadius = new CornerRadius(App.Frost ? 8 : 21),
            Background = (Brush)FindResource(App.Frost ? "GlassFrost" : "Glass"), BorderBrush = (Brush)FindResource(App.Frost ? "GlassEdgeFrost" : "GlassEdge"), BorderThickness = new Thickness(1),
        };

        SourceInitialized += (_, _) => { Native.AddExStyle(this, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW); if (App.Frost) Native.Frost(this); };
        SizeChanged += (_, _) => Reposition(false);
        Loaded += (_, _) => { App.Mind.BorrowShell(); Refresh(); Reposition(false); };
        Closed += (_, _) => { App.Mind.Sensor.ForegroundChanged -= OnForeground; App.Mind.RestoreShell(); };
        App.Mind.Sensor.ForegroundChanged += OnForeground;
        MouseEnter += (_, _) => { _leave.Stop(); SetDown(false); };
        MouseLeave += (_, _) => { _leave.Stop(); _leave.Start(); };
        _leave.Tick += (_, _) => { _leave.Stop(); if (!IsMouseOver) SetDown(InTheWay()); };
    }

    void OnForeground(nint _) => Dispatcher.BeginInvoke(() => { Refresh(); if (!IsMouseOver) SetDown(InTheWay()); });

    /// <summary>True when the window the user is working in covers the place where the dock floats.</summary>
    bool InTheWay()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == 0 || Native.IsIconic(fg) || Native.ProcessName(fg) == "Mindspace") return _down;
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;      // the desktop itself
        if (!Native.GetWindowRect(fg, out var r)) return false;
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double left = Left * k, right = (Left + ActualWidth) * k, top = (SystemParameters.PrimaryScreenHeight - ActualHeight - Gap) * k;
        return r.Bottom > top + 2 && r.Left < right && r.Right > left;
    }

    void SetDown(bool down)
    {
        if (down == _down && IsLoaded) return;
        _down = down;
        Reposition(true);
    }

    void Reposition(bool animate)
    {
        BeginAnimation(TopProperty, null);
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        double to = _down ? SystemParameters.PrimaryScreenHeight - Sliver : SystemParameters.PrimaryScreenHeight - ActualHeight - Gap;
        if (!animate || double.IsNaN(Top)) { Top = to; return; }
        var a = new DoubleAnimation(Top, to, TimeSpan.FromMilliseconds(_down ? 220 : 180)) { EasingFunction = Ease, FillBehavior = FillBehavior.Stop };
        a.Completed += (_, _) => Top = _down ? SystemParameters.PrimaryScreenHeight - Sliver : SystemParameters.PrimaryScreenHeight - ActualHeight - Gap;
        BeginAnimation(TopProperty, a);
    }

    public void Heartbeat()
    {
        if (Native.IsTaskbarVisible()) Native.SetTaskbarVisible(false);   // Explorer sometimes brings it back
        Refresh();
        if (!IsMouseOver) SetDown(InTheWay());
    }

    FrameworkElement Divider() => new Rectangle { Width = 1, Height = 20, Fill = (Brush)FindResource("Line"), Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };

    FrameworkElement StartButton()
    {
        var g = new UniformGrid { Rows = 2, Columns = 2, Width = 17, Height = 17 };
        foreach (var a in new[] { Agents.Quill, Agents.Tick, Agents.Sage, Agents.Patch }) g.Children.Add(new Rectangle { Fill = a.Gradient(), RadiusX = 2, RadiusY = 2, Margin = new Thickness(1) });
        return Bubble(g, "Start", () => Native.TapWinKey(), false, false);
    }

    Border Bubble(UIElement content, string tip, Action click, bool running, bool active)
    {
        var grid = new Grid { Width = 36, Height = 34 };
        grid.Children.Add(new ContentControl { Content = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Focusable = false });
        if (running)
            grid.Children.Add(new Border { Width = active ? 12 : 4, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, Background = active ? Agents.Sage.Gradient() : (Brush)FindResource("Faint") });
        var b = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(12), Background = active ? (Brush)FindResource("Surface") : Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
            RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 1), Margin = new Thickness(1, 0, 1, 0),
        };
        ToolTipService.SetInitialShowDelay(b, 350);
        ToolTipService.SetPlacement(b, PlacementMode.Top);
        b.MouseEnter += (_, _) => Scale(b, 1.16);
        b.MouseLeave += (_, _) => Scale(b, 1);
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    static void Scale(Border b, double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)) { EasingFunction = Ease };
        b.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        b.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // Looking up a window's program is the costly part of the 3-second refresh, and it never changes for a window.
    readonly Dictionary<nint, string> _names = new();
    string ProcName(nint h) => _names.TryGetValue(h, out var n) ? n : _names[h] = Native.ProcessName(h);

    void Refresh()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("h:mm tt");
        _date.Text = now.ToString("ddd, MMM d");

        var fg = Native.GetForegroundWindow();
        if (_names.Count > 200) _names.Clear();
        var windows = Native.TaskbarWindows().Where(h => ProcName(h) != "Mindspace").Take(14).ToList();
        var sig = string.Join(",", windows) + "|" + fg;
        if (sig == _signature) return;     // nothing changed: no layout work
        _signature = sig;

        _apps.Children.Clear();
        foreach (var h in windows)
        {
            var path = Native.ProcessPath(h) ?? "";
            if (!_icons.TryGetValue(path, out var icon)) _icons[path] = icon = path.Length > 0 ? Native.IconForPath(path) : null;
            UIElement face = icon != null
                ? new Image { Source = icon, Width = 24, Height = 24 }
                : new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("SurfaceHi") };
            if (icon != null) RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
            var hwnd = h;
            _apps.Children.Add(Bubble(face, ContextSensor.Shorten(Native.WindowTitle(h), 60), () =>
            {
                if (Native.GetForegroundWindow() == hwnd || hwnd == App.Mind.Sensor.LastExternalWindow && !Native.IsIconic(hwnd) && Native.ProcessName(Native.GetForegroundWindow()) == "Mindspace")
                    Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
                else
                {
                    if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                    Native.SetForegroundWindow(hwnd);
                }
            }, true, h == fg));
        }
    }
}
