using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace.Views;

public partial class SageView : UserControl, IAgentView
{
    readonly List<ChatMsg> _history = [];
    CancellationTokenSource? _cts;
    string _clip = "";
    int _epoch;
    static readonly string[] Ideas = ["How does compound interest work?", "What is an API, simply?", "Why is the sky blue?"];

    readonly Agent _agent;
    string SystemPrompt => _agent.IsCustom ? Agents.CustomSystem(_agent) : Agents.SageSystem;
    bool SeesWindow => !_agent.IsCustom || _agent.SeesWindow;

    public SageView() : this(Agents.Sage) { }

    /// <summary>Sage, or one of the user's own agents: same conversation, different instructions.</summary>
    public SageView(Agent agent)
    {
        _agent = agent;
        InitializeComponent();
        SendBtn.Background = agent.Gradient();
        if (agent.IsCustom)
        {
            EmptyTitle.Text = agent.Name;
            EmptySub.Text = agent.Prompt.Length > 0 ? Gen.OneLine(agent.Prompt, 110) : "No instructions yet — add some in Settings.";
            Ask.Tag = $"Ask {agent.Name}…";
        }
        SetBusy(false);
    }

    public void Activate(string? intent, bool autoRun)
    {
        _clip = Gen.ClipboardText(4000);
        if (ContextSensor.IsPrivate(_clip)) _clip = "";
        if (_cts == null) ShowChips(_history.Count == 0 ? "start" : "follow");
        if (intent != null && intent.StartsWith("@selection:"))
        {
            // Alt+Shift+E on selected text: explain it straight away.
            var sel = intent["@selection:".Length..];
            intent = null; autoRun = false;
            if (sel.Length >= 2) _ = RunAsync((_agent.IsCustom ? "Here is the text I selected:\n" : "Explain this:\n") + Gen.AsContext(sel), (_agent.IsCustom ? "“" : "Explain  “") + Gen.OneLine(sel, 80) + "”");
        }
        if (!string.IsNullOrEmpty(intent)) { Ask.Text = intent; Ask.CaretIndex = intent.Length; }
        Dispatcher.BeginInvoke(() => { Ask.Focus(); Keyboard.Focus(Ask); }, System.Windows.Threading.DispatcherPriority.Input);
        _ = App.Mind.Ollama.WarmAsync(App.Mind.Settings.ExplainerModel);
        if (autoRun && intent != null && SeesWindow && AboutScreen(intent)) { Ask.Text = ""; _ = AskAboutWindowAsync(intent, intent); return; }
        if (autoRun && !string.IsNullOrEmpty(intent)) { Ask.Text = ""; _ = RunAsync(intent, intent); }
    }

    void ShowChips(string mode)
    {
        Chips.Children.Clear();
        if (mode == "start")
        {
            var win = App.Mind.Sensor.LastExternalWindow;
            if (SeesWindow && WindowReader.CanRead(win))
            {
                string app = WindowReader.Pretty(App.Mind.Sensor.LastExternalApp);
                AddChip((_agent.IsCustom ? "Use what's open in " : "Explain what's open in ") + app,
                    () => _ = AskAboutWindowAsync(_agent.IsCustom ? "Apply your instructions to this window." : "Explain what this is about, briefly.", (_agent.IsCustom ? "Use what's open in " : "Explain what's open in ") + app), true);
            }
            if (_clip.Length >= 12)
                AddChip((_agent.IsCustom ? "Use what I copied  ·  “" : "Explain what I copied  ·  “") + Gen.OneLine(_clip, 22) + "”", () => _ = RunAsync((_agent.IsCustom ? "Here is what I copied:\n" : "Explain this:\n") + Gen.AsContext(_clip), "What I copied"));
            if (!_agent.IsCustom) foreach (var idea in Ideas) AddChip(idea, () => _ = RunAsync(idea, idea));
        }
        else if (mode == "follow")
        {
            if (_agent.IsCustom)
            {
                // a custom agent's follow-ups are whatever the user types; only Copy is universal
                AddChip("Copy", () => { try { Clipboard.SetText(_history.LastOrDefault(m => m.Role == "assistant")?.Content ?? ""); } catch { } });
                return;
            }
            AddChip("Simpler", () => _ = RunAsync("Explain that again, much simpler, like I'm new to this. Keep it short.", "Simpler, please"));
            AddChip("Go deeper", () => _ = RunAsync("Go one level deeper on that. Stay clear and structured.", "Go deeper"));
            AddChip("Give an example", () => _ = RunAsync("Give me one concrete, real-world example of that.", "Give me an example"));
            AddChip("Copy", () => { try { Clipboard.SetText(_history.LastOrDefault(m => m.Role == "assistant")?.Content ?? ""); } catch { } });
        }
    }

    /// <summary>"what does this page say", "summarise this window": the question is about what is on screen.</summary>
    static bool AboutScreen(string q) => System.Text.RegularExpressions.Regex.IsMatch(q, @"\b(this|the|my) (window|page|screen|site|article|email|message|chat|thread|document|doc|tab|pdf)\b|\bon (my |the )?screen\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Reads the window the user was in (accessibility text, nothing stored) and asks about it.</summary>
    async Task AskAboutWindowAsync(string question, string shown)
    {
        var read = await WindowReader.ReadAsync(App.Mind.Sensor.LastExternalWindow, 7000);
        if (read == null) { await RunAsync(question, shown, "I couldn't read that window — it may not expose its text. Select the text and press Alt+Shift+E instead."); return; }
        await RunAsync(question + "\n" + Gen.AsContext(read.Text), shown);
    }

    void AddChip(string text, Action act, bool accent = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Chip") };
        if (accent) b.BorderBrush = new SolidColorBrush(Color.FromArgb(150, _agent.Accent.R, _agent.Accent.G, _agent.Accent.B));
        else if (_history.Count == 0) b.Foreground = (Brush)FindResource("Muted");
        b.Click += (_, _) => act();
        Chips.Children.Add(b);
    }

    void Ask_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Send_Click(s, e); }
    }

    void Send_Click(object s, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        var q = Ask.Text.Trim();
        if (q.Length == 0) return;
        Ask.Text = "";
        _ = RunAsync(q, q);
    }

    async Task RunAsync(string prompt, string shown, string? failure = null)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int epoch = _epoch;
        Empty.Visibility = Visibility.Collapsed; Scroll.Visibility = Visibility.Visible; NewBtn.Visibility = Visibility.Visible;
        Chips.Children.Clear();

        // user bubble
        var bubble = new Border
        {
            Background = (Brush)FindResource("SurfaceHi"), CornerRadius = new CornerRadius(15, 15, 4, 15), Padding = new Thickness(12, 7, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(50, 6, 0, 10),
            Child = new TextBlock { Text = shown, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 },
        };
        Thread.Children.Add(bubble);
        var answer = new Markdown { Margin = new Thickness(2, 0, 0, 10) };
        var dots = new TextBlock { Text = "thinking…", Foreground = (Brush)FindResource("Faint"), FontSize = 12, Margin = new Thickness(2, 0, 0, 10) };
        Thread.Children.Add(dots);
        Scroll.ScrollToEnd();

        if (failure != null)
        {
            Thread.Children.Remove(dots);
            Thread.Children.Add(new TextBlock { Text = failure, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(2, 0, 0, 10) });
            _cts = null; ShowChips(_history.Count == 0 ? "start" : "follow");
            return;
        }
        var msgs = new List<ChatMsg> { new("system", SystemPrompt) };
        msgs.AddRange(_history.TakeLast(6));          // short memory keeps the context (and VRAM) small
        msgs.Add(new("user", prompt));
        SetBusy(true);
        bool swapped = false;
        var r = await Gen.StreamAsync(_agent, App.Mind.Settings.ExplainerModel, msgs, t =>
        {
            if (!swapped && t.Length > 0) { swapped = true; int i = Thread.Children.IndexOf(dots); Thread.Children.RemoveAt(i); Thread.Children.Insert(i, answer); }
            answer.Text = t;
            Scroll.ScrollToEnd();
        }, cts.Token, 0.5, 800);
        if (_cts == cts) { _cts = null; SetBusy(false); }
        if (!swapped) Thread.Children.Remove(dots);
        if (!r.Ok)
        {
            Thread.Children.Add(new TextBlock { Text = r.Error, Foreground = (Brush)FindResource("Danger"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(2, 0, 0, 10) });
            Scroll.ScrollToEnd();
            return;
        }
        if (epoch != _epoch) return;                 // the conversation was cleared while this answer was streaming
        _history.Add(new("user", prompt));
        _history.Add(new("assistant", r.Text));
        if (_cts == null) ShowChips("follow");
        ScrollTo(bubble);
    }

    /// <summary>When an answer is complete, put its question at the top so the user reads from the start.</summary>
    void ScrollTo(FrameworkElement el)
    {
        Scroll.UpdateLayout();
        double y = el.TranslatePoint(new Point(0, 0), Thread).Y;
        Scroll.ScrollToVerticalOffset(Math.Max(0, y - 2));
    }

    void SetBusy(bool busy)
    {
        SendBtn.Content = busy ? "" : "";
        SendBtn.ToolTip = busy ? "Stop" : "Ask (Enter)";
    }

    void New_Click(object s, RoutedEventArgs e)
    {
        _cts?.Cancel(); _cts = null; SetBusy(false);
        _epoch++;
        _history.Clear(); Thread.Children.Clear();
        Scroll.Visibility = Visibility.Collapsed; Empty.Visibility = Visibility.Visible; NewBtn.Visibility = Visibility.Collapsed;
        ShowChips("start");
        Ask.Focus();
    }
}
