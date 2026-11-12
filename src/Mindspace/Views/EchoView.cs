using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace.Views;

/// <summary>
/// Echo: voice notes and the history of everything you dictated or captured. While a note is recording the words
/// appear as they are spoken and, if "AI bullets" is on, the local model keeps a short list of key points beside them.
/// Only text is kept; audio lives in memory until it has been transcribed and is then discarded.
/// </summary>
public sealed class EchoView : UserControl, IAgentView
{
    readonly Button _rec = new() { Padding = new Thickness(15, 7, 15, 8), VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _state = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly ToggleButton _ai = new() { Content = "AI bullets", IsChecked = true, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Let the local model keep a short list of key points while you talk" };
    readonly Border _liveCard = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    readonly Markdown _bullets = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _live = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 18 };
    readonly TextBox _search = new() { Tag = "Search notes, dictations and captures…", FontSize = 12.5 };
    readonly StackPanel _list = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly StringBuilder _text = new();
    DateTime _started;
    bool _recording, _busy;
    int _bulletsAt;
    string _bulletText = "";
    CancellationTokenSource? _bulletCts;
    string? _open;

    public EchoView()
    {
        var root = new StackPanel { Margin = new Thickness(18, 2, 18, 14) };

        _rec.Style = (Style)FindResource("Primary"); _rec.Background = Agents.Echo.Gradient();
        _rec.Click += (_, _) => { if (_recording) _ = StopAsync(); else Start(); };
        _ai.Style = (Style)FindResource("Chip");
        _state.Foreground = (Brush)FindResource("Muted");
        var top = new DockPanel();
        DockPanel.SetDock(_ai, Dock.Right);
        top.Children.Add(_ai); top.Children.Add(_rec); top.Children.Add(_state);
        root.Children.Add(top);

        var live = new StackPanel();
        live.Children.Add(_bullets);
        live.Children.Add(_live);
        _liveCard.Style = (Style)FindResource("Card");
        _liveCard.Child = new ScrollViewer { MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = live, Padding = new Thickness(0, 0, 8, 0), Margin = new Thickness(0, 0, -8, 0) };
        root.Children.Add(_liveCard);

        _search.Style = (Style)FindResource("Input");
        _search.TextChanged += (_, _) => Rebuild();
        root.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 12, 0, 8), Child = _search });
        root.Children.Add(new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(0, 0, 8, 0), Margin = new Thickness(0, 0, -8, 0) });
        Content = root;

        _tick.Tick += (_, _) => Tick();
        App.Mind.Notes.Changed += () => Dispatcher.BeginInvoke(Rebuild);
        Idle();
    }

    public void Activate(string? intent, bool autoRun)
    {
        if (!_recording) Idle();
        Rebuild();
        if (autoRun && !_recording && intent != null && System.Text.RegularExpressions.Regex.IsMatch(intent, @"\b(note|notes|record|listen|meeting)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) Start();
        else if (!string.IsNullOrEmpty(intent)) { _search.Text = intent; }
    }

    void Idle()
    {
        _rec.Content = "●  Start a note";
        _state.Text = !App.Mind.Speech.Installed ? Speech.SetupHint
            : "Or hold Alt + Shift + D anywhere and speak: your words are typed where the cursor is.";
        _rec.IsEnabled = App.Mind.Speech.Installed;
    }

    // ---------- recording a note ----------

    void Start()
    {
        if (App.Mind.Halo.IsDictating || App.Mind.Mic.IsRecording) { _state.Text = "The microphone is busy with a dictation."; return; }
        if (!App.Mind.Mic.Start()) { _state.Text = "I can't find a microphone."; return; }
        _recording = true; _started = DateTime.Now; _text.Clear(); _bulletsAt = 0; _bulletText = "";
        _live.Text = ""; _bullets.Text = ""; _bullets.Visibility = Visibility.Collapsed;
        _liveCard.Visibility = Visibility.Visible;
        _rec.Content = "■  Stop and save";
        _tick.Start();
        Tick();
    }

    async void Tick()
    {
        if (!_recording) return;
        var mic = App.Mind.Mic;
        var t = DateTime.Now - _started;
        _state.Text = $"Recording  {(int)t.TotalMinutes}:{t.Seconds:00}   " + new string('•', 1 + (int)(Math.Min(1, mic.Level * 3) * 9));
        if (!App.Mind.Panel.IsOpen) App.Mind.Halo.Status(Agents.Echo, $"Taking notes  {(int)t.TotalMinutes}:{t.Seconds:00}  ·  click to open");
        // Hand the model a piece whenever the speaker pauses, or every 14 s if they don't.
        if (_busy || !(mic.Seconds >= 14 || mic.Seconds >= 4 && mic.Level < 0.02)) return;
        await TranscribeAsync(mic.Take());
        if (_recording && _ai.IsChecked == true && _text.Length - _bulletsAt > 320) _ = BulletsAsync();
        if (t.TotalMinutes >= 90) _ = StopAsync();          // a forgotten recording does not run all day
    }

    async Task TranscribeAsync(byte[] pcm)
    {
        _busy = true;
        try
        {
            var piece = await App.Mind.Speech.TranscribeAsync(pcm);
            if (piece.Length > 0) { _text.Append(_text.Length > 0 ? " " : "").Append(piece); _live.Text = _text.ToString(); }
        }
        catch (Exception ex) { _state.Text = ex.Message; }
        finally { _busy = false; }
    }

    async Task BulletsAsync()
    {
        _bulletCts?.Cancel();
        var cts = _bulletCts = new CancellationTokenSource();
        _bulletsAt = _text.Length;
        string all = _text.ToString();
        var msgs = new List<ChatMsg> { new("system", Agents.EchoSystem), new("user", Gen.AsContext(all.Length > 7000 ? all[^7000..] : all)) };
        var r = await Gen.StreamAsync(Agents.Echo, App.Mind.Settings.ExplainerModel, msgs, _ => { }, cts.Token, 0.2, 380);
        if (cts.IsCancellationRequested || !r.Ok || r.Text.Length == 0) return;
        _bulletText = r.Text;
        _bullets.Text = r.Text; _bullets.Visibility = Visibility.Visible;
    }

    async Task StopAsync()
    {
        if (!_recording) return;
        _recording = false; _tick.Stop();
        _rec.IsEnabled = false; _state.Text = "Finishing…";
        App.Mind.Halo.HidePeek();
        for (int i = 0; i < 100 && _busy; i++) await Task.Delay(50);      // let the piece in flight finish
        await TranscribeAsync(App.Mind.Mic.Stop());
        string text = _text.ToString().Trim();
        if (text.Length == 0) { _liveCard.Visibility = Visibility.Collapsed; Idle(); _state.Text = "Nothing was said, so nothing was saved."; return; }
        if (_ai.IsChecked == true) { _state.Text = "Writing the key points…"; await BulletsAsync(); }
        var note = App.Mind.Notes.Add("note", Gen.OneLine(text, 60), text, "", _bulletText);
        _open = note.Id;
        _liveCard.Visibility = Visibility.Collapsed;
        Idle();
        _state.Text = App.Mind.Settings.KeepNotes ? "Saved below." : "Kept until you quit (history is off in Settings).";
        Rebuild();
    }

    // ---------- history ----------

    void Rebuild()
    {
        _list.Children.Clear();
        var q = _search.Text.Trim();
        var items = (q.Length == 0 ? App.Mind.Notes.Items : App.Mind.Notes.Search(q)).Take(40).ToList();
        if (items.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = q.Length > 0 ? "Nothing matches." : "Notes you record, things you dictate and text Iris saves will be listed here.", Foreground = (Brush)FindResource("Faint"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) });
            return;
        }
        foreach (var n in items) _list.Children.Add(Row(n));
    }

    FrameworkElement Row(Note n)
    {
        bool open = _open == n.Id;
        var kind = new TextBlock { Text = n.Kind switch { "dictation" => "", "capture" => "", _ => "" }, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        var title = new TextBlock { Text = n.Title.Length > 0 ? n.Title : "(untitled)", FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        string where = n.Kind switch { "dictation" => "Dictated" + (n.App.Length > 0 ? " into " + WindowReader.Pretty(n.App) : ""), "capture" => "Saved from " + WindowReader.Pretty(n.App), _ => "Note" };
        var meta = new TextBlock { Text = $"{where}  ·  {WhenParser.Friendly(n.Created)}", FontSize = 10.5, Foreground = (Brush)FindResource("Faint") };
        var text = new StackPanel();
        text.Children.Add(title); text.Children.Add(meta);
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.Children.Add(kind); head.Children.Add(text);
        head.MouseLeftButtonUp += (_, _) => { _open = open ? null : n.Id; Rebuild(); };
        var body = new StackPanel();
        body.Children.Add(head);
        if (open)
        {
            if (n.Bullets.Length > 0) body.Children.Add(new Markdown { Text = n.Bullets, Margin = new Thickness(0, 9, 0, 0) });
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0),
                Content = new TextBlock { Text = n.Text.Length > 4000 ? n.Text[..4000] + "…" : n.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 17, Foreground = (Brush)FindResource("Muted") },
            });
            var acts = new WrapPanel { Margin = new Thickness(0, 9, 0, -6) };
            acts.Children.Add(Act("Copy", b => { try { Clipboard.SetText(n.Bullets.Length > 0 ? n.Bullets + "\n\n" + n.Text : n.Text); b.Content = "Copied ✓"; } catch { } }));
            if (n.Bullets.Length == 0 && n.Text.Length > 120) acts.Children.Add(Act("Key points", async b =>
            {
                b.IsEnabled = false; b.Content = "Thinking…";
                var msgs = new List<ChatMsg> { new("system", Agents.EchoSystem), new("user", Gen.AsContext(n.Text.Length > 7000 ? n.Text[..7000] : n.Text)) };
                var r = await Gen.StreamAsync(Agents.Echo, App.Mind.Settings.ExplainerModel, msgs, _ => { }, CancellationToken.None, 0.2, 380);
                if (r.Ok && r.Text.Length > 0) { n.Bullets = r.Text; App.Mind.Notes.Update(); } else { b.Content = "Key points"; b.IsEnabled = true; }
            }));
            acts.Children.Add(Act("Delete", _ => App.Mind.Notes.Remove(n)));
            body.Children.Add(acts);
        }
        return new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 8, 12, 9), CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 0, 6), Child = body };
    }

    Button Act(string text, Action<Button> act)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Chip"), FontSize = 11.5, Padding = new Thickness(10, 3, 10, 4) };
        b.Click += (_, _) => act(b);
        return b;
    }
}
