using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mindspace.Services;

public record ChatMsg(string Role, string Content);

public sealed class GenStats
{
    public double TokensPerSec; public int Tokens; public double FirstTokenMs; public double TotalMs;
}

/// <summary>
/// Minimal Ollama client. The base address is hard-wired to loopback: this class is the only
/// network code in the app and it cannot talk to anything but the local model server.
/// Model output is only ever returned as text; nothing here (or anywhere) executes it.
/// </summary>
public sealed class Ollama
{
    static readonly Uri Base = new("http://127.0.0.1:11434/");
    readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    { BaseAddress = Base, Timeout = Timeout.InfiniteTimeSpan };
    readonly HashSet<string> _noThink = new();
    readonly Settings _settings;

    public Ollama(Settings s) => _settings = s;

    public static bool IsRemoteTag(string model) => model.Contains("cloud", StringComparison.OrdinalIgnoreCase) || model.Contains("://");

    public async Task<bool> IsUpAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(1500);
            using var r = await _http.GetAsync("api/version", cts.Token);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<string>> ModelsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(3000);
            var json = JsonNode.Parse(await _http.GetStringAsync("api/tags", cts.Token));
            return json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>() ?? "").Where(n => n.Length > 0 && !IsRemoteTag(n)).OrderBy(n => n).ToList() ?? [];
        }
        catch { return []; }
    }

    /// <summary>Starts the local server if the user has Ollama installed but not running.</summary>
    public async Task<bool> TryStartAsync()
    {
        if (await IsUpAsync()) return true;
        try
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            if (!File.Exists(exe)) return false;
            Process.Start(new ProcessStartInfo(exe, "serve") { CreateNoWindow = true, UseShellExecute = false });
            for (int i = 0; i < 20; i++) { await Task.Delay(400); if (await IsUpAsync()) return true; }
        }
        catch { }
        return false;
    }

    /// <summary>Streams a chat completion as text deltas.</summary>
    public async IAsyncEnumerable<string> ChatAsync(string model, IEnumerable<ChatMsg> messages, double temperature = 0.6,
        int maxTokens = 700, JsonNode? format = null, GenStats? stats = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Ollama can proxy "cloud" model tags to a remote service. Mindspace promises on-device only, so refuse them here,
        // at the single choke point every request passes through.
        if (IsRemoteTag(model)) throw new OllamaException($"“{model}” is a cloud model. Mindspace only runs models on this PC — pick a local one in Settings.");
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? resp = null;
        for (int attempt = 0; attempt < 2 && resp == null; attempt++)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["stream"] = true,
                ["keep_alive"] = $"{Math.Clamp(_settings.KeepAliveMinutes, 0, 60)}m",
                ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
                ["options"] = new JsonObject { ["temperature"] = temperature, ["num_predict"] = maxTokens, ["num_ctx"] = 4096, ["top_p"] = 0.9 },
            };
            if (!_noThink.Contains(model)) body["think"] = false;   // small models: answer, don't deliberate
            if (format != null) body["format"] = format.DeepClone();
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            var r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (r.IsSuccessStatusCode) { resp = r; break; }
            var err = await r.Content.ReadAsStringAsync(ct);
            r.Dispose();
            if (attempt == 0 && err.Contains("think", StringComparison.OrdinalIgnoreCase)) { _noThink.Add(model); continue; }
            throw new OllamaException(err.Contains("not found") ? $"Model “{model}” isn't installed. Run:  ollama pull {model}" : $"Ollama error: {Trim(err)}");
        }

        using (resp)
        using (var reader = new StreamReader(await resp!.Content.ReadAsStreamAsync(ct)))
        {
            bool first = true;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(line); } catch { continue; }
                var delta = node?["message"]?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(delta))
                {
                    if (first && stats != null) stats.FirstTokenMs = sw.Elapsed.TotalMilliseconds;
                    first = false;
                    yield return delta;
                }
                if (node?["done"]?.GetValue<bool>() == true)
                {
                    if (stats != null)
                    {
                        stats.Tokens = node["eval_count"]?.GetValue<int>() ?? 0;
                        double ns = node["eval_duration"]?.GetValue<long>() ?? 0;
                        stats.TokensPerSec = ns > 0 ? stats.Tokens / (ns / 1e9) : 0;
                        stats.TotalMs = sw.Elapsed.TotalMilliseconds;
                    }
                    yield break;
                }
            }
        }
    }

    public async Task<string> CompleteAsync(string model, IEnumerable<ChatMsg> messages, double temperature = 0.2,
        int maxTokens = 300, JsonNode? format = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var d in ChatAsync(model, messages, temperature, maxTokens, format, null, ct)) sb.Append(d);
        return sb.ToString();
    }

    /// <summary>Loads a model into memory ahead of the first keystroke so the first answer is instant.</summary>
    public async Task WarmAsync(string model)
    {
        if (IsRemoteTag(model)) return;
        try
        {
            var body = new JsonObject { ["model"] = model, ["keep_alive"] = $"{Math.Clamp(_settings.KeepAliveMinutes, 0, 60)}m" };
            using var cts = new CancellationTokenSource(60000);
            using var r = await _http.PostAsync("api/generate", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cts.Token);
        }
        catch { }
    }

    static string Trim(string s) => s.Length > 200 ? s[..200] + "…" : s;
}

public sealed class OllamaException(string message) : Exception(message);
