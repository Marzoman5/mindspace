using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Mindspace.Services;

namespace Mindspace.Views;

public partial class QuillView : UserControl, IAgentView
{
    static readonly (string label, string hint)[] ToneList =
    [
        ("Friendly", "Tone: friendly and natural."), ("Professional", "Tone: professional and polished."),
        ("Brief", "Tone: very brief, 2-3 sentences at most."), ("Warm", "Tone: warm and considerate."), ("Direct", "Tone: direct and to the point, no fluff."),
    ];
    static readonly string[] StarterList = ["Reply: yes, that works for me", "Politely decline", "Ask for a quick update", "Say thanks"];

    string _tone = ToneList[0].hint;
    string _context = "";
    string _lastAsk = "";
    readonly List<ChatMsg> _thread = [];
    CancellationTokenSource? _cts;
    WhenParser.Result? _promise;

    public QuillView()
    {
        InitializeComponent();
        SendBtn.Background = Agents.Quill.Gradient();
        InsertBtn.Background = Agents.Quill.Gradient();
        TickOrb.Agent = Agents.Tick;
        SetBusy(false);
        foreach (var (label, hint) in ToneList)
        {
            var b = new ToggleButton { Content = label, Style = (Style)FindResource("Chip"), IsChecked = hint == _tone };
            b.Click += (_, _) => { _tone = hint; foreach (ToggleButton t in Tones.Children) t.IsChecked = ReferenceEquals(t, b); };
            Tones.Children.Add(b);
        }
        foreach (var s in StarterList)
        {
            var b = new Button { Content = s, Style = (Style)FindResource("Chip"), Foreground = (System.Windows.Media.Brush)FindResource("Muted") };
            b.Click += (_, _) => { Ask.Text = s; Ask.CaretIndex = s.Length; _ = RunAsync(s); };
            Starters.Children.Add(b);
        }
    }

    public void Activate(string? intent, bool autoRun)
    {
        // Offer the copied text as something to reply to. It is only sent to the local model if the user presses send.
        var clip = Gen.ClipboardText();
        if (clip.Length >= 40 && clip != Out.Text.Trim() && !ContextSensor.IsPrivate(clip))
        {
            _context = clip;
            CtxText.Text = "Replying to what you copied:  “" + Gen.OneLine(clip, 70) + "”";
            CtxChip.Visibility = Visibility.Visible;
        }
        else { _context = ""; CtxChip.Visibility = Visibility.Collapsed; }

        var app = App.Mind.Sensor.LastExternalApp;
        InsertLabel.Text = app.Length > 0 ? $"Insert into {Pretty(app)}" : "Insert";
        Ask.Tag = _context.Length > 0 ? "How should I reply?  e.g. yes, but Wednesday 3pm" : "What do you want to say?  e.g. tell Sam I'll be 10 min late";
        if (!string.IsNullOrEmpty(intent)) { Ask.Text = intent; Ask.CaretIndex = intent.Length; }
        Dispatcher.BeginInvoke(() => { Ask.Focus(); Keyboard.Focus(Ask); }, System.Windows.Threading.DispatcherPriority.Input);
        _ = App.Mind.Ollama.WarmAsync(App.Mind.Settings.WriterModel);   // load while the user types
        if (autoRun && !string.IsNullOrEmpty(intent)) _ = RunAsync(intent);
    }

    static string Pretty(string proc) => proc.ToLowerInvariant() switch
    {
        "chrome" => "Chrome", "msedge" => "Edge", "firefox" => "Firefox", "olk" or "outlook" => "Outlook", "winword" => "Word",
        "code" => "VS Code", "slack" => "Slack", "ms-teams" or "teams" => "Teams", "notepad" => "Notepad", "discord" => "Discord",
        var p => char.ToUpper(p[0]) + (p.Length > 14 ? p[1..14] + "…" : p[1..]),
    };

    void Ask_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Send_Click(s, e); }
    }

    void Send_Click(object s, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        var ask = Ask.Text.Trim();
        if (ask.Length == 0 && _context.Length > 0) ask = "Write a helpful reply.";
        if (ask.Length > 0) _ = RunAsync(ask);
    }

    async Task RunAsync(string ask, string? refine = null)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var set = App.Mind.Settings;
        if (refine == null)
        {
            _lastAsk = ask;
            _thread.Clear();
            _thread.Add(new("system", Agents.QuillSystem(set.YourName.Trim())));
            _thread.Add(new("user", (_context.Length > 0 ? $"<context>\n{_context}\n</context>\n" : "") + ask + "\n" + _tone));
        }
        else
        {
            // keep system + original request, swap in the current (possibly hand-edited) draft, then the tweak
            while (_thread.Count > 2) _thread.RemoveAt(2);
            _thread.Add(new("assistant", Out.Text.Trim()));
            _thread.Add(new("user", refine + " Output only the rewritten message."));
        }

        Err.Visibility = Visibility.Collapsed; TickBar.Visibility = Visibility.Collapsed; Starters.Visibility = Visibility.Collapsed;
        OutCard.Visibility = Visibility.Visible; Actions.Visibility = Visibility.Collapsed;
        Out.Text = ""; Out.IsReadOnly = true;
        SetBusy(true);
        var r = await Gen.StreamAsync(Agents.Quill, set.WriterModel, _thread, t => { Out.Text = t; OutScroll.ScrollToEnd(); }, cts.Token, 0.65, 600);
        if (_cts != cts) return;
        _cts = null; SetBusy(false); Out.IsReadOnly = false;
        if (!r.Ok) { Err.Text = r.Error; Err.Visibility = Visibility.Visible; if (r.Text.Length == 0) OutCard.Visibility = Visibility.Collapsed; return; }
        Out.Text = Clean(r.Text, reply: _context.Length > 0);
        OutScroll.ScrollToTop();
        Actions.Visibility = Visibility.Visible;
        CopyBtn.Content = "Copy";

        // Did the user just promise something? Hand it to Tick (local regex; no model call).
        _promise = Commitments.Find(Out.Text) ?? Commitments.Find(ask);
        if (_promise != null)
        {
            TickText.Text = $"Remind you to “{Gen.OneLine(_promise.Title, 34)}”?";
            TickWhen.Text = _promise.Due is { } d ? WhenParser.Friendly(d) : "No time set";
            TickAdd.Content = "Remind me"; TickAdd.IsEnabled = true;
            TickBar.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Small models sometimes wrap the message in quotes or a code fence; strip that, never the content.</summary>
    static string Clean(string t, bool reply = false)
    {
        t = t.Trim();
        // an empty subject is never wanted, and a reply to copied text goes into an existing thread
        t = Regex.Replace(t, reply ? @"^Subject:[^\n]*\n\s*" : @"^Subject:[ \t]*\n\s*", "");
        if (t.StartsWith("```")) { int nl = t.IndexOf('\n'); if (nl > 0) t = t[(nl + 1)..]; if (t.EndsWith("```")) t = t[..^3]; }
        if (t.Length > 1 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        t = t.Replace("**", "").Trim();
        // never sign as the assistant, and keep a blank line after the subject
        t = Regex.Replace(t, @"\n\s*(Quill|\[[^\]]*name[^\]]*\])\s*$", "", RegexOptions.IgnoreCase).TrimEnd();
        t = Regex.Replace(t, @"^(Subject:[^\n]*)\n(?!\s*\n)", "$1\n\n");
        return t;
    }

    void SetBusy(bool busy)
    {
        SendBtn.Content = busy ? "" : "";
        SendBtn.ToolTip = busy ? "Stop" : "Draft it (Enter)";
    }

    void Insert_Click(object s, RoutedEventArgs e) => App.Mind.Panel.InsertText(Out.Text.Trim());

    void Copy_Click(object s, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Out.Text.Trim()); CopyBtn.Content = "Copied ✓"; } catch { }
    }

    void Refine_Click(object s, RoutedEventArgs e) => _ = RunAsync(_lastAsk, (string)((Button)s).Tag);
    void Retry_Click(object s, RoutedEventArgs e) { if (_lastAsk.Length > 0) _ = RunAsync(_lastAsk); }
    void CtxRemove_Click(object s, RoutedEventArgs e) { _context = ""; CtxChip.Visibility = Visibility.Collapsed; Ask.Tag = "What do you want to say?"; Ask.Focus(); }
    void TickDismiss_Click(object s, RoutedEventArgs e) => TickBar.Visibility = Visibility.Collapsed;

    void TickAdd_Click(object s, RoutedEventArgs e)
    {
        if (_promise == null) return;
        App.Mind.Reminders.Add(_promise.Title, _promise.Due);
        TickAdd.Content = "Added ✓"; TickAdd.IsEnabled = false;
    }
}
