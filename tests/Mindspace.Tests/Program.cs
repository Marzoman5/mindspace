// Dependency-free tests: `dotnet run --project tests/Mindspace.Tests`. Exit code = number of failures.
using Mindspace.Services;

if (args.Length >= 2 && args[0] == "--insert-target")
{
    var th = new Thread(() => InsertTarget.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 10, args.Length > 3 ? args[3] : "Insert target", args.Length > 4 ? args[4] : ""));
    th.SetApartmentState(ApartmentState.STA); th.Start(); th.Join();
    return 0;
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
