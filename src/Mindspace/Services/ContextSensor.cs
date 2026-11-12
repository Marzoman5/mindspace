using System.Windows.Threading;

namespace Mindspace.Services;

public sealed record Suggestion(Agent Agent, string Text, string Kind, object? Payload = null);

/// <summary>
/// Decides when an agent should quietly offer help. Privacy rules, enforced here:
///  • only the foreground window's process name and title are looked at, in memory, and never stored or logged;
///  • clipboard text is only inspected when the user has opted in, by a local regex, and is never stored;
///  • nothing is sent to a model until the user clicks.
/// </summary>
public sealed class ContextSensor : IDisposable
{
    readonly Settings _settings;
    readonly Native.WinEventProc _proc;   // keep the delegate alive
    nint _hook;
    readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromSeconds(2.5) };
    nint _pending;
    readonly Dictionary<string, DateTime> _cooldown = new();
    public event Action<Suggestion>? Suggest;
    public event Action<nint>? ForegroundChanged;

    /// <summary>The last real app window the user was in (never one of ours). Used for "Insert".</summary>
    public nint LastExternalWindow { get; private set; }
    public string LastExternalApp { get; private set; } = "";

    static readonly string[] MailHints = ["outlook", "gmail", "- mail", "thunderbird", "proton mail", "inbox", "yahoo mail", "superhuman", "fastmail", "compose"];
    static readonly string[] ChatHints = ["slack", "teams", "discord", "whatsapp", "telegram", "messenger"];
    static readonly string[] LearnHints = ["wikipedia", "stack overflow", "arxiv", "khan academy", "documentation", " docs", "mdn web", ".pdf", "coursera", "tutorial", "leetcode", "lecture"];
    static readonly string[] PrivateHints = ["password", "1password", "bitwarden", "keepass", "lastpass", "private browsing", "inprivate", "incognito", "bank", "paypal"];

    public ContextSensor(Settings s)
    {
        _settings = s;
        _proc = OnForeground;
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        _dwell.Tick += (_, _) => { _dwell.Stop(); Evaluate(_pending); };
        var fg = Native.GetForegroundWindow();
        if (fg != 0 && Native.ProcessName(fg) != "Mindspace") LastExternalWindow = fg;
    }

    void OnForeground(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == 0) return;
        var proc = Native.ProcessName(hwnd);
        if (proc is "Mindspace" or "") return;
        if (proc == "explorer" && Native.WindowTitle(hwnd).Length == 0) return;   // desktop / shell surfaces
        LastExternalWindow = hwnd; LastExternalApp = proc;
        ForegroundChanged?.Invoke(hwnd);
        _pending = hwnd;
        _dwell.Stop(); _dwell.Start();   // only react once the user has settled in a window
    }

    /// <summary>True when the window looks like mail or chat: the place a reply would go.</summary>
    public static bool LooksLikeConversation(nint hwnd)
    {
        string hay = (Native.ProcessName(hwnd) + " " + Native.WindowTitle(hwnd)).ToLowerInvariant();
        return MailHints.Any(hay.Contains) || ChatHints.Any(hay.Contains);
    }

    public static bool IsPrivate(string title) => PrivateHints.Any(h => title.Contains(h, StringComparison.OrdinalIgnoreCase));

    void Evaluate(nint hwnd)
    {
        if (!_settings.Suggestions || hwnd != Native.GetForegroundWindow()) return;
        var title = Native.WindowTitle(hwnd);
        var proc = Native.ProcessName(hwnd).ToLowerInvariant();
        if (IsPrivate(title)) return;
        string hay = (proc + " " + title).ToLowerInvariant();

        if (MailHints.Any(hay.Contains)) Offer("mail", new Suggestion(Agents.Quill, "Writing an email? I can draft it.", "write"));
        else if (ChatHints.Any(hay.Contains)) Offer("chat:" + proc, new Suggestion(Agents.Quill, "Need a reply worded nicely?", "write"), 45);
        else if (Agents.All.FirstOrDefault(a => a.IsCustom && a.Triggers.Any(hay.Contains)) is { } mine)
            Offer("custom:" + mine.Id, new Suggestion(mine, $"{mine.Role}: want a hand here?", "open"), 30);
        else if (LearnHints.Any(hay.Contains)) Offer("learn", new Suggestion(Agents.Sage, "Stuck on something? Ask me.", "explain"), 30);
    }

    void Offer(string key, Suggestion s, int cooldownMinutes = 20)
    {
        if (_cooldown.TryGetValue(key, out var t) && DateTime.Now - t < TimeSpan.FromMinutes(cooldownMinutes)) return;
        _cooldown[key] = DateTime.Now;
        Suggest?.Invoke(s);
    }

    /// <summary>Called on clipboard change (only wired up when the user opted in).</summary>
    public void OnClipboardText(string text)
    {
        if (!_settings.ClipboardSensing || !_settings.Suggestions) return;
        if (IsPrivate(Native.WindowTitle(Native.GetForegroundWindow()))) return;
        var c = Commitments.Find(text);
        if (c == null) return;
        Suggest?.Invoke(new Suggestion(Agents.Tick, $"Remind you to “{Shorten(c.Title, 34)}”{(c.Due is { } d ? " · " + WhenParser.Friendly(d) : "")}?", "remind", c));
    }

    public static string Shorten(string s, int n) => s.Length <= n ? s : s[..(n - 1)].TrimEnd() + "…";

    public void Dispose() { if (_hook != 0) Native.UnhookWinEvent(_hook); _hook = 0; }
}
