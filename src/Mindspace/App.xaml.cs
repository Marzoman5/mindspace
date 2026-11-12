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
    public NoteStore Notes { get; private set; } = null!;
    public Speech Speech { get; private set; } = null!;
    public Recorder Mic { get; } = new();
    public HaloWindow Halo { get; private set; } = null!;
    public PanelWindow Panel { get; private set; } = null!;
    public DockWindow? Dock { get; private set; }

    /// <summary>True when the glass is see-through with the desktop blurred behind it (Windows 11; can be switched off).</summary>
    public static bool Frost { get; private set; }

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

        // Software rendering: the UI is a few small translucent windows, so the CPU path is plenty, it keeps the
        // discrete GPU asleep (battery) and its VRAM free for the model, and it avoids a ~100 MB Direct3D footprint.
        if (!e.Args.Contains("--gpu-ui")) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreShell();
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { RestoreShell(); Log(ex.ExceptionObject as Exception); };

        Settings = Settings.Load();
        Frost = Settings.Frost && Environment.OSVersion.Version.Build >= 22000;
        Ollama = new Ollama(Settings);
        Agents.Load(Settings);
        Reminders = new ReminderStore();
        Notes = new NoteStore(Settings);
        Speech = new Speech(Settings);
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

        if (!Settings.Dock) RestoreShell();   // self-heal if a previous run was killed while the dock was up
        ApplyDock();
        _ = ListenAsync(_life.Token);
        _ = Ollama.TryStartAsync();

        if (e.Args.Length > 0) Dispatcher.BeginInvoke(() => Command(string.Join(' ', e.Args)), DispatcherPriority.ApplicationIdle);
        else if (!Settings.Welcomed) Dispatcher.BeginInvoke(() => Panel.Open("welcome"), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Puts back the two things the dock borrows: the taskbar and the strip of screen it reserves.</summary>
    public void RestoreShell()
    {
        Native.SetTaskbarVisible(true);
        if (Settings?.TaskbarAutoHideBefore is bool before) { Native.TaskbarAutoHide = before; Settings.TaskbarAutoHideBefore = null; Settings.Save(); }
    }

    /// <summary>Hides the taskbar and frees its strip for windows, remembering how the user had it set.</summary>
    public void BorrowShell()
    {
        if (Settings.TaskbarAutoHideBefore == null) { Settings.TaskbarAutoHideBefore = Native.TaskbarAutoHide; Settings.Save(); }
        Native.TaskbarAutoHide = true;
        Native.SetTaskbarVisible(false);
    }

    public void ApplyDock()
    {
        if (Settings.Dock && Dock == null) { Dock = new DockWindow(); Dock.Show(); }
        else if (!Settings.Dock && Dock != null) { Dock.Close(); Dock = null; RestoreShell(); }
    }

    public void Quit()
    {
        _life.Cancel();
        Mic.Stop();
        Sensor.Dispose();
        Dock?.Close();
        RestoreShell();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RestoreShell();
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
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readTimeout.CancelAfter(2000);     // a client that connects and says nothing can't block the channel
                int n;
                try { n = await reader.ReadAsync(buf.AsMemory(), readTimeout.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
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
            case "dictate": Halo.ToggleDictation(handsFree: true); break;   // same as tapping Alt+Shift+D
            case "quit": Quit(); break;
#if DEBUG
            // Debug builds only (never compiled into Release): lets the automated test press "Insert".
            // Debug builds only: lets the automated test press Iris's Start button ("debug-start collect up" / "debug-start play").
            case "debug-start": Panel.Open("iris", arg1 == "play" ? "keep playing this game" : "collect all messages " + rest); Dispatcher.BeginInvoke(() => Panel.DebugView<Views.IrisView>("iris")?.DebugStart(), DispatcherPriority.ApplicationIdle); break;
            case "debug-insert": Panel.InsertText(arg1 + (rest.Length > 0 ? " " + rest : "")); break;
#endif
        }
    }

    static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var log = new FileInfo(Path.Combine(Settings.Dir, "error.log"));
            if (log.Exists && log.Length > 256 * 1024) log.Delete();    // never grows without bound
            File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:s} {ex}\n\n");
        }
        catch { }
    }
}
