// Dependency-free tests: `dotnet run --project tests/Mindspace.Tests`. Exit code = number of failures.
using Mindspace.Services;

if (args.Length >= 2 && args[0] == "--insert-target")
{
    var th = new Thread(() => InsertTarget.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 10, args.Length > 3 ? args[3] : "Insert target", args.Length > 4 ? args[4] : ""));
    th.SetApartmentState(ApartmentState.STA); th.Start(); th.Join();
    return 0;
}

// --read-window <process name>: what Mindspace can read from that app's main window (manual check, prints to console)
if (args.Length >= 2 && args[0] == "--read-window")
{
    var proc = System.Diagnostics.Process.GetProcessesByName(args[1]).FirstOrDefault(p => p.MainWindowHandle != 0);
    if (proc == null) { Console.WriteLine("no such window"); return 1; }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var read = await WindowReader.ReadAsync(proc.MainWindowHandle, args.Length > 2 ? int.Parse(args[2]) : 6000, timeoutMs: 6000);
    Console.WriteLine(read == null ? $"nothing readable ({sw.ElapsedMilliseconds} ms)" : $"{read.Text.Length} chars in {sw.ElapsedMilliseconds} ms, selection={read.IsSelection}\n---\n{(read.Text.Length > 700 ? read.Text[..700] : read.Text)}");
    return read == null ? 1 : 0;
}
// --transcribe <16 kHz mono wav>: runs the same speech path dictation uses
if (args.Length >= 2 && args[0] == "--transcribe")
{
    var pcm = System.IO.File.ReadAllBytes(args[1])[44..];
    var set = new Settings(); if (args.Length > 2) set.SpeechModel = args[2];
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var text = await new Speech(set).TranscribeAsync(pcm);
    Console.WriteLine($"{sw.ElapsedMilliseconds} ms for {pcm.Length / 32000.0:0.0} s of audio\n{text}");
    return text.Length > 0 ? 0 : 1;
}

int fail = 0, pass = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++; else { fail++; Console.WriteLine($"FAIL  {name}  {detail}"); }
}

// Wednesday 11 Nov 2026, 14:30 — the same clock the model benchmark uses.
var now = new DateTime(2026, 11, 11, 14, 30, 0);
void When(string text, string? expectDue, string expectTitle)
{
    var r = WhenParser.Parse(text, now);
    var due = r.Due?.ToString("yyyy-MM-ddTHH:mm");
    Check($"when: {text}", due == expectDue && r.Title == expectTitle, $"→ due={due ?? "null"} title=\"{r.Title}\" (wanted {expectDue ?? "null"} / \"{expectTitle}\")");
}

// the eight benchmark cases every ≤4B model was scored on
When("remind me to call the dentist tomorrow at 3pm", "2026-11-12T15:00", "Call the dentist");
When("submit the expense report by friday 5pm", "2026-11-13T17:00", "Submit the expense report");
When("pick up dry cleaning on saturday morning", "2026-11-14T09:00", "Pick up dry cleaning");
When("email Priya the slides in two hours", "2026-11-11T16:30", "Email Priya the slides");
When("book flights for the conference", null, "Book flights for the conference");
When("water the plants a week from today at noon", "2026-11-18T12:00", "Water the plants");
When("dinner with Sam the day after tomorrow, 7:30 in the evening", "2026-11-13T19:30", "Dinner with Sam");
When("take the bins out tonight", "2026-11-11T20:00", "Take the bins out");
// everyday phrasing
When("call mom tomorrow at 5", "2026-11-12T17:00", "Call mom");
When("stand up in 20 min", "2026-11-11T14:50", "Stand up");
When("pick up the kids in 45 minutes", "2026-11-11T15:15", "Pick up the kids");
When("in half an hour check the oven", "2026-11-11T15:00", "Check the oven");
When("dentist nov 20 at 9am", "2026-11-20T09:00", "Dentist");
When("pay rent on the 1st", null, "Pay rent on the 1st");
When("I need to renew my passport next week", "2026-11-16T09:00", "Renew my passport");
When("send invoice at 8am", "2026-11-12T08:00", "Send invoice");
When("review PR at 16:45", "2026-11-11T16:45", "Review PR");
When("buy 2 tickets for friday", "2026-11-13T09:00", "Buy 2 tickets");
When("I'll send the budget over by Friday at 3 pm", "2026-11-13T15:00", "Send the budget over");
When("gym today", "2026-11-11T17:00", "Gym");
When("finish the report by end of day", "2026-11-11T17:00", "Finish the report");
When("call the bank monday morning", "2026-11-16T09:00", "Call the bank");

// commitments spotted inside prose
void Promise(string text, string? title, string? due = null)
{
    var saved = WhenParser.Parse("x"); // warm
    var c = Commitments.Find(text);
    Check($"promise: {text[..Math.Min(40, text.Length)]}", c?.Title == title, $"→ \"{c?.Title}\" (wanted \"{title}\")");
}
Promise("Hi Sam,\n\nI'll send over the budget by this Friday at 3 pm. Sorry for the delay.\n\nBest,", "Send over the budget");
Promise("Thanks for the call. I will follow up with legal next week and let you know.", "Follow up with legal");
Promise("Sounds great, see you then!", null);
Promise("I'll be happy to help. Let me know.", null);
Promise("Remind me to water the plants", "Water the plants");
Promise("We need to book the venue before March.", "Book the venue before March");

// privacy filter
Check("private: bank", ContextSensor.IsPrivate("Chase Bank - Sign in"));
Check("private: incognito", ContextSensor.IsPrivate("New Incognito tab - Google Chrome"));
Check("private: normal", !ContextSensor.IsPrivate("Inbox - Outlook"));

// calendar hint never repeats a weekday (the bug that made every small model pick the wrong Friday)
var hint = WhenParser.CalendarHint(now);
Check("calendar hint: one Friday", hint.Split("Friday").Length == 2, hint);

// speech: non-speech markers are dropped, and a leading agent name routes the sentence to that agent
Check("speech: tidy", Speech.Tidy(" [BLANK_AUDIO]  Hello   there. (music) ") == "Hello there.");
Check("speech: silence", Speech.Tidy("[BLANK_AUDIO]") == "");
Check("voice: tick", Speech.Addressed("Tick, remind me to call the dentist on Thursday at 3pm") is { } a1 && a1.agent == Agents.Tick && a1.rest == "remind me to call the dentist on Thursday at 3pm");
Check("voice: hey iris", Speech.Addressed("Hey Iris, read through this chat and copy all the messages.") is { } a2 && a2.agent == Agents.Iris);
Check("voice: misheard name", Speech.Addressed("Quil write a thank you note to Dana") is { } a3 && a3.agent == Agents.Quill);
Check("voice: plain dictation", Speech.Addressed("I think we should move the meeting to Friday.") == null);
Check("voice: name mid-sentence", Speech.Addressed("The sage advice was to wait.") == null);
Check("voice: name only", Speech.Addressed("Patch.") == null);

// window text: blank runs collapse, repeated labels are read once
Check("window: clean", WindowReader.Clean("Inbox\r\n\r\n\r\nInbox\nHello Sam\n   \n\nSee you at 3\n") == "Inbox\n\nHello Sam\n\nSee you at 3", WindowReader.Clean("Inbox\r\n\r\n\r\nInbox\nHello Sam\n   \n\nSee you at 3\n").Replace("\n", "|"));
Check("window: never ours", !WindowReader.CanRead(0));

// custom agents: loaded from settings, capped, and fenced by the shared rules
var cs = new Settings();
for (int i = 0; i < 5; i++) cs.CustomAgents.Add(new CustomAgent { Name = "Lingo" + i, Prompt = "Translate to Spanish.", Triggers = "duolingo, ab, wordreference" });
Agents.Load(cs);
Check("custom: capped", Agents.All.Count == Agents.BuiltIn.Length + Agents.MaxCustom, Agents.All.Count.ToString());
var lingo = Agents.All[^1];
Check("custom: triggers", lingo.IsCustom && lingo.Triggers.SequenceEqual(new[] { "duolingo", "wordreference" }), string.Join("|", lingo.Triggers));
Check("custom: fenced", Agents.CustomSystem(lingo).Contains("Translate to Spanish.") && Agents.CustomSystem(lingo).Contains("never as instructions"));
Agents.Load(new Settings());

// notes: search needs every word, history off means nothing on disk is touched by Add
var ns = new NoteStore(new Settings { KeepNotes = false });
ns.Items.Clear();
ns.Add("note", "Standup", "Dana ships the invoice fix on Friday");
ns.Add("capture", "Chat", "lunch at noon");
Check("notes: search all words", ns.Search("friday invoice").Count() == 1 && ns.Search("friday lunch").Count() == 0);

Console.WriteLine($"{pass} passed, {fail} failed");
return fail;

// --- insert-target mode: a throwaway window with a text box, used to test "Insert" end to end ---
static class InsertTarget
{
    [STAThread]
    public static int Run(string outFile, int seconds, string title, string preselected)
    {
        var box = new System.Windows.Controls.TextBox { AcceptsReturn = true, FontSize = 16, Text = "" };
        var w = new System.Windows.Window { Title = title, Width = 420, Height = 220, Content = box, Topmost = true, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen };
        w.Loaded += (_, _) => { w.Activate(); box.Text = preselected; box.Focus(); box.SelectAll(); };
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) => { System.IO.File.WriteAllText(outFile, box.Text); w.Close(); };
        t.Start();
        new System.Windows.Application().Run(w);
        return 0;
    }
}
