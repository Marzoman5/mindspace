using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Mindspace.Services;

namespace Mindspace.Views;

public partial class TickView : UserControl, IAgentView
{
    ReminderStore Store => App.Mind.Reminders;
    bool _busy;

    public TickView()
    {
        InitializeComponent();
        AddBtn.Background = Agents.Tick.Gradient();
        Store.Changed += () => Dispatcher.Invoke(Rebuild);
        Rebuild();
    }

    public void Activate(string? intent, bool autoRun)
    {
        if (!string.IsNullOrEmpty(intent)) { Ask.Text = intent; Ask.CaretIndex = intent.Length; }
        Rebuild();
        Dispatcher.BeginInvoke(() => { Ask.Focus(); Keyboard.Focus(Ask); }, System.Windows.Threading.DispatcherPriority.Input);
        if (autoRun && !string.IsNullOrEmpty(intent)) Add_Click(this, new RoutedEventArgs());
    }

    void Ask_Changed(object s, TextChangedEventArgs e)
    {
        var t = Ask.Text.Trim();
        if (t.Length < 3) { Preview.Visibility = Visibility.Collapsed; return; }
        var r = WhenParser.Parse(t);
        PreviewText.Text = r.Due is { } d ? $"{WhenParser.Friendly(d)}  ·  {r.Title}" : "No time yet — add “tomorrow 9am”, “in 20 min”, “friday”…";
        PreviewText.Opacity = r.Due != null ? 1 : 0.55;
        Preview.Visibility = Visibility.Visible;
    }

    void Ask_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Add_Click(s, e); }
    }

    async void Add_Click(object s, RoutedEventArgs e)
    {
        var text = Ask.Text.Trim();
        if (text.Length < 2 || _busy) return;
        var r = WhenParser.Parse(text);

        // Rules handle the common phrasing instantly. Only when they find no time but the text clearly
        // contains one ("a week from today", "the 3rd"…) do we wake the small model.
        if (!r.FoundTime && LooksTimed(text))
        {
            _busy = true; PreviewText.Text = "Working out when…"; PreviewText.Opacity = 1; Preview.Visibility = Visibility.Visible;
            r = await AskModelAsync(text) ?? r;
            _busy = false;
        }
        Store.Add(r.Title.Length > 0 ? r.Title : text, r.Due);
        Ask.Text = ""; Preview.Visibility = Visibility.Collapsed;
    }

    static bool LooksTimed(string t) => System.Text.RegularExpressions.Regex.IsMatch(t,
        @"\b(\d|week|month|day|hour|minute|tonight|later|soon|noon|morning|evening|weekend|next|after|before|until|end of)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static async Task<WhenParser.Result?> AskModelAsync(string text)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var now = DateTime.Now;
            var json = await App.Mind.Ollama.CompleteAsync(App.Mind.Settings.ReminderModel,
                [new("system", Agents.TickSystem), new("user", WhenParser.CalendarHint(now) + "\nText: " + text)], 0, 80, WhenParser.Schema, cts.Token);
            var node = JsonNode.Parse(json);
            var title = node?["title"]?.GetValue<string>()?.Trim() ?? "";
            var dueText = node?["due"]?.GetValue<string>() ?? "";
            DateTime? due = DateTime.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) && d > now.AddMinutes(-1) && d < now.AddYears(3) ? d : null;
            // The model's output is data: a title string and a date we validated. Nothing else is accepted.
            if (title.Length is 0 or > 120) title = WhenParser.CleanTitle(text);
            return new WhenParser.Result(title, due, due != null);
        }
        catch { return null; }
    }

    void Rebuild()
    {
        List.Children.Clear();
        var now = DateTime.Now;
        var open = Store.Items.Where(r => !r.Done).OrderBy(r => r.Due ?? DateTime.MaxValue).ThenBy(r => r.Created).ToList();
        var done = Store.Items.Where(r => r.Done).ToList();
        foreach (var r in open.Concat(done.TakeLast(3))) List.Children.Add(Row(r, now));
        EmptyText.Visibility = Store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DoneBar.Visibility = done.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DoneText.Text = $"{done.Count} done";
    }

    FrameworkElement Row(Reminder r, DateTime now)
    {
        bool overdue = !r.Done && r.Due is { } d && d < now;
        var check = new Grid { Width = 20, Height = 20, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 11, 0), Background = Brushes.Transparent };
        check.Children.Add(new Ellipse { Stroke = r.Done ? Brushes.Transparent : (Brush)FindResource("Faint"), StrokeThickness = 1.5, Fill = r.Done ? Agents.Tick.Gradient() : Brushes.Transparent });
        if (r.Done) check.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(11, 42, 42)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        check.MouseLeftButtonUp += (_, _) => Store.Toggle(r);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = r.Title, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource(r.Done ? "Faint" : "Ink"), TextDecorations = r.Done ? TextDecorations.Strikethrough : null,
        });
        if (r.Due is { } due && !r.Done)
            text.Children.Add(new TextBlock { Text = (overdue ? "Overdue · " : "") + WhenParser.Friendly(due), FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0), Foreground = overdue ? (Brush)FindResource("Danger") : new SolidColorBrush(Color.FromRgb(0x38, 0xF9, 0xD7)) });

        var del = new Button { Style = (Style)FindResource("IconBtn"), Content = "", FontSize = 9, Width = 22, Height = 22, Opacity = 0, ToolTip = "Remove", VerticalAlignment = VerticalAlignment.Top };
        del.Click += (_, _) => Store.Remove(r);

        var dock = new DockPanel();
        DockPanel.SetDock(check, Dock.Left); DockPanel.SetDock(del, Dock.Right);
        dock.Children.Add(check); dock.Children.Add(del); dock.Children.Add(text);
        var row = new Border { Child = dock, Padding = new Thickness(8, 8, 6, 8), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent };
        row.MouseEnter += (_, _) => { row.Background = (Brush)FindResource("Surface"); del.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; del.Opacity = 0; };
        return row;
    }

    void Clear_Click(object s, RoutedEventArgs e)
    {
        Store.Items.RemoveAll(r => r.Done);
        Store.Save();
    }
}
