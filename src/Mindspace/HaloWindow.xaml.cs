using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace;

/// <summary>The always-there sliver at the top of the screen. Never takes focus.</summary>
public partial class HaloWindow : Window
{
    readonly DispatcherTimer _intent = new() { Interval = TimeSpan.FromMilliseconds(110) };
    readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(380) };
    readonly DispatcherTimer _peekTimer = new();
    Suggestion? _peek;
    bool _expanded;
    const int HotkeyId = 0x4D53;
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

    public HaloWindow()
    {
        InitializeComponent();
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        Island.ToolTip = "Mindspace  ·  Ctrl+Alt+Space";

        foreach (var a in Agents.All)
        {
            var orb = new AgentOrb { Agent = a, Width = 24, Height = 24, Margin = new Thickness(6, 0, 6, 0), Cursor = Cursors.Hand, ToolTip = $"{a.Name} · {a.Role}" };
            ToolTipService.SetInitialShowDelay(orb, 350);
            orb.MouseLeftButtonUp += (_, e) => { e.Handled = true; App.Mind.Panel.Open(a.Id); Collapse(); };
            orb.MouseEnter += (_, _) => Lift(orb, 1.18);
            orb.MouseLeave += (_, _) => Lift(orb, 1);
            orb.RenderTransformOrigin = new Point(0.5, 0.5);
            orb.RenderTransform = new ScaleTransform(1, 1);
            Orbs.Children.Add(orb);
        }

        _intent.Tick += (_, _) => { _intent.Stop(); if (Island.IsMouseOver) Expand(); };
        _collapse.Tick += (_, _) => { _collapse.Stop(); if (!Island.IsMouseOver) Collapse(); };
        _peekTimer.Tick += (_, _) => HidePeek();

        SourceInitialized += (_, _) =>
        {
            Native.AddExStyle(this, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            var src = HwndSource.FromHwnd(Native.Handle(this));
            src?.AddHook(WndProc);
            // Ctrl+Alt+Space; if another app owns it we simply go without (the island still works).
            Native.RegisterHotKey(Native.Handle(this), HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20);
            Native.AddClipboardFormatListener(Native.Handle(this));
        };
        Closed += (_, _) => { Native.UnregisterHotKey(Native.Handle(this), HotkeyId); Native.RemoveClipboardFormatListener(Native.Handle(this)); };
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == HotkeyId) { App.Mind.Panel.Toggle(); handled = true; }
        else if (msg == Native.WM_CLIPBOARDUPDATE && App.Mind.Settings.ClipboardSensing)
        {
            // Opt-in only. Text is examined by a local regex and dropped; see ContextSensor.
            try { if (Clipboard.ContainsText()) App.Mind.Sensor.OnClipboardText(Clipboard.GetText()); } catch { }
        }
        return 0;
    }

    static void Lift(UIElement e, double to)
    {
        var st = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = Ease };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------- island ----------

    void Island_Enter(object s, MouseEventArgs e) { _collapse.Stop(); if (!_expanded) _intent.Start(); }
    void Island_Leave(object s, MouseEventArgs e) { _intent.Stop(); _collapse.Stop(); _collapse.Start(); }
    void Island_Click(object s, MouseButtonEventArgs e) { App.Mind.Panel.Toggle(); Collapse(); }

    public void DemoExpand() { Expand(); }

    void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        Animate(Island, WidthProperty, 172, 260, Spring);
        Animate(Island, HeightProperty, 42, 260, Spring);
        Animate(Spark, OpacityProperty, 0, 120);
        Animate(Orbs, OpacityProperty, 1, 220);
        Orbs.IsHitTestVisible = true;
    }

    void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        Animate(Island, WidthProperty, 78, 220, Ease);
        Animate(Island, HeightProperty, 9, 220, Ease);
        Animate(Spark, OpacityProperty, 1, 260);
        Animate(Orbs, OpacityProperty, 0, 100);
        Orbs.IsHitTestVisible = false;
    }

    /// <summary>While the panel is open the island tucks away so the panel reads as "the island, opened".</summary>
    public void SetPanelOpen(bool open)
    {
        Collapse();
        Animate(Island, OpacityProperty, open ? 0 : 1, 160);
        Island.IsHitTestVisible = !open;
        if (open) HidePeek();
    }

    static void Animate(UIElement el, DependencyProperty p, double to, int ms, IEasingFunction? ease = null) =>
        el.BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Ease });

    // ---------- peek ----------

    public void Peek(Suggestion s)
    {
        if (App.Mind.Panel.IsOpen && s.Kind != "due") return;   // never interrupt an open conversation
        _peek = s;
        PeekOrb.Agent = s.Agent;
        PeekName.Text = s.Kind == "due" ? $"{s.Agent.Name} · reminder" : s.Agent.Name;
        PeekText.Text = s.Text;
        var edge = new LinearGradientBrush(Color.FromArgb(150, s.Agent.Accent.R, s.Agent.Accent.G, s.Agent.Accent.B), Color.FromArgb(40, 255, 255, 255), 90);
        PeekCard.BorderBrush = edge;

        PeekActions.Children.Clear();
        if (s.Kind == "due" && s.Payload is Reminder r)
        {
            PeekActions.Children.Add(PeekButton("Done", () => { if (!r.Done) App.Mind.Reminders.Toggle(r); HidePeek(); }));
            PeekActions.Children.Add(PeekButton("+10 min", () => { App.Mind.Reminders.Snooze(r, TimeSpan.FromMinutes(10)); HidePeek(); }));
        }
        else if (s.Kind == "remind" && s.Payload is WhenParser.Result c)
        {
            PeekActions.Children.Add(PeekButton("Add", () => { App.Mind.Reminders.Add(c.Title, c.Due); Flash("Added ✓"); }));
        }

        PeekCard.Visibility = Visibility.Visible;
        Animate(PeekCard, OpacityProperty, 1, 220);
        PeekShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-26, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(s.Kind == "due" ? 45 : 9);
        _peekTimer.Start();
    }

    Button PeekButton(string text, Action act)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(10, 4, 10, 5) };
        b.Click += (_, e) => { e.Handled = true; act(); };
        return b;
    }

    void Flash(string text)
    {
        PeekText.Text = text; PeekActions.Children.Clear();
        _peekTimer.Stop(); _peekTimer.Interval = TimeSpan.FromSeconds(1.4); _peekTimer.Start();
    }

    void Peek_Click(object s, MouseButtonEventArgs e)
    {
        if (_peek == null || e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        var p = _peek; HidePeek();
        switch (p.Kind)
        {
            case "due": App.Mind.Panel.Open("tick"); break;
            case "remind": App.Mind.Panel.Open("tick", (p.Payload as WhenParser.Result)?.Title); break;
            case "diagnose": App.Mind.Panel.Open("patch", "diagnose", autoRun: true); break;
            default: App.Mind.Panel.Open(p.Agent.Id); break;
        }
    }

    static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    void Peek_Enter(object s, MouseEventArgs e) => _peekTimer.Stop();
    void Peek_Leave(object s, MouseEventArgs e) { if (_peek != null) { _peekTimer.Interval = TimeSpan.FromSeconds(3); _peekTimer.Start(); } }
    void Peek_Close(object s, RoutedEventArgs e) => HidePeek();

    public void HidePeek()
    {
        _peekTimer.Stop();
        if (_peek == null) return;
        _peek = null;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (_peek == null) PeekCard.Visibility = Visibility.Collapsed; };
        PeekCard.BeginAnimation(OpacityProperty, fade);
        PeekShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(180)));
    }
}
