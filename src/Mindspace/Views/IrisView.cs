using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace.Views;

/// <summary>
/// Iris looks at the window you were just in. Reading is through the accessibility interface (see WindowReader);
/// the two things that touch the window — scrolling to collect it, and autoplay — always wait for the Start button.
/// </summary>
public sealed class IrisView : UserControl, IAgentView
{
    readonly TextBlock _looking = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _ask = new() { Tag = "Ask about this window…" };
    readonly WrapPanel _chips = new() { Margin = new Thickness(0, 9, 0, 0) };
    readonly Markdown _answer = new() { Margin = new Thickness(2, 0, 0, 0) };
    readonly ScrollViewer _answerScroll;
    readonly Border _job = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock _jobText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _jobSub = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
    readonly Button _jobBtn = new() { Content = "Start", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    nint _target;
    CancellationTokenSource? _cts;
    Func<CancellationToken, Task>? _pending;
    bool _hooked;

    public IrisView()
    {
        var root = new StackPanel { Margin = new Thickness(18, 2, 18, 14) };

        var eye = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 8, 0) };
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
        head.Children.Add(eye); head.Children.Add(_looking);
        root.Children.Add(head);

        _ask.Style = (Style)FindResource("Input");
        _ask.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Go(); } };
        var send = new Button { Content = "Ask", Style = (Style)FindResource("Primary"), Background = Agents.Iris.Gradient(), Padding = new Thickness(13, 5, 13, 6), VerticalAlignment = VerticalAlignment.Center };
        send.Click += (_, _) => Go();
        var askRow = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        askRow.Children.Add(send); askRow.Children.Add(_ask);
        _ask.VerticalAlignment = VerticalAlignment.Center; _ask.Margin = new Thickness(0, 0, 8, 0);
        root.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 9, 8, 9), Child = askRow });

        root.Children.Add(_chips);

        _jobBtn.Style = (Style)FindResource("Primary"); _jobBtn.Background = Agents.Iris.Gradient();
        _jobBtn.Click += (_, _) => JobClick();
        _jobSub.Foreground = (Brush)FindResource("Muted");
        var jobText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        jobText.Children.Add(_jobText); jobText.Children.Add(_jobSub);
        var jobRow = new DockPanel();
        DockPanel.SetDock(_jobBtn, Dock.Right);
        jobRow.Children.Add(_jobBtn); jobRow.Children.Add(jobText);
        _job.Child = jobRow; _job.Padding = new Thickness(12, 9, 9, 9); _job.CornerRadius = new CornerRadius(14);
        _job.Background = new SolidColorBrush(Color.FromArgb(0x16, 0x8F, 0xD3, 0xF4)); _job.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x8F, 0xD3, 0xF4)); _job.BorderThickness = new Thickness(1);
        root.Children.Add(_job);

        _answerScroll = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _answer, Padding = new Thickness(0, 0, 8, 0), Margin = new Thickness(0, 12, -8, 0), Visibility = Visibility.Collapsed };
        root.Children.Add(_answerScroll);
        Content = root;
    }

    public void Activate(string? intent, bool autoRun)
    {
        if (!_hooked) { _hooked = true; App.Mind.Sensor.ForegroundChanged += _ => Dispatcher.BeginInvoke(Retarget); }
        Retarget();
        Dispatcher.BeginInvoke(() => { _ask.Focus(); Keyboard.Focus(_ask); }, System.Windows.Threading.DispatcherPriority.Input);
        _ = App.Mind.Ollama.WarmAsync(App.Mind.Settings.ExplainerModel);
        if (string.IsNullOrWhiteSpace(intent)) return;
        // Spoken or typed requests that mean "do something to the window" only ever prepare the card; Start is still a click.
        if (Regex.IsMatch(intent, @"\b(copy|collect|scroll|index|save)\b.*\b(all|every|whole|entire|messages|chat|thread|page)\b|\bread through\b", RegexOptions.IgnoreCase))
            PrepareCollect(up: !Regex.IsMatch(intent, @"\b(down|below|rest of)\b", RegexOptions.IgnoreCase));
        else if (Regex.IsMatch(intent, @"\b(play|playing|keep (it |this )?going|game)\b", RegexOptions.IgnoreCase)) PrepareAutoplay();
        else { _ask.Text = intent; _ask.CaretIndex = intent.Length; if (autoRun) Go(); }
    }

    void Retarget()
    {
        if (_cts != null) return;                       // a job keeps the window it started on
        _target = App.Mind.Sensor.LastExternalWindow;
        bool ok = WindowReader.CanRead(_target);
        _looking.Text = ok ? $"Looking at  {WindowReader.Pretty(Native.ProcessName(_target))}  ·  {Gen.OneLine(Native.WindowTitle(_target), 60)}"
                           : _target != 0 && Native.IsWindow(_target) ? "That window looks private, so I won't read it." : "Open the window you want help with, then come back.";
        _looking.Foreground = (Brush)FindResource(ok ? "Muted" : "Faint");
        _chips.Children.Clear();
        if (!ok) return;
        Chip("Summarise", () => Ask("Summarise this in a few bullet points.", "Summarise this window"));
        Chip("What matters here?", () => Ask("What are the key points and anything I need to act on?", "What matters here?"));
        Chip("Save text to Notes", () => _ = SaveAsync());
        Chip("Collect it all ↑", () => PrepareCollect(true), "Scrolls up through the window and saves every line to Notes");
        Chip("Collect it all ↓", () => PrepareCollect(false), "Scrolls down through the window and saves every line to Notes");
        Chip("Autoplay", PrepareAutoplay, "Keeps a jump-over-things runner game going while you're away");
    }

    void Chip(string text, Action act, string? tip = null)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Chip"), ToolTip = tip };
        b.Click += (_, _) => act();
        _chips.Children.Add(b);
    }

    void Go()
    {
        var q = _ask.Text.Trim();
        if (q.Length == 0) return;
        _ask.Text = "";
        Ask(q, q);
    }

    async void Ask(string question, string shown)
    {
        if (_cts != null) return;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            _job.Visibility = Visibility.Collapsed;
            _answerScroll.Visibility = Visibility.Visible;
            _answer.Text = "*reading the window…*";
            var read = await WindowReader.ReadAsync(_target, 9000);
            if (read == null) { _answer.Text = "I couldn't read that window. Some apps don't expose their text; try selecting it and pressing **Alt + Shift + E**."; return; }
            var msgs = new List<ChatMsg> { new("system", Agents.IrisSystem), new("user", $"Window: {read.App} — {Gen.OneLine(read.Title, 80)}\n{Gen.AsContext(read.Text)}\n\n{question}") };
            _answer.Text = "*thinking…*";
            var r = await Gen.StreamAsync(Agents.Iris, App.Mind.Settings.ExplainerModel, msgs, t => { if (t.Length > 0) _answer.Text = $"**{shown}**\n\n{t}"; }, cts.Token, 0.4, 700);
            if (!r.Ok) _answer.Text = r.Error ?? "Something went wrong.";
        }
        finally { if (_cts == cts) _cts = null; }
    }

    async Task SaveAsync()
    {
        var read = await WindowReader.ReadAsync(_target, 60_000, timeoutMs: 4000);
        Result(read == null ? "I couldn't read that window." : $"Saved {read.Text.Split('\n').Length} lines from {WindowReader.Pretty(read.App)} to Notes. Find them under Echo.");
        if (read != null) App.Mind.Notes.Add("capture", Gen.OneLine(read.Title, 70), read.Text, read.App);
    }

    // ---------- jobs: prepared by a chip or a spoken request, started only by the Start button ----------

    void PrepareCollect(bool up)
    {
        if (!Guard()) return;
        string app = WindowReader.Pretty(Native.ProcessName(_target));
        Prepare($"Collect everything in {app} by scrolling {(up ? "up" : "down")}?",
            "I turn the mouse wheel over that window, read each screenful, and save the text to Notes. Nothing is typed or clicked. Esc stops it.",
            async ct =>
            {
                App.Mind.Halo.Status(Agents.Iris, "Collecting… Esc to stop");
                var text = await ScreenSkills.CollectAsync(_target, up, (lines, step) => Dispatcher.BeginInvoke(() => _jobText.Text = $"Collecting from {app}…  {lines} lines so far (screen {step})"), ct);
                App.Mind.Halo.HidePeek();
                if (text.Length == 0) { Result("I couldn't read any text from that window."); return; }
                App.Mind.Notes.Add("capture", Gen.OneLine(Native.WindowTitle(_target), 70), text, Native.ProcessName(_target));
                Result($"Saved {text.Split('\n').Length} lines from {app} to Notes. Open Echo to search or copy them.");
            });
    }

    void PrepareAutoplay()
    {
        if (!Guard()) return;
        string app = WindowReader.Pretty(Native.ProcessName(_target));
        Prepare($"Keep the runner game in {app} going?",
            "For side-scrolling jump games. I watch a thin strip ahead of the runner and tap Space when something is coming — nothing else. Stops on Esc, when you switch windows, or after 15 minutes.",
            async ct =>
            {
                App.Mind.Panel.Dismiss();
                App.Mind.Halo.Status(Agents.Iris, "Finding the game…");
                var r = await ScreenSkills.AutoplayAsync(_target, TimeSpan.FromMinutes(15), s => Dispatcher.BeginInvoke(() => App.Mind.Halo.Status(Agents.Iris, s)), ct);
                string done = r.Found ? $"Played for {(int)r.Played.TotalMinutes}:{r.Played.Seconds:00} — {r.Jumps} jumps, {r.Restarts + 1} run{(r.Restarts == 0 ? "" : "s")}. Stopped: {r.Why}." : $"Didn't start: {r.Why}.";
                App.Mind.Halo.Status(Agents.Iris, Gen.OneLine(done, 60), 6);
                Result(done);
            });
    }

    bool Guard()
    {
        if (_cts != null) return false;
        if (ScreenSkills.Allowed(_target)) return true;
        Result("I can't do that in this window (it is a terminal, a private window, or it has closed).");
        return false;
    }

    void Prepare(string text, string sub, Func<CancellationToken, Task> run)
    {
        _pending = run;
        _jobText.Text = text; _jobSub.Text = sub; _jobSub.Visibility = Visibility.Visible;
        _jobBtn.Content = "Start"; _jobBtn.Visibility = Visibility.Visible;
        _job.Visibility = Visibility.Visible;
        _answerScroll.Visibility = Visibility.Collapsed;
        _jobBtn.Focus();
    }

    async void JobClick()
    {
        if (_cts != null) { _cts.Cancel(); return; }
        if (_pending == null) return;
        var run = _pending; _pending = null;
        var cts = _cts = new CancellationTokenSource();
        _jobBtn.Content = "Stop"; _jobSub.Visibility = Visibility.Collapsed;
        App.Mind.Panel.SuppressDismiss = true;           // the panel stays up to show progress while the window is worked on
        try { await run(cts.Token); }
        catch (Exception ex) { Result("That didn't work: " + ex.Message); }
        finally { App.Mind.Panel.SuppressDismiss = false; _cts = null; Retarget(); }
    }

#if DEBUG
    public void DebugStart() => JobClick();
#endif

    void Result(string text)
    {
        _jobText.Text = text; _jobSub.Visibility = Visibility.Collapsed; _jobBtn.Visibility = Visibility.Collapsed;
        _job.Visibility = Visibility.Visible;
    }
}
