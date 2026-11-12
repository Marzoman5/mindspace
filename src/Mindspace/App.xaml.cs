using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;
using Mindspace.Services;

namespace Mindspace;

public partial class App : Application
{
    public static App Mind => (App)Current;

    public Settings Settings { get; private set; } = null!;
    public Ollama Ollama { get; private set; } = null!;
    public ReminderStore Reminders { get; private set; } = null!;
    public SystemMonitor Monitor { get; } = new();
    public ContextSensor Sensor { get; private set; } = null!;
    public HaloWindow Halo { get; private set; } = null!;
    public PanelWindow Panel { get; private set; } = null!;
    public DockWindow? Dock { get; private set; }

    Mutex? _mutex;
    readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly CancellationTokenSource _life = new();
    const string PipeName = "Mindspace.Control";

    /// <summary>Raised after every generation so the footer can show model + speed.</summary>
    public event Action<string, GenStats>? Generated;
    public void ReportGeneration(string model, GenStats s) => Generated?.Invoke(model, s);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "Mindspace.SingleInstance", out bool first);
        if (!first)
        {
            // Second launch just forwards its arguments ("open sage", "quit" …) to the running instance.
            Forward(e.Args.Length > 0 ? string.Join(' ', e.Args) : "open");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Native.SetTaskbarVisible(true);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Native.SetTaskbarVisible(true); Log(ex.ExceptionObject as Exception); };

        Settings = Settings.Load();
        Ollama = new Ollama(Settings);
        Reminders = new ReminderStore();
        Sensor = new ContextSensor(Settings);
        Halo = new HaloWindow();
        Panel = new PanelWindow();
        Halo.Show();

        Sensor.Suggest += s => Halo.Peek(s);
        Monitor.Alert += text => Halo.Peek(new Suggestion(Agents.Patch, text, "diagnose"));
        Reminders.Due += r => Halo.Peek(new Suggestion(Agents.Tick, r.Title, "due", r));

        _heartbeat.Tick += (_, _) =>
        {
            Monitor.Tick(Settings.HealthAlerts);
            Reminders.CheckDue();
            Dock?.Heartbeat();
        };
        _heartbeat.Start();
        Monitor.Tick(false);

        ApplyDock();
        _ = ListenAsync(_life.Token);
        _ = Ollama.TryStartAsync();

        if (e.Args.Length > 0) Dispatcher.BeginInvoke(() => Command(string.Join(' ', e.Args)), DispatcherPriority.ApplicationIdle);
        else if (!Settings.Welcomed) Dispatcher.BeginInvoke(() => Panel.Open("welcome"), DispatcherPriority.ApplicationIdle);
    }

    public void ApplyDock()
    {
        if (Settings.Dock && Dock == null) { Dock = new DockWindow(); Dock.Show(); }
        else if (!Settings.Dock && Dock != null) { Dock.Close(); Dock = null; Native.SetTaskbarVisible(true); }
    }

    public void Quit()
    {
        _life.Cancel();
        Sensor.Dispose();
        Dock?.Close();
        Native.SetTaskbarVisible(true);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Native.SetTaskbarVisible(true);
        base.OnExit(e);
    }

    // ---------- local control channel ----------
    // A named pipe restricted to the current user. It accepts only the UI verbs below; it cannot
    // run programs, read files, or return data. It exists so `Mindspace.exe open sage` works from a shortcut.

    async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(pipe);
                var buf = new char[2048];
                int n = await reader.ReadAsync(buf.AsMemory(), ct);
                var line = new string(buf, 0, n).Trim();
                if (line.Length > 0) await Dispatcher.InvokeAsync(() => Command(line));
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(500, CancellationToken.None); }
        }
    }

    static void Forward(string line)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(1500);
            using var w = new StreamWriter(pipe);
            w.Write(line);
        }
        catch { }
    }

    void Command(string line)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string arg1 = parts.Length > 1 ? parts[1].ToLowerInvariant() : "", rest = parts.Length > 2 ? parts[2] : "";
        switch (parts[0].ToLowerInvariant().TrimStart('-'))
        {
            case "open": Panel.Open(arg1.Length > 0 ? arg1 : null); break;
            case "close": Panel.Dismiss(); break;
            case "toggle": Panel.Toggle(); break;
            case "ask": Panel.Open(arg1, rest, autoRun: true); break;       // fills the box and presses send, as the user would
            case "peek": Halo.Peek(new Suggestion(Agents.ById(arg1), rest.Length > 0 ? rest : Agents.ById(arg1).Tagline, arg1 switch { "quill" => "write", "patch" => "diagnose", _ => "explain" })); break;
            case "hover": Halo.DemoExpand(); break;
            case "quit": Quit(); break;
        }
    }

    static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:s} {ex}\n\n");
        }
        catch { }
    }
}
