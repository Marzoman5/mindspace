using System.IO;
using System.Text.Json;

namespace Mindspace.Services;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>"dictation" (something you spoke into another app), "note" (a recorded note) or "capture" (text saved from a window).</summary>
    public string Kind { get; set; } = "note";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Bullets { get; set; } = "";
    public string App { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>
/// Echo's history: the text of dictations, notes and window captures, in one JSON file under %AppData%\Mindspace.
/// Only text is kept, never audio. Nothing is written when "Keep history" is off. Capped so it cannot grow without bound.
/// </summary>
public sealed class NoteStore
{
    const int MaxItems = 300, MaxChars = 120_000;
    static string FilePath => Path.Combine(Settings.Dir, "notes.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly Settings _settings;
    public List<Note> Items { get; private set; } = [];
    public event Action? Changed;

    public NoteStore(Settings s)
    {
        _settings = s;
        try { if (File.Exists(FilePath)) Items = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? []; }
        catch { Items = []; }
    }

    public Note Add(string kind, string title, string text, string app = "", string bullets = "")
    {
        var n = new Note { Kind = kind, Title = title, Text = text.Length > MaxChars ? text[..MaxChars] : text, App = app, Bullets = bullets };
        Items.Insert(0, n);
        if (Items.Count > MaxItems) Items.RemoveRange(MaxItems, Items.Count - MaxItems);
        Save();
        return n;
    }

    public void Update() => Save();
    public void Remove(Note n) { Items.Remove(n); Save(); }
    public void Clear() { Items.Clear(); Save(); try { File.Delete(FilePath); } catch { } }

    /// <summary>Case-insensitive search over titles, text and bullets; every word must appear.</summary>
    public IEnumerable<Note> Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Items.Where(n => words.All(w => n.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Text.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Bullets.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    void Save()
    {
        Changed?.Invoke();
        if (!_settings.KeepNotes) return;       // history stays in memory for this session only
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}
