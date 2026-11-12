using System.IO;
using System.Text.Json;

namespace Mindspace.Services;

/// <summary>Plain JSON in %AppData%\Mindspace. Nothing here is secret; nothing leaves the machine.</summary>
public sealed class Settings
{
    // Per-agent models. Defaults come from the benchmark in docs/RESEARCH.md.
    public string WriterModel { get; set; } = "qwen3.5:4b";
    public string ExplainerModel { get; set; } = "qwen3.5:4b";
    public string ReminderModel { get; set; } = "qwen3.5:4b";
    public string DiagnosisModel { get; set; } = "qwen3.5:4b";

    /// <summary>Agents may drop down on their own when the foreground app looks relevant.</summary>
    public bool Suggestions { get; set; } = true;
    /// <summary>Opt-in: look at copied text (in memory only) for things worth a reminder.</summary>
    public bool ClipboardSensing { get; set; } = false;
    /// <summary>Opt-in: floating bubble dock; hides the Windows taskbar only while Mindspace runs.</summary>
    public bool Dock { get; set; } = false;
    public bool HealthAlerts { get; set; } = true;
    public bool Welcomed { get; set; } = false;
    public string YourName { get; set; } = "";
    /// <summary>Minutes a model stays in VRAM after its last use.</summary>
    public int KeepAliveMinutes { get; set; } = 3;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mindspace");
    static string File => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { /* corrupt file → defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            System.IO.File.Move(tmp, File, true);
        }
        catch { }
    }
}
