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
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

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
        Toggle("Bubble dock", "Floating dock instead of the taskbar. The taskbar is only hidden while Mindspace runs and returns when you turn this off or quit.", S.Dock, v => { S.Dock = v; App.Mind.ApplyDock(); });
        Toggle("Start with Windows", "Adds Mindspace to your own startup list (current user only).", IsAutoStart(), SetAutoStart);

        Section("You");
        var name = new TextBox { Style = (Style)FindResource("Input"), Tag = "Your name, for email sign-offs", Text = S.YourName, FontSize = 13 };
        name.LostFocus += (_, _) => { S.YourName = name.Text.Trim(); S.Save(); };
        name.TextChanged += (_, _) => S.YourName = name.Text.Trim();
        _root.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 8, 12, 9), Child = name, Margin = new Thickness(0, 0, 0, 6) });

        Section("Models  ·  all run locally through Ollama");
        var models = await App.Mind.Ollama.ModelsAsync();
        Model(Agents.Quill, "writes", S.WriterModel, models, v => S.WriterModel = v);
        Model(Agents.Sage, "explains", S.ExplainerModel, models, v => S.ExplainerModel = v);
        Model(Agents.Tick, "reads odd dates", S.ReminderModel, models, v => S.ReminderModel = v);
        Model(Agents.Patch, "diagnoses", S.DiagnosisModel, models, v => S.DiagnosisModel = v);
        if (models.Count == 0) Note("Ollama isn't reachable, so the installed-model list is empty. Start Ollama and reopen settings.");

        Section("Privacy");
        Note("Mindspace talks to exactly one address: the Ollama server on this PC (127.0.0.1). No account, no telemetry, no cloud. " +
             "Agents can only produce text — they can't run commands, open files or change settings. Text is inserted into another app only when you press Insert.");

        var quit = new Button { Content = "Quit Mindspace", Style = (Style)FindResource("Chip"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        quit.Click += (_, _) => App.Mind.Quit();
        _root.Children.Add(quit);
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
        root.Children.Add(new TextBlock { Text = "Four small helpers that live at the top of your screen and run entirely on this PC.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 3, 0, 14) });
        foreach (var a in Agents.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new AgentOrb { Agent = a, Width = 34, Height = 34 });
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
                       "Nothing leaves this PC: the only connection is to the local model server. Agents write text; they can't run anything. " +
                       "Watching the clipboard and replacing the taskbar are off until you switch them on in Settings.",
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
