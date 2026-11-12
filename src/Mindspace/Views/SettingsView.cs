using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace.Views;

public sealed class SettingsView : UserControl, IAgentView
{
    readonly StackPanel _root = new() { Margin = new Thickness(18, 2, 18, 14) };
    Settings S => App.Mind.Settings;
    int _pass;
    const string RunKey =@"Software\Microsoft\Windows\CurrentVersion\Run";

    public SettingsView()
    {
        Content = new ScrollViewer { MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root, Padding = new Thickness(0, 0, 8, 0), Margin = new Thickness(0, 0, -8, 0) };
    }

    public async void Activate(string? intent, bool autoRun)
    {
        _root.Children.Clear();
        _root.Children.Add(new TextBlock { Text = "Settings", FontFamily = (FontFamily)FindResource("Ui"), FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 2, 0, 10) });

        Section("Behaviour");
        Toggle("Agents offer help", "An agent may lean in when the app in front looks relevant (mail, docs…). Only the window title is checked, on this PC.", S.Suggestions, v => S.Suggestions = v);
        Toggle("Notice promises in copied text", "Off by default. When on, text you copy is checked by a local pattern (no AI, nothing saved) for things like “I'll send it Friday”.", S.ClipboardSensing, v => S.ClipboardSensing = v);
        Toggle("Tell me when the PC is struggling", "Patch drops in if CPU or memory stays very high.", S.HealthAlerts, v => S.HealthAlerts = v);
        Toggle("Bubble dock", "Floating dock instead of the taskbar. It drops out of the way when a window reaches the bottom of the screen. While it is on, the taskbar is hidden and set to auto-hide; both are put back when you turn this off or quit.", S.Dock, v => { S.Dock = v; App.Mind.ApplyDock(); });
        Toggle("Frosted glass", "See-through panels with the desktop blurred behind them. Applies the next time Mindspace starts.", S.Frost, v => S.Frost = v);
        Toggle("Start with Windows", "Adds Mindspace to your own startup list (current user only).", IsAutoStart(), SetAutoStart);

        Section("Voice and notes");
        Note(App.Mind.Speech.Installed
            ? "Hold Alt + Shift + D and speak: the words are typed where your cursor is. Start with an agent's name (“Tick, remind me…”) to talk to that agent. Speech is turned into text on this PC by whisper.cpp."
            : Speech.SetupHint);
        if (App.Mind.Speech.Installed && App.Mind.Speech.HasModel("small.en"))
            Choice("Speech model", ["base.en", "small.en"], S.SpeechModel, v => S.SpeechModel = v, "base.en answers in about a second; small.en is more accurate and takes about three times as long.");
        Toggle("Keep history", "Saves the text of dictations, notes and captures on this PC so Echo can list and search them. Audio is never saved.", S.KeepNotes, v => S.KeepNotes = v);
        var clear = new Button { Content = $"Delete all {App.Mind.Notes.Items.Count} saved items", Style = (Style)FindResource("Chip"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 4), IsEnabled = App.Mind.Notes.Items.Count > 0 };
        clear.Click += (_, _) => { App.Mind.Notes.Clear(); clear.Content = "Deleted"; clear.IsEnabled = false; };
        _root.Children.Add(clear);

        Section($"Your agents  ·  up to {Agents.MaxCustom}");
        foreach (var c in S.CustomAgents.Take(Agents.MaxCustom).ToList()) _root.Children.Add(AgentRow(c));
        if (_editing != null && !S.CustomAgents.Contains(_editing)) _root.Children.Add(AgentEditor(_editing, true));
        else if (S.CustomAgents.Count < Agents.MaxCustom)
        {
            var add = new Button { Content = "+  New agent", Style = (Style)FindResource("Chip"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 4) };
            add.Click += (_, _) => { _editing = new CustomAgent { Palette = S.CustomAgents.Count }; Activate(null, false); };
            _root.Children.Add(add);
        }

        Section("You");
        var name = new TextBox { Style = (Style)FindResource("Input"), Tag = "Your name, for email sign-offs", Text = S.YourName, FontSize = 13 };
        name.LostFocus += (_, _) => { S.YourName = name.Text.Trim(); S.Save(); };
        name.TextChanged += (_, _) => S.YourName = name.Text.Trim();
        _root.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 8, 12, 9), Child = name, Margin = new Thickness(0, 0, 0, 6) });

        Section("Models  ·  all run locally through Ollama");
        int pass = ++_pass;
        var models = await App.Mind.Ollama.ModelsAsync();
        if (pass != _pass) return;      // a newer Activate() is rebuilding the page
        Model(Agents.Quill, "writes", S.WriterModel, models, v => S.WriterModel = v);
        Model(Agents.Sage, "explains", S.ExplainerModel, models, v => S.ExplainerModel = v);
        Model(Agents.Tick, "reads odd dates", S.ReminderModel, models, v => S.ReminderModel = v);
        Model(Agents.Patch, "diagnoses", S.DiagnosisModel, models, v => S.DiagnosisModel = v);
        if (models.Count == 0) Note("Ollama isn't reachable, so the installed-model list is empty. Start Ollama and reopen settings.");

        Section("Privacy");
        Note("Mindspace talks to exactly one address: the Ollama server on this PC (127.0.0.1). No account, no telemetry, no cloud. " +
             "Agents can only produce text — they can't run commands, open files or change settings. Text reaches another app only when you press Insert or dictate it yourself. " +
             "Windows are read only when you ask, never private ones, and Iris scrolls or plays only after you press Start.");

        var quit = new Button { Content = "Quit Mindspace", Style = (Style)FindResource("Chip"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        quit.Click += (_, _) => App.Mind.Quit();
        _root.Children.Add(quit);
    }

    CustomAgent? _editing;
#if DEBUG
    public void DebugEdit() { _editing = S.CustomAgents.FirstOrDefault(); Activate(null, false); }
#endif

    FrameworkElement AgentRow(CustomAgent c)
    {
        if (ReferenceEquals(_editing, c)) return AgentEditor(c, false);
        var live = Agents.All.FirstOrDefault(a => a.Id == c.Id);
        var row = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
        var edit = new Button { Content = "Edit", Style = (Style)FindResource("Chip"), Margin = new Thickness(0), FontSize = 11.5, Padding = new Thickness(10, 3, 10, 4) };
        edit.Click += (_, _) => { _editing = c; Activate(null, false); };
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(edit);
        if (live != null) row.Children.Add(new AgentOrb { Agent = live, Width = 24, Height = 24 });
        var t = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = $"{c.Name}  ·  {(c.Role.Length > 0 ? c.Role : "Custom")}", FontSize = 13 });
        t.Children.Add(new TextBlock { Text = Gen.OneLine(c.Prompt, 60), FontSize = 11.5, Foreground = (Brush)FindResource("Faint") });
        row.Children.Add(t);
        return row;
    }

    /// <summary>An agent the user defines: a name, a face, what it should do, and when it may lean in. It can only write text.</summary>
    FrameworkElement AgentEditor(CustomAgent c, bool isNew)
    {
        var box = new StackPanel();
        TextBox Field(string placeholder, string value, bool multi = false)
        {
            var tb = new TextBox { Style = (Style)FindResource("Input"), Tag = placeholder, Text = value, FontSize = 12.5, AcceptsReturn = multi, MaxLength = multi ? 1500 : 60 };
            if (multi) { tb.MinHeight = 54; tb.MaxHeight = 120; tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            box.Children.Add(new Border { Background = (Brush)FindResource("Surface"), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 0, 6), Child = tb });
            return tb;
        }
        var name = Field("Name, e.g. Lingo", c.Name);
        name.MaxLength = 14;
        var role = Field("Role in one or two words, e.g. Translator", c.Role);
        role.MaxLength = 18;
        var prompt = Field("What should it do? e.g. Translate whatever I give you into Spanish and explain any idioms.", c.Prompt, true);
        var triggers = Field("Lean in when the window title contains… (optional, comma separated)", c.Triggers);

        int palette = c.Palette, mood = c.Mood;
        var faces = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
        void Faces()
        {
            faces.Children.Clear();
            for (int i = 0; i < Agents.Palettes.Length; i++)
            {
                int k = i;
                var orb = new AgentOrb { Agent = new Agent("preview", "", "", "", Agents.Palettes[i], Agents.Moods[mood % Agents.Moods.Length]), Width = 26, Height = 26, Margin = new Thickness(0, 0, 7, 0), Cursor = System.Windows.Input.Cursors.Hand, Opacity = i == palette ? 1 : 0.45, ToolTip = i == palette ? "Click again to change the face" : null };
                orb.MouseLeftButtonUp += (_, _) => { if (palette == k) mood = (mood + 1) % Agents.Moods.Length; palette = k; Faces(); };
                faces.Children.Add(orb);
            }
        }
        Faces();
        box.Children.Add(faces);

        var sees = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = c.SeesWindow, Margin = new Thickness(10, 0, 0, 0) };
        var seesRow = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(sees, Dock.Right);
        seesRow.Children.Add(sees);
        seesRow.Children.Add(new TextBlock { Text = "May read the window in front when I ask it to", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(seesRow);

        var acts = new WrapPanel { Margin = new Thickness(0, 0, 0, -6) };
        var save = new Button { Content = "Save", Style = (Style)FindResource("Primary"), Background = Agents.Sage.Gradient(), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(13, 5, 13, 6) };
        save.Click += (_, _) =>
        {
            if (name.Text.Trim().Length < 2 || prompt.Text.Trim().Length < 5) { name.Tag = "Give it a name and say what it should do"; return; }
            if (Agents.BuiltIn.Any(a => a.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase))) { name.Text = ""; name.Tag = "That name is taken by a built-in agent"; return; }
            c.Name = name.Text.Trim(); c.Role = role.Text.Trim(); c.Prompt = prompt.Text.Trim(); c.Triggers = triggers.Text.Trim(); c.SeesWindow = sees.IsChecked == true; c.Palette = palette; c.Mood = mood;
            if (isNew) S.CustomAgents.Add(c);
            _editing = null; S.Save(); Agents.Load(S); Activate(null, false);
        };
        acts.Children.Add(save);
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("Chip") };
        cancel.Click += (_, _) => { _editing = null; Activate(null, false); };
        acts.Children.Add(cancel);
        if (!isNew)
        {
            var del = new Button { Content = "Delete", Style = (Style)FindResource("Chip"), Foreground = (Brush)FindResource("Danger") };
            del.Click += (_, _) => { S.CustomAgents.Remove(c); _editing = null; S.Save(); Agents.Load(S); Activate(null, false); };
            acts.Children.Add(del);
        }
        box.Children.Add(acts);
        return new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(11, 10, 11, 10), Margin = new Thickness(0, 0, 0, 8), Child = box };
    }

    void Choice(string title, string[] options, string current, Action<string> set, string detail)
    {
        var box = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        foreach (var o in options) box.Items.Add(o);
        box.SelectedItem = options.Contains(current) ? current : options[0];
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string m) { set(m); S.Save(); } };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(0, 1, 8, 0), LineHeight = 16 });
        var dock = new DockPanel { Margin = new Thickness(2, 0, 0, 11) };
        DockPanel.SetDock(box, Dock.Right);
        dock.Children.Add(box); dock.Children.Add(text);
        _root.Children.Add(dock);
    }

    void Section(string title) => _root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Label"), Margin = new Thickness(2, 12, 0, 7) });

    void Note(string text) => _root.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(2, 0, 0, 4), LineHeight = 17 });

    void Toggle(string title, string detail, bool value, Action<bool> set)
    {
        var sw = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };
        sw.Click += (_, _) => { set(sw.IsChecked == true); S.Save(); };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(0, 1, 0, 0), LineHeight = 16 });
        var dock = new DockPanel { Margin = new Thickness(2, 0, 0, 11) };
        DockPanel.SetDock(sw, Dock.Right);
        dock.Children.Add(sw); dock.Children.Add(text);
        _root.Children.Add(dock);
    }

    void Model(Agent agent, string does, string current, List<string> models, Action<string> set)
    {
        var box = new ComboBox { Width = 170, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var m in models.Contains(current) ? models : models.Prepend(current)) box.Items.Add(m);
        box.SelectedItem = current;
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string m) { set(m); S.Save(); } };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new AgentOrb { Agent = agent, Width = 20, Height = 20 });
        left.Children.Add(new TextBlock { Text = $"{agent.Name}", Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(new TextBlock { Text = does, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Faint"), FontSize = 12 });
        var dock = new DockPanel { Margin = new Thickness(2, 0, 0, 7) };
        DockPanel.SetDock(box, Dock.Right);
        dock.Children.Add(box); dock.Children.Add(left);
        _root.Children.Add(dock);
    }

    static bool IsAutoStart()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Mindspace") != null; } catch { return false; }
    }

    static void SetAutoStart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on && Environment.ProcessPath is { } exe) k.SetValue("Mindspace", $"\"{exe}\"");
            else k.DeleteValue("Mindspace", false);
        }
        catch { }
    }
}

/// <summary>First-run card: who the agents are and what Mindspace will and won't do.</summary>
public sealed class WelcomeView : UserControl, IAgentView
{
    public WelcomeView()
    {
        var root = new StackPanel { Margin = new Thickness(20, 4, 20, 16) };
        root.Children.Add(new TextBlock { Text = "Welcome to your Mindspace", FontFamily = (FontFamily)FindResource("Ui"), FontSize = 21, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "Small helpers that live at the top of your screen and run entirely on this PC.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 3, 0, 14) });
        foreach (var a in Agents.BuiltIn)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(new AgentOrb { Agent = a, Width = 28, Height = 28 });
            var t = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = $"{a.Name}  ·  {a.Role}", FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
            t.Children.Add(new TextBlock { Text = a.Tagline, Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
            row.Children.Add(t);
            root.Children.Add(row);
        }
        root.Children.Add(new Border
        {
            Style = (Style)FindResource("Card"), Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(13, 10, 13, 11),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 18, Foreground = (Brush)FindResource("Muted"),
                Text = "Hover the sliver at the top of the screen, or press Ctrl + Alt + Space.\n" +
                       "Select text in any app, then Alt + Shift + E explains it and Alt + Shift + W drafts a reply.\n" +
                       "Hold Alt + Shift + D and speak to type with your voice, or say an agent's name first to talk to it.\n" +
                       "Nothing leaves this PC: the only connection is to the local model server. Agents write text; they can't run anything, " +
                       "and they read a window only when you ask. Watching the clipboard and replacing the taskbar are off until you switch them on in Settings.",
            },
        });
        var name = new TextBox { Style = (Style)FindResource("Input"), Tag = "Your first name (Quill signs emails with it)", FontSize = 13, Text = App.Mind.Settings.YourName };
        name.TextChanged += (_, _) => App.Mind.Settings.YourName = name.Text.Trim();
        root.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 8, 12, 9), Child = name, Margin = new Thickness(0, 0, 0, 12) });
        var go = new Button { Content = "Let's go", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Left, Background = Agents.Sage.Gradient() };
        go.Click += (_, _) => { App.Mind.Settings.Welcomed = true; App.Mind.Settings.Save(); App.Mind.Panel.Open("sage"); };
        root.Children.Add(go);
        Content = root;
    }
    public void Activate(string? intent, bool autoRun) { }
}
