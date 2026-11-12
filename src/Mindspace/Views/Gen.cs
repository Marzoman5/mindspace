using System.Diagnostics;
using System.Text;
using System.Windows;
using Mindspace.Services;

namespace Mindspace.Views;

/// <summary>Shared "ask the local model and stream the answer into the UI" routine.</summary>
public static class Gen
{
    public sealed record Outcome(string Text, bool Ok, string? Error);

    public static async Task<Outcome> StreamAsync(Agent agent, string model, List<ChatMsg> messages, Action<string> onText,
        CancellationToken ct, double temperature = 0.6, int maxTokens = 700)
    {
        var app = App.Mind;
        var sb = new StringBuilder();
        var stats = new GenStats();
        app.Panel.SetThinking(agent, true);
        try
        {
            if (!await app.Ollama.IsUpAsync() && !await app.Ollama.TryStartAsync())
            {
                app.Panel.SetStatus(false, "Ollama isn't running");
                return new("", false, "I can't reach the local model. Mindspace runs its agents on Ollama — start it (button below) or install it from ollama.com.");
            }
            var throttle = Stopwatch.StartNew();
            await foreach (var delta in app.Ollama.ChatAsync(model, messages, temperature, maxTokens, null, stats, ct))
            {
                sb.Append(delta);
                if (throttle.ElapsedMilliseconds >= 33) { onText(sb.ToString()); throttle.Restart(); }
            }
            var text = sb.ToString().Trim();
            onText(text);
            if (stats.Tokens > 0) app.ReportGeneration(model, stats);
            return new(text, true, null);
        }
        catch (OperationCanceledException) { return new(sb.ToString().Trim(), true, null); }
        catch (OllamaException ex) { return new(sb.ToString(), false, ex.Message); }
        catch (Exception ex) { return new(sb.ToString(), false, "Something went wrong talking to the local model: " + ex.Message); }
        finally { app.Panel.SetThinking(agent, false); }
    }

    /// <summary>Clipboard text, read only at the moment the user opens an agent. Empty if none / too long / unavailable.</summary>
    public static string ClipboardText(int max = 6000)
    {
        try
        {
            if (!Clipboard.ContainsText()) return "";
            var t = Clipboard.GetText().Trim();
            return t.Length <= max ? t : t[..max];
        }
        catch { return ""; }
    }

    /// <summary>
    /// Wraps untrusted text (clipboard, selection) as data for the model. The closing tag is defanged so the
    /// text cannot step outside its data section and pose as instructions.
    /// </summary>
    public static string AsContext(string text) =>
        "<context>\n" + System.Text.RegularExpressions.Regex.Replace(text, @"<\s*/?\s*context", "<​context", System.Text.RegularExpressions.RegexOptions.IgnoreCase) + "\n</context>";

    public static string OneLine(string s, int max) => ContextSensor.Shorten(System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim(), max);
}
