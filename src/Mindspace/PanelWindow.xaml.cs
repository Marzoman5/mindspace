using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mindspace.Controls;
using Mindspace.Services;
using Mindspace.Views;

namespace Mindspace;

public interface IAgentView
{
    /// <summary>Called every time the view is brought forward. <paramref name="intent"/> is optional prefilled text.</summary>
    void Activate(string? intent, bool autoRun);
}

/// <summary>The drop-down: four agents, one at a time, in a single sheet of glass under the island.</summary>
public partial class PanelWindow : Window
{
    readonly Dictionary<string, FrameworkElement> _views = new();
    string _current = "sage";
    bool _closing;
    public bool IsOpen { get; private set; }
    public bool SuppressDismiss { get; set; }
    static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public PanelWindow()
    {
        InitializeComponent();
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        if (App.Frost)
        {
            // Frosted: the window is exactly the sheet of glass, and Windows rounds and blurs it.
            Shell.Margin = new Thickness(0); Shell.CornerRadius = new CornerRadius(8); Shade.Visibility = Visibility.Collapsed;
            Shell.Background = (Brush)FindResource("GlassFrost"); Shell.BorderBrush = (Brush)FindResource("GlassEdgeFrost");
            Width = 440; Top = 8;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        }
        SourceInitialized += (_, _) => { Native.AddExStyle(this, Native.WS_EX_TOOLWINDOW); if (App.Frost) Native.Frost(this); };
        Deactivated += (_, _) => { if (IsOpen && PinBtn.IsChecked != true && !SuppressDismiss) Dismiss(); };
        PreviewKeyDown += OnKey;
        Clipper.SizeChanged += (_, _) => Clipper.Clip = new RectangleGeometry(new Rect(0, 0, Clipper.ActualWidth, Clipper.ActualHeight), App.Frost ? 7 : 23, App.Frost ? 7 : 23);
        App.Mind.Generated += (model, s) => SetStatus(true, $"On-device · {model} · {s.TokensPerSec:0} tok/s · first word {s.FirstTokenMs / 1000:0.0}s");
        BuildTabs();
        Agents.Changed += () =>
        {
            // custom agents were edited: drop their views so they are rebuilt with the new instructions
            foreach (var key in _views.Keys.Where(k => k.StartsWith("custom-")).ToList()) _views.Remove(key);
            if (Agents.All.All(a => a.Id != _current)) _current = "sage";
            BuildTabs(ReferenceEquals(Body.Content, _views.GetValueOrDefault("settings")) ? null : _current);
        };
    }

    void OnKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9 && e.Key - Key.D1 < Agents.All.Count)
        { Show(Agents.All[e.Key - Key.D1].Id, null, false); e.Handled = true; }
    }

    // ---------- open / close ----------

    public void Toggle() { if (IsOpen) Dismiss(); else Open(); }

    public void Open(string? id = null, string? intent = null, bool autoRun = false)
    {
        Show(string.IsNullOrEmpty(id) ? _current : id, intent, autoRun);
        if (IsOpen && !_closing) { Activate(); return; }
        IsOpen = true; _closing = false;   // also rescues a panel that was mid-fade-out
        App.Mind.Halo.SetPanelOpen(true);
        Show();
        Activate();
        Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)));
        Shade.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        if (App.Frost)
        {
            // The frost is the window's own shape, so the glass stays put and only its contents arrive.
            ShellScale.ScaleX = ShellScale.ScaleY = 1; ShellShift.Y = 0;
            ContentShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
        }
        else
        {
            var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = Spring };
            ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ShellShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = Spring });
        }
        _ = RefreshStatusAsync();
    }

    public void Dismiss()
    {
        if (!IsOpen || _closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) =>
        {
            if (!_closing) return;
            IsOpen = false; _closing = false;
            Hide();
            App.Mind.Halo.SetPanelOpen(false);
        };
        Shell.BeginAnimation(OpacityProperty, fade);
        Shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)));
        if (!App.Frost)
        {
            var shrink = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(130)) { EasingFunction = Ease };
            ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }
    }

    // ---------- views ----------

    void Show(string id, string? intent, bool autoRun)
    {
        id = id.ToLowerInvariant();
        if (id is not ("settings" or "welcome") && Agents.All.All(a => a.Id != id)) id = "sage";
        if (!_views.TryGetValue(id, out var view))
        {
            view = id switch
            {
                "quill" => new QuillView(),
                "tick" => new TickView(),
                "patch" => new PatchView(),
                "echo" => new EchoView(),
                "iris" => new IrisView(),
                "settings" => new SettingsView(),
                "welcome" => new WelcomeView(),
                _ => new SageView(Agents.ById(id)),
            };
            _views[id] = view;
        }
        bool changed = !ReferenceEquals(Body.Content, view);
        _current = id is "settings" or "welcome" ? _current : id;
        Body.Content = view;
        if (changed)
        {
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var agent = id is "settings" or "welcome" ? null : Agents.ById(id);
            Aura.Fill = AuraBrush(agent ?? Agents.Sage);
            BuildTabs(id is "settings" or "welcome" ? null : id);
        }
        (view as IAgentView)?.Activate(intent, autoRun);
    }

    /// <summary>The agent's colours as a soft glow: a plain radial gradient, no blur shader.</summary>
    static Brush AuraBrush(Agent a)
    {
        var b = new RadialGradientBrush { GradientOrigin = new(0.5, 0.5), Center = new(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        Color c1 = a.Colors[^1], c2 = a.Colors[a.Colors.Length / 2];
        b.GradientStops.Add(new GradientStop(Color.FromArgb(105, c2.R, c2.G, c2.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(55, c1.R, c1.G, c1.B), 0.45));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c1.R, c1.G, c1.B), 1));
        b.Freeze();
        return b;
    }

    void BuildTabs(string? selected = null)
    {
        Tabs.Children.Clear();
        foreach (var a in Agents.All)
        {
            bool on = a.Id == selected;
            double size = Agents.All.Count > 7 ? 24 : 30;
            var orb = new AgentOrb { Agent = a, Width = size, Height = size };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(orb);
            if (on)
            {
                var text = new StackPanel { Margin = new Thickness(9, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = a.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Ui") });
                text.Children.Add(new TextBlock { Text = a.Role, FontSize = 10.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, -2, 0, 0) });
                row.Children.Add(text);
            }
            var tab = new Border
            {
                Child = row, CornerRadius = new CornerRadius(19), Padding = new Thickness(Agents.All.Count > 6 ? 3 : 4), Margin = new Thickness(0, 0, Agents.All.Count > 6 ? 1 : 4, 0), Cursor = Cursors.Hand,
                Background = on ? (Brush)FindResource("Surface") : Brushes.Transparent, Opacity = on || selected == null ? 1 : 0.72, ToolTip = on ? null : $"{a.Name} · {a.Role}",
                Focusable = false,
            };
            ToolTipService.SetInitialShowDelay(tab, 300);
            tab.MouseEnter += (_, _) => { if (!on) tab.Opacity = 1; };
            tab.MouseLeave += (_, _) => { if (!on && selected != null) tab.Opacity = 0.72; };
            tab.MouseLeftButtonUp += (_, _) => Show(a.Id, null, false);
            if (a == Agents.Tick && App.Mind.Reminders.OpenCount > 0 && !on)
                tab.ToolTip = $"Tick · {App.Mind.Reminders.OpenCount} open";
            Tabs.Children.Add(tab);
        }
    }

    /// <summary>The orb of the agent that is currently generating bounces its eyes.</summary>
    public void SetThinking(Agent agent, bool on)
    {
        int i = Agents.All.ToList().IndexOf(agent);
        if (i >= 0 && i < Tabs.Children.Count && Tabs.Children[i] is Border { Child: StackPanel sp } && sp.Children[0] is AgentOrb orb) orb.IsThinking = on;
    }

    // ---------- footer ----------

    async Task RefreshStatusAsync()
    {
        bool up = await App.Mind.Ollama.IsUpAsync();
        if (up) { if (StatusText.Text.StartsWith("Ollama") || StatusText.Text == "On-device") SetStatus(true, "On-device · nothing leaves this PC"); }
        else SetStatus(false, "Ollama isn't running");
    }

    public void SetStatus(bool ok, string text)
    {
        StatusDot.Fill = (Brush)FindResource(ok ? "Good" : "Danger");
        StatusText.Text = text;
        StartOllama.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    async void StartOllama_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "Starting Ollama…"; StartOllama.Visibility = Visibility.Collapsed;
        bool ok = await App.Mind.Ollama.TryStartAsync();
        SetStatus(ok, ok ? "On-device · nothing leaves this PC" : "Ollama not found — install it from ollama.com");
    }

    void Pin_Click(object s, RoutedEventArgs e) { }
    void Settings_Click(object s, RoutedEventArgs e) => Show(ReferenceEquals(Body.Content, _views.GetValueOrDefault("settings")) ? _current : "settings", null, false);

    // ---------- insert ----------

    /// <summary>
    /// Pastes text into the app the user was in before opening Mindspace. Only reachable from the Insert button.
    /// Works like Copy followed by Ctrl+V in that app.
    /// </summary>
    public async void InsertText(string text)
    {
        var target = App.Mind.Sensor.LastExternalWindow;
        Dismiss();
        await Task.Delay(180);
        await PasteIntoAsync(target, text);
    }

    /// <summary>
    /// Puts text on the clipboard and pastes it into one specific window, exactly like Copy then Ctrl+V there.
    /// Returns false (text stays on the clipboard) if that window is gone, is a terminal or a private window, or did
    /// not take focus. Only reached from Insert and from the user's own dictation.
    /// </summary>
    public async Task<bool> PasteIntoAsync(nint target, string text)
    {
        try { Clipboard.SetText(text); } catch { return false; }
        if (target == 0 || !Native.IsWindow(target)) return false;   // nothing to paste into
        if (Native.IsTerminal(target)) return false;                 // never type into a shell; the user pastes there by hand
        if (ContextSensor.IsPrivate(Native.WindowTitle(target))) return false;
        for (int i = 0; i < 40 && Native.ModifiersDown(); i++) await Task.Delay(25);   // a hotkey may still be held
        if (Native.GetForegroundWindow() != target)
        {
            Native.SetForegroundWindow(target);
            await Task.Delay(140);
        }
        if (Native.GetForegroundWindow() != target) return false;    // focus didn't land where expected: don't type anywhere else
        Native.SendPaste();
        // The text deliberately stays on the clipboard afterwards, exactly as if the user had pressed Copy:
        // if the paste was blocked (e.g. an elevated window) they can still Ctrl+V it, and we never rewrite
        // clipboard contents we did not create.
        return true;
    }

#if DEBUG
    public T? DebugView<T>(string id) where T : class => _views.GetValueOrDefault(id) as T;
#endif

    /// <summary>Dictation while the panel has focus goes into whatever box the user is typing in.</summary>
    public void TypeIntoFocus(string text)
    {
        if (Keyboard.FocusedElement is TextBox tb && !tb.IsReadOnly)
        {
            int at = tb.SelectionStart;
            string add = (at > 0 && !char.IsWhiteSpace(tb.Text[at - 1]) ? " " : "") + text;
            tb.SelectedText = add;
            tb.SelectionLength = 0; tb.CaretIndex = at + add.Length;
        }
        else Open("sage", text);
    }
}
