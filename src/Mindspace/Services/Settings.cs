using System.IO;
using System.Text.Json;

namespace Mindspace.Services;

public sealed class CustomAgent
{
    public string Id { get; set; } = "custom-" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Triggers { get; set; } = "";
    public bool SeesWindow { get; set; }
    public int Palette { get; set; }
    public int Mood { get; set; }
}

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
    /// <summary>See-through glass with the desktop blurred behind it.</summary>
    public bool Frost { get; set; } = true;
    /// <summary>How the taskbar's auto-hide switch was set before the dock borrowed it; null when the dock is not borrowing it.</summary>
    public bool? TaskbarAutoHideBefore { get; set; }
    /// <summary>Which speech model Echo uses: "base.en" (fast) or "small.en" (more accurate).</summary>
    public string SpeechModel { get; set; } = "base.en";
    /// <summary>Keep the text of dictations and notes in the notes file under %AppData%. Audio is never kept.</summary>
    public bool KeepNotes { get; set; } = true;
    public List<CustomAgent> CustomAgents { get; set; } = [];
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
