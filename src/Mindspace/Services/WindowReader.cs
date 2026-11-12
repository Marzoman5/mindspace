using System.Text;
using System.Windows.Automation;

namespace Mindspace.Services;

public sealed record WindowText(string Text, string App, string Title, bool IsSelection);

/// <summary>
/// Reads the text of a window through Windows' accessibility interface (UI Automation) — the same channel a
/// screen reader uses. No screenshots, no clipboard, no keystrokes. Rules, enforced here:
///  • only ever called from something the user did (opening an agent, a hotkey, a button);
///  • never our own windows, never a window whose title looks private (passwords, banking, private browsing);
///  • password boxes are skipped; the text is returned to the caller and never stored or logged.
/// </summary>
public static class WindowReader
{
    public static bool CanRead(nint hwnd) =>
        hwnd != 0 && Native.IsWindow(hwnd) && Native.ProcessName(hwnd) is not ("Mindspace" or "") && !ContextSensor.IsPrivate(Native.WindowTitle(hwnd));

    /// <summary>The selection in that window if there is one, otherwise the main text of the window. Null if nothing readable.</summary>
    public static async Task<WindowText?> ReadAsync(nint hwnd, int maxChars = 6000, bool selectionOnly = false, int timeoutMs = 2500)
    {
        var first = await ReadOnceAsync(hwnd, maxChars, selectionOnly, timeoutMs);
        if (first != null || selectionOnly || !CanRead(hwnd)) return first;
        // Browsers and Electron apps switch their accessibility tree on the first time someone asks; ask once more.
        await Task.Delay(700);
        return await ReadOnceAsync(hwnd, maxChars, selectionOnly, timeoutMs);
    }

    static Task<WindowText?> ReadOnceAsync(nint hwnd, int maxChars, bool selectionOnly, int timeoutMs)
    {
        if (!CanRead(hwnd)) return Task.FromResult<WindowText?>(null);
        // A read that overruns its deadline cannot be interrupted from outside; until it returns, no new one starts,
        // so slow apps can never pile up reader threads.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Task.FromResult<WindowText?>(null);
        var tcs = new TaskCompletionSource<WindowText?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        // UI Automation calls cross into the other app and can block; they get their own thread and a deadline.
        var th = new Thread(() =>
        {
            try { tcs.TrySetResult(Read(hwnd, maxChars, selectionOnly, deadline)); }
            catch { tcs.TrySetResult(null); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }) { IsBackground = true, Name = "WindowReader" };
        th.SetApartmentState(ApartmentState.MTA);
        th.Start();
        return Task.WhenAny(tcs.Task, Task.Delay(timeoutMs + 400)).ContinueWith(t => tcs.Task.IsCompleted ? tcs.Task.Result : null);
    }

    static int _busy;

    static WindowText? Read(nint hwnd, int max, bool selectionOnly, DateTime deadline)
    {
        string app = Native.ProcessName(hwnd), title = Native.WindowTitle(hwnd);
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        var root = AutomationElement.FromHandle(hwnd);

        // 1. What the user has selected, if the keyboard focus is still in that window.
        try
        {
            var focus = AutomationElement.FocusedElement;
            if (focus != null && focus.Current.ProcessId == pid && !focus.Current.IsPassword)
            {
                var sel = Selection(focus) ?? Selection(Ancestor(focus, ControlType.Document));
                if (!string.IsNullOrWhiteSpace(sel)) return new(Trim(sel, max), app, title, true);
            }
        }
        catch { }
        if (selectionOnly) return null;

        // 2. The document the window is showing (a web page, an email, a file).
        var sb = new StringBuilder();
        try
        {
            var docs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            foreach (AutomationElement d in docs)
            {
                if (DateTime.UtcNow > deadline) break;
                if (d.TryGetCurrentPattern(TextPattern.Pattern, out var p))
                {
                    var t = ((TextPattern)p).DocumentRange.GetText(max * 4);
                    if (t.Length > sb.Length) { sb.Clear(); sb.Append(t); }
                }
            }
        }
        catch { }

        // 3. Otherwise, walk what a screen reader would read out.
        if (sb.Length < 40)
        {
            sb.Clear();
            int nodes = 0;
            Walk(root, sb, max * 4, ref nodes, deadline, 0);
        }
        var text = Clean(sb.ToString());
        return text.Length == 0 ? null : new(HeadTail(text, max), app, title, false);
    }

    static string? Selection(AutomationElement? el)
    {
        try
        {
            if (el == null || !el.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return null;
            var parts = ((TextPattern)p).GetSelection().Select(r => r.GetText(8000));
            return string.Join("\n", parts).Trim();
        }
        catch { return null; }
    }

    static AutomationElement? Ancestor(AutomationElement el, ControlType type)
    {
        try
        {
            var w = TreeWalker.ControlViewWalker;
            for (int i = 0; i < 12 && el != null; i++)
            {
                if (Equals(el.Current.ControlType, type)) return el;
                el = w.GetParent(el);
            }
        }
        catch { }
        return null;
    }

    static void Walk(AutomationElement el, StringBuilder sb, int max, ref int nodes, DateTime deadline, int depth)
    {
        if (sb.Length >= max || nodes > 2500 || depth > 40 || DateTime.UtcNow > deadline) return;
        nodes++;
        try
        {
            var c = el.Current;
            if (c.IsPassword) return;
            var type = c.ControlType;
            if (type == ControlType.Text || type == ControlType.Edit || type == ControlType.Hyperlink || type == ControlType.ListItem || type == ControlType.DataItem)
            {
                string s = c.Name ?? "";
                if (type == ControlType.Edit && el.TryGetCurrentPattern(ValuePattern.Pattern, out var v)) s = ((ValuePattern)v).Current.Value ?? s;
                if (s.Length > 1) sb.Append(s).Append('\n');
                if (type == ControlType.Text || type == ControlType.Edit) return;
            }
            else if (type == ControlType.MenuBar || type == ControlType.ToolBar || type == ControlType.TitleBar || type == ControlType.ScrollBar || type == ControlType.Menu) return;
            var w = TreeWalker.ContentViewWalker;
            for (var child = w.GetFirstChild(el); child != null; child = w.GetNextSibling(child))
                Walk(child, sb, max, ref nodes, deadline, depth + 1);
        }
        catch { }
    }

    /// <summary>Collapses the blank runs and object placeholders accessibility text is full of.</summary>
    public static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        int blank = 0; string last = "";
        foreach (var raw in s.Replace("￼", " ").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { if (++blank == 1 && sb.Length > 0) sb.Append('\n'); continue; }
            blank = 0;
            if (line == last) continue;      // the same label read twice (name + value)
            last = line;
            sb.Append(line).Append('\n');
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// When a window holds more than fits, keep the start and, mostly, the end: in mail and chat windows the
    /// navigation comes first and the message being read comes last.
    /// </summary>
    public static string HeadTail(string s, int max)
    {
        if (s.Length <= max) return s;
        int head = max / 3, tail = max - head - 5;
        return s[..head] + "\n…\n" + s[^tail..];
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public static string Pretty(string proc) => proc.ToLowerInvariant() switch
    {
        "chrome" => "Chrome", "msedge" => "Edge", "firefox" => "Firefox", "olk" or "outlook" => "Outlook", "winword" => "Word",
        "code" => "VS Code", "slack" => "Slack", "ms-teams" or "teams" => "Teams", "notepad" => "Notepad", "discord" => "Discord",
        "" => "the window",
        var p => char.ToUpper(p[0]) + (p.Length > 14 ? p[1..14] + "…" : p[1..]),
    };
}
