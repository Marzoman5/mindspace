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
    static readonly string[] Ideas = ["How does compound interest work?", "What is an API, simply?", "Why is the sky blue?"];

    public SageView()
    {
        InitializeComponent();
        SendBtn.Background = Agents.Sage.Gradient();
        SetBusy(false);
    }

    public void Activate(string? intent, bool autoRun)
    {
        _clip = Gen.ClipboardText(4000);
        if (ContextSensor.IsPrivate(_clip)) _clip = "";
        if (_cts == null) ShowChips(_history.Count == 0 ? "start" : "follow");
        if (!string.IsNullOrEmpty(intent)) { Ask.Text = intent; Ask.CaretIndex = intent.Length; }
        Dispatcher.BeginInvoke(() => { Ask.Focus(); Keyboard.Focus(Ask); }, System.Windows.Threading.DispatcherPriority.Input);
        _ = App.Mind.Ollama.WarmAsync(App.Mind.Settings.ExplainerModel);
        if (autoRun && !string.IsNullOrEmpty(intent)) { Ask.Text = ""; _ = RunAsync(intent, intent); }
    }

    void ShowChips(string mode)
    {
        Chips.Children.Clear();
        if (mode == "start")
        {
            if (_clip.Length >= 12)
                AddChip("Explain what I copied  ·  “" + Gen.OneLine(_clip, 26) + "”", () => _ = RunAsync($"Explain this:\n<context>\n{_clip}\n</context>", "Explain what I copied"), true);
            foreach (var idea in Ideas) AddChip(idea, () => _ = RunAsync(idea, idea));
        }
        else if (mode == "follow")
        {
            AddChip("Simpler", () => _ = RunAsync("Explain that again, much simpler, like I'm new to this. Keep it short.", "Simpler, please"));
            AddChip("Go deeper", () => _ = RunAsync("Go one level deeper on that. Stay clear and structured.", "Go deeper"));
            AddChip("Give an example", () => _ = RunAsync("Give me one concrete, real-world example of that.", "Give me an example"));
            AddChip("Copy", () => { try { Clipboard.SetText(_history.LastOrDefault(m => m.Role == "assistant")?.Content ?? ""); } catch { } });
        }
    }

    void AddChip(string text, Action act, bool accent = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Chip") };
        if (accent) b.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0x7F, 0x7F, 0xD5));
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

    async Task RunAsync(string prompt, string shown)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Empty.Visibility = Visibility.Collapsed; Scroll.Visibility = Visibility.Visible; NewBtn.Visibility = Visibility.Visible;
        Chips.Children.Clear();

        // user bubble
        Thread.Children.Add(new Border
        {
            Background = (Brush)FindResource("SurfaceHi"), CornerRadius = new CornerRadius(15, 15, 4, 15), Padding = new Thickness(12, 7, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(50, 6, 0, 10),
            Child = new TextBlock { Text = shown, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 },
        });
        var answer = new Markdown { Margin = new Thickness(2, 0, 0, 10) };
        var dots = new TextBlock { Text = "thinking…", Foreground = (Brush)FindResource("Faint"), FontSize = 12, Margin = new Thickness(2, 0, 0, 10) };
        Thread.Children.Add(dots);
        Scroll.ScrollToEnd();

        var msgs = new List<ChatMsg> { new("system", Agents.SageSystem) };
        msgs.AddRange(_history.TakeLast(6));          // short memory keeps the context (and VRAM) small
        msgs.Add(new("user", prompt));
        SetBusy(true);
        bool swapped = false;
        var r = await Gen.StreamAsync(Agents.Sage, App.Mind.Settings.ExplainerModel, msgs, t =>
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
        _history.Add(new("user", prompt));
        _history.Add(new("assistant", r.Text));
        if (_cts == null) ShowChips("follow");
        Scroll.ScrollToEnd();
    }

    void SetBusy(bool busy)
    {
        SendBtn.Content = busy ? "" : "";
        SendBtn.ToolTip = busy ? "Stop" : "Ask (Enter)";
    }

    void New_Click(object s, RoutedEventArgs e)
    {
        _cts?.Cancel(); _cts = null; SetBusy(false);
        _history.Clear(); Thread.Children.Clear();
        Scroll.Visibility = Visibility.Collapsed; Empty.Visibility = Visibility.Visible; NewBtn.Visibility = Visibility.Collapsed;
        ShowChips("start");
        Ask.Focus();
    }
}
