using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mindspace.Controls;
using Mindspace.Services;

namespace Mindspace.Views;

/// <summary>Patch only ever reads. It cannot end processes, change settings or run commands; the two buttons open Windows' own tools.</summary>
public partial class PatchView : UserControl, IAgentView
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    readonly (Ring ring, TextBlock value, TextBlock sub)[] _g = new (Ring, TextBlock, TextBlock)[4];
    CancellationTokenSource? _cts;
    Snapshot? _last;
    bool _sampling;

    public PatchView()
    {
        InitializeComponent();
        DiagBtn.Background = Agents.Patch.Gradient();
        string[] names = ["CPU", "Memory", "GPU", "VRAM"];
        for (int i = 0; i < 4; i++)
        {
            var ring = new Ring { Width = 64, Height = 64, Stroke = Agents.Patch.Gradient() };
            var value = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Ui"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "–" };
            var sub = new TextBlock { FontSize = 10.5, Foreground = (Brush)FindResource("Faint"), HorizontalAlignment = HorizontalAlignment.Center, Text = " " };
            var cell = new Grid(); cell.Children.Add(ring); cell.Children.Add(value);
            var col = new StackPanel();
            col.Children.Add(cell);
            col.Children.Add(new TextBlock { Text = names[i], FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            col.Children.Add(sub);
            Gauges.Children.Add(col);
            _g[i] = (ring, value, sub);
        }
        _timer.Tick += async (_, _) => await RefreshAsync();
        // Sampling runs only while this view is on screen.
        IsVisibleChanged += (_, _) => { if (IsVisible) { _timer.Start(); _ = RefreshAsync(); } else _timer.Stop(); };
    }

    public void Activate(string? intent, bool autoRun)
    {
        if (autoRun) Dispatcher.BeginInvoke(() => Diagnose_Click(this, new RoutedEventArgs()), DispatcherPriority.Background);
    }

    async Task RefreshAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var s = _last = await App.Mind.Monitor.SnapshotAsync();
            Set(0, s.Cpu / 100, $"{s.Cpu:0}%", $"{Environment.ProcessorCount} threads");
            Set(1, s.MemPercent / 100.0, $"{s.MemPercent}%", $"{s.MemUsedGb:0.0} / {s.MemTotalGb:0} GB");
            if (s.GpuPercent is { } g)
            {
                Set(2, g / 100.0, $"{g}%", $"{s.GpuTempC}°C");
                Set(3, (s.VramUsedGb ?? 0) / Math.Max(0.1, s.VramTotalGb ?? 1), $"{s.VramUsedGb:0.0}", $"of {s.VramTotalGb:0} GB");
            }
            else { Set(2, 0, "–", "no NVIDIA GPU"); Set(3, 0, "–", " "); }
            Facts.Text = $"C: {s.DiskFreeGb:0} GB free  ·  up {(s.Uptime.TotalDays >= 1 ? $"{(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h" : $"{s.Uptime.Hours}h {s.Uptime.Minutes}m")}";

            Procs.Children.Clear();
            double max = Math.Max(5, s.TopCpu.Count > 0 ? s.TopCpu[0].CpuPercent : 5);
            foreach (var p in s.TopCpu.Take(4))
            {
                var mem = s.TopMem.FirstOrDefault(m => m.Name == p.Name)?.MemMb ?? p.MemMb;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("Surface"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 10, 0) };
                var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Agents.Patch.Gradient(), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 150 * Math.Min(1, p.CpuPercent / max)) };
                track.Child = fill; Grid.SetColumn(track, 1); row.Children.Add(track);
                var nums = new TextBlock { Text = $"{p.CpuPercent:0.0}%  ·  {(p.MemMb >= 1024 ? $"{p.MemMb / 1024:0.0} GB" : $"{p.MemMb:0} MB")}", FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(nums, 2); row.Children.Add(nums);
                Procs.Children.Add(row);
            }
        }
        catch { }
        finally { _sampling = false; }
    }

    void Set(int i, double v, string text, string sub)
    {
        _g[i].ring.AnimateTo(v); _g[i].value.Text = text; _g[i].sub.Text = sub;
    }

    async void Diagnose_Click(object s, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        var cts = _cts = new CancellationTokenSource();
        DiagLabel.Text = "Stop";
        VerdictCard.Visibility = Visibility.Visible;
        Verdict.Text = "*Taking a look…*";
        try
        {
            var snap = _last ?? await App.Mind.Monitor.SnapshotAsync(cts.Token);
            var msgs = new List<ChatMsg> { new("system", Agents.PatchSystem), new("user", snap.ToPrompt()) };
            var r = await Gen.StreamAsync(Agents.Patch, App.Mind.Settings.DiagnosisModel, msgs, t => Verdict.Text = t, cts.Token, 0.2, 260);
            if (!r.Ok) Verdict.Text = r.Error ?? "";
        }
        catch (OperationCanceledException) { }
        finally { if (_cts == cts) _cts = null; DiagLabel.Text = "Check again"; }
    }

    // Fixed, absolute system paths only: no user- or model-supplied strings ever reach Process.Start.
    void TaskMgr_Click(object s, RoutedEventArgs e) => Launch(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"));
    void Storage_Click(object s, RoutedEventArgs e) => Launch("ms-settings:storagesense");

    static void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
