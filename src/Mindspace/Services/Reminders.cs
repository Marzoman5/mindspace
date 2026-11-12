using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mindspace.Services;

public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public DateTime? Due { get; set; }
    public bool Done { get; set; }
    public bool Fired { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class ReminderStore
{
    static string File => Path.Combine(Settings.Dir, "reminders.json");
    public List<Reminder> Items { get; private set; } = [];
    public event Action? Changed;
    public event Action<Reminder>? Due;

    public ReminderStore()
    {
        try { if (System.IO.File.Exists(File)) Items = JsonSerializer.Deserialize<List<Reminder>>(System.IO.File.ReadAllText(File)) ?? []; }
        catch { Items = []; }
    }

    public Reminder Add(string title, DateTime? due)
    {
        var r = new Reminder { Title = title.Trim(), Due = due };
        Items.Add(r); Save(); return r;
    }
    public void Toggle(Reminder r) { r.Done = !r.Done; Save(); }
    public void Remove(Reminder r) { Items.Remove(r); Save(); }
    public void Snooze(Reminder r, TimeSpan by) { r.Due = DateTime.Now + by; r.Fired = false; r.Done = false; Save(); }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        Changed?.Invoke();
    }

    /// <summary>Called on a slow timer; raises Due once per reminder.</summary>
    public void CheckDue()
    {
        var now = DateTime.Now;
        foreach (var r in Items.Where(r => !r.Done && !r.Fired && r.Due is { } d && d <= now).ToList())
        {
            r.Fired = true; Save();
            Due?.Invoke(r);
        }
    }

    public int OpenCount => Items.Count(r => !r.Done);
}

/// <summary>
/// Understands "call mom tomorrow at 5pm" with plain rules: instant, zero GPU. The model is only a fallback
/// for phrasing the rules don't catch (see Tick). Smallest tool that does the job.
/// </summary>
public static partial class WhenParser
{
    public record Result(string Title, DateTime? Due, bool FoundTime);

    static readonly string[] Days = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
    static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    [GeneratedRegex(@"\b(?:in|after)\s+(an?|half an?|\d+(?:\.\d+)?)\s*(min(?:ute)?s?|h(?:ou)?rs?|days?|weeks?)\b", RegexOptions.IgnoreCase)] private static partial Regex RelRx();
    [GeneratedRegex(@"\b(?:at|by|@|around)\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)?(?!\s*(?:min|hour|day|week|%))\b|\b(\d{1,2})(?::(\d{2}))?\s*(am|pm)\b|\b(\d{1,2}):(\d{2})\b", RegexOptions.IgnoreCase)] private static partial Regex TimeRx();
    [GeneratedRegex(@"\b(?:on\s+|by\s+|this\s+|next\s+)?(sunday|monday|tuesday|wednesday|thursday|friday|saturday|sun|mon|tues?|wed|thurs?|fri|sat)\b", RegexOptions.IgnoreCase)] private static partial Regex DayRx();
    [GeneratedRegex(@"\b(?:on\s+|by\s+)?(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(\d{1,2})(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase)] private static partial Regex MonthRx();
    [GeneratedRegex(@"\b(day after tomorrow|tomorrow|tmrw|today|tonight|next week|this weekend|end of (?:the )?day|eod)\b", RegexOptions.IgnoreCase)] private static partial Regex WordRx();
    [GeneratedRegex(@"\b(?:in the |this |by )?(morning|noon|afternoon|evening|midnight)\b", RegexOptions.IgnoreCase)] private static partial Regex PartRx();
    [GeneratedRegex(@"^\s*(?:hey\s+\w+,?\s+)?(?:please\s+)?(?:remind me (?:to|about|that)|remember to|don'?t forget to|i (?:need|have|want) to|i(?:'ll| will| should| must|'m going to| am going to| gotta)|let me|todo:?|to-do:?)\s+", RegexOptions.IgnoreCase)] private static partial Regex LeadRx();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex SpacesRx();

    public static Result Parse(string text, DateTime? nowOverride = null)
    {
        var now = nowOverride ?? DateTime.Now;
        string t = text.Trim();
        DateTime? date = null; TimeSpan? time = null; DateTime? absolute = null;

        t = Cut(RelRx(), t, m =>
        {
            var q = m.Groups[1].Value.ToLowerInvariant();
            double n = q.StartsWith("half") ? 0.5 : q is "a" or "an" ? 1 : double.Parse(q, CultureInfo.InvariantCulture);
            var u = m.Groups[2].Value.ToLowerInvariant();
            absolute = u[0] switch { 'm' => now.AddMinutes(n), 'h' => now.AddHours(n), 'd' => now.AddDays(n), _ => now.AddDays(7 * n) };
        });
        t = Cut(WordRx(), t, m =>
        {
            switch (m.Value.ToLowerInvariant())
            {
                case "today": date = now.Date; break;
                case "tonight": date = now.Date; time ??= TimeSpan.FromHours(20); break;
                case "tomorrow" or "tmrw": date = now.Date.AddDays(1); break;
                case "day after tomorrow": date = now.Date.AddDays(2); break;
                case "next week": date = now.Date.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7 is var d && d == 0 ? 7 : d); break;
                case "this weekend": date = now.Date.AddDays(((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7); time ??= TimeSpan.FromHours(10); break;
                default: date = now.Date; time ??= TimeSpan.FromHours(17); break; // end of day
            }
        });
        t = Cut(MonthRx(), t, m =>
        {
            int month = Array.IndexOf(Months, m.Groups[1].Value.ToLowerInvariant()[..3]) + 1;
            int day = int.Parse(m.Groups[2].Value);
            if (month < 1 || day < 1 || day > DateTime.DaysInMonth(now.Year, month)) return;
            var d = new DateTime(now.Year, month, day);
            date = d < now.Date ? d.AddYears(1) : d;
        });
        t = Cut(DayRx(), t, m =>
        {
            var name = m.Groups[1].Value.ToLowerInvariant();
            int idx = Array.FindIndex(Days, d => d.StartsWith(name[..3]));
            if (idx < 0) return;
            int delta = (idx - (int)now.DayOfWeek + 7) % 7;
            if (delta == 0) delta = 7;
            if (m.Value.TrimStart().StartsWith("next", StringComparison.OrdinalIgnoreCase) && delta < 7 - (int)now.DayOfWeek) delta += 7;
            date = now.Date.AddDays(delta);
        });
        t = Cut(TimeRx(), t, m =>
        {
            string h = First(m, 1, 4, 7), mi = First(m, 2, 5, 8), ap = First(m, 3, 6);
            int hour = int.Parse(h), min = mi.Length > 0 ? int.Parse(mi) : 0;
            if (hour > 23 || min > 59) return;
            ap = ap.Replace(".", "").ToLowerInvariant();
            if (ap == "pm" && hour < 12) hour += 12;
            else if (ap == "am" && hour == 12) hour = 0;
            else if (ap.Length == 0 && hour is >= 1 and <= 7) hour += 12; // "at 5" almost always means 5pm
            time = new TimeSpan(hour, min, 0);
        });
        t = Cut(PartRx(), t, m =>
        {
            time ??= m.Groups[1].Value.ToLowerInvariant() switch
            { "morning" => TimeSpan.FromHours(9), "noon" => TimeSpan.FromHours(12), "afternoon" => TimeSpan.FromHours(15), "evening" => TimeSpan.FromHours(18), _ => TimeSpan.FromHours(23.98) };
        });

        DateTime? due = absolute;
        if (due == null && (date != null || time != null))
        {
            var d = (date ?? now.Date) + (time ?? TimeSpan.FromHours(9));
            if (date == null && d <= now) d = d.AddDays(1);           // "at 8am" said at noon → tomorrow
            if (date == now.Date && time == null) d = now.Date.AddHours(Math.Min(22, Math.Max(now.Hour + 2, 17))); // "today" → later today
            due = d;
        }
        return new Result(CleanTitle(t), due, due != null);
    }

    static string First(Match m, params int[] groups) => groups.Select(g => m.Groups[g].Value).FirstOrDefault(v => v.Length > 0) ?? "";

    static string Cut(Regex rx, string s, Action<Match> onMatch)
    {
        var m = rx.Match(s);
        if (!m.Success) return s;
        onMatch(m);
        return s.Remove(m.Index, m.Length);
    }

    public static string CleanTitle(string t)
    {
        t = LeadRx().Replace(t, "");
        t = SpacesRx().Replace(t, " ").Trim(' ', ',', '.', ';', ':', '-', '!');
        t = Regex.Replace(t, @"\s+(?:at|on|by|in|for|before|until)$", "", RegexOptions.IgnoreCase).Trim(' ', ',');
        t = Regex.Replace(t, @"\s+,", ",");
        if (t.Length > 0) t = char.ToUpper(t[0]) + t[1..];
        return t;
    }

    public static string Friendly(DateTime d)
    {
        var now = DateTime.Now;
        string time = d.ToString(d.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
        int days = (d.Date - now.Date).Days;
        if (days == 0)
        {
            var delta = d - now;
            if (delta.TotalMinutes is > 0 and < 60) return $"in {Math.Max(1, (int)Math.Round(delta.TotalMinutes))} min";
            return $"Today {time}";
        }
        if (days == 1) return $"Tomorrow {time}";
        if (days == -1) return $"Yesterday {time}";
        if (days is > 1 and < 7) return $"{d:ddd} {time}";
        return $"{d:MMM d} {time}";
    }

    /// <summary>A 7-day lookup table we hand a small model so it never has to do date arithmetic.
    /// Exactly seven days: the benchmark showed that listing a weekday twice makes every small model pick the wrong one.</summary>
    public static string CalendarHint(DateTime now)
    {
        var lines = Enumerable.Range(0, 7).Select(i => now.Date.AddDays(i)).Select((d, i) => $"{(i == 0 ? "today" : i == 1 ? "tomorrow" : d.DayOfWeek.ToString())} = {d:yyyy-MM-dd}");
        return $"Now: {now:yyyy-MM-ddTHH:mm} ({now.DayOfWeek}). Calendar: {string.Join("; ", lines)}.";
    }

    public static readonly JsonNode Schema = JsonNode.Parse("""{"type":"object","properties":{"title":{"type":"string"},"due":{"type":"string"}},"required":["title","due"]}""")!;
}

/// <summary>Spots "I'll send the deck by Friday" inside a draft or copied text. Pure regex, no model, nothing stored.</summary>
public static partial class Commitments
{
    [GeneratedRegex(@"(?<![\w'])(?:I(?:'|’)ll|I will|I(?:'|’)m going to|I am going to|I need to|I have to|I(?:'|’)ve got to|I should|I must|let me|remind me to|don(?:'|’)?t forget to|we need to|we should|we(?:'|’)ll)\s+(?!be\s+(?:happy|glad|honest|there|around|available|in touch\b))([a-z][^.!?\n;]{3,90})", RegexOptions.IgnoreCase)]
    private static partial Regex Rx();
    [GeneratedRegex(@"^(?:be able to|try to|make sure to|go ahead and|also|just|definitely|probably)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex FillerRx();
    static readonly string[] Skip = ["let you know", "see you", "talk to you", "keep you posted", "look forward", "be in touch", "get back to you if", "be honest"];

    public static WhenParser.Result? Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 6000) return null;
        WhenParser.Result? best = null;
        foreach (Match m in Rx().Matches(text))
        {
            var phrase = FillerRx().Replace(m.Groups[1].Value.Trim(), "");
            if (Skip.Any(s => phrase.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            var r = WhenParser.Parse(phrase);
            if (r.Title.Length < 4 || r.Title.Split(' ').Length < 2) continue;
            if (r.FoundTime) return r;            // a commitment with a deadline is the strongest signal
            best ??= r;
        }
        return best;
    }
}
