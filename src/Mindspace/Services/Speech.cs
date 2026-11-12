using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Mindspace.Services;

/// <summary>
/// Microphone capture: 16 kHz mono, held in memory only. The microphone is open only between Start and Stop,
/// which only ever happen because the user pressed the dictation key or a record button.
/// </summary>
public sealed class Recorder : IDisposable
{
    public const int Rate = 16000;
    const int Buffers = 4, BufferBytes = Rate * 2 / 10;     // 100 ms each
    nint _h;
    readonly nint[] _hdr = new nint[Buffers], _buf = new nint[Buffers];
    readonly MemoryStream _pcm = new();
    readonly object _lock = new();
    Thread? _pump;
    volatile bool _run;
    static readonly int HdrSize = Marshal.SizeOf<Native.WAVEHDR>();

    public bool IsRecording => _run;
    /// <summary>Loudness of the most recent 100 ms, 0..1. For the level meter and for finding pauses.</summary>
    public double Level { get; private set; }
    public double Seconds { get { lock (_lock) return _pcm.Length / (double)(Rate * 2); } }
    public static bool HasMicrophone => Native.waveInGetNumDevs() > 0;

    public bool Start()
    {
        if (_run) return true;
        if (!HasMicrophone) return false;
        var fmt = new Native.WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Rate * 2 };
        if (Native.waveInOpen(out _h, uint.MaxValue /*default device*/, ref fmt, 0, 0, 0) != 0) { _h = 0; return false; }
        for (int i = 0; i < Buffers; i++)
        {
            _buf[i] = Marshal.AllocHGlobal(BufferBytes);
            _hdr[i] = Marshal.AllocHGlobal(HdrSize);
            Marshal.StructureToPtr(new Native.WAVEHDR { lpData = _buf[i], dwBufferLength = BufferBytes }, _hdr[i], false);
            Native.waveInPrepareHeader(_h, _hdr[i], HdrSize);
            Native.waveInAddBuffer(_h, _hdr[i], HdrSize);
        }
        lock (_lock) _pcm.SetLength(0);
        _run = true;
        Native.waveInStart(_h);
        _pump = new Thread(Pump) { IsBackground = true, Name = "Recorder" };
        _pump.Start();
        return true;
    }

    void Pump()
    {
        var chunk = new byte[BufferBytes];
        while (_run)
        {
            for (int i = 0; i < Buffers && _run; i++)
            {
                var hdr = Marshal.PtrToStructure<Native.WAVEHDR>(_hdr[i]);
                if ((hdr.dwFlags & 1 /*WHDR_DONE*/) == 0) continue;
                int n = (int)Math.Min(hdr.dwBytesRecorded, BufferBytes);
                Marshal.Copy(_buf[i], chunk, 0, n);
                int peak = 0;
                for (int k = 0; k + 1 < n; k += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(chunk, k)));
                Level = peak / 32768.0;
                lock (_lock) _pcm.Write(chunk, 0, n);
                if (_run) Native.waveInAddBuffer(_h, _hdr[i], HdrSize);
            }
            Thread.Sleep(25);
        }
    }

    /// <summary>Everything recorded so far, and starts a fresh buffer. Used for live notes.</summary>
    public byte[] Take()
    {
        lock (_lock) { var b = _pcm.ToArray(); _pcm.SetLength(0); return b; }
    }

    public byte[] Stop()
    {
        if (!_run) return [];
        _run = false;
        _pump?.Join(300);
        Native.waveInReset(_h);
        for (int i = 0; i < Buffers; i++)
        {
            Native.waveInUnprepareHeader(_h, _hdr[i], HdrSize);
            Marshal.FreeHGlobal(_hdr[i]); Marshal.FreeHGlobal(_buf[i]);
        }
        Native.waveInClose(_h); _h = 0; Level = 0;
        return Take();
    }

    public void Dispose() => Stop();

    /// <summary>Peak loudness of a clip, 0..1: lets callers skip silence instead of asking the model to transcribe it.</summary>
    public static double Peak(byte[] pcm)
    {
        int peak = 0;
        for (int k = 0; k + 1 < pcm.Length; k += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, k)));
        return peak / 32768.0;
    }

    public static void WriteWav(string path, byte[] pcm)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
    }
}

/// <summary>
/// Speech to text with whisper.cpp (MIT), run as a short-lived helper process on the CPU. It reads one temporary
/// WAV file and prints text; it has no network code path here and the audio file is deleted straight after.
/// The engine is not part of Mindspace: tools\get-speech.ps1 fetches it from its official release page.
/// </summary>
public sealed class Speech
{
    readonly Settings _settings;
    public Speech(Settings s) => _settings = s;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mindspace", "speech");
    static string Exe => Path.Combine(Dir, "whisper-cli.exe");
    string ModelPath(string name) => Path.Combine(Dir, $"ggml-{name}.bin");
    public bool HasModel(string name) => File.Exists(ModelPath(name));
    string Model => HasModel(_settings.SpeechModel) ? _settings.SpeechModel : "base.en";
    public bool Installed => File.Exists(Exe) && HasModel(Model);
    public const string SetupHint = "Voice isn't set up yet. Run tools\\get-speech.ps1 once (it downloads the open-source whisper.cpp engine, about 150 MB).";

    static readonly Regex Noise = new(@"\[[^\]]*\]|\([^)]*\)|♪", RegexOptions.Compiled);

    /// <summary>Text of a 16 kHz mono clip, or "" for silence. Throws InvalidOperationException if the engine is missing or fails.</summary>
    public async Task<string> TranscribeAsync(byte[] pcm, CancellationToken ct = default)
    {
        if (pcm.Length < Recorder.Rate * 2 * 0.3 || Recorder.Peak(pcm) < 0.012) return "";     // too short or silent
        if (!Installed) throw new InvalidOperationException(SetupHint);
        var wav = Path.Combine(Path.GetTempPath(), $"mindspace-{Guid.NewGuid():N}.wav");
        try
        {
            Recorder.WriteWav(wav, pcm);
            int threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            var psi = new ProcessStartInfo(Exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Dir };
            foreach (var a in new[] { "-m", ModelPath(Model), "-t", threads.ToString(), "-nt", "-np", "-l", Model.EndsWith(".en") ? "en" : "auto", "-f", wav }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("The speech engine did not start.");
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }      // never fight the user's own work for the CPU
            _ = p.StandardError.ReadToEndAsync(ct);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                var text = await p.StandardOutput.ReadToEndAsync(limit.Token);
                await p.WaitForExitAsync(limit.Token);
                if (p.ExitCode != 0) throw new InvalidOperationException("The speech engine reported an error.");
                return Tidy(text);
            }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } throw; }
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    /// <summary>Whisper marks non-speech as [BLANK_AUDIO], (music) and the like; those are not words the user said.</summary>
    public static string Tidy(string raw) => Regex.Replace(Noise.Replace(raw, " "), @"\s+", " ").Trim();

    /// <summary>
    /// "Tick, remind me to call mom at five" → (tick, "remind me to call mom at five"). Null when the words do not
    /// start with an agent's name, in which case they are plain dictation.
    /// </summary>
    public static (Agent agent, string rest)? Addressed(string text)
    {
        var m = Regex.Match(text, @"^\s*(?:hey|ok|okay|hi)?[\s,]*([A-Za-z]{3,14})\s*[,:.!?-]*\s+(.{3,})$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        string said = m.Groups[1].Value.ToLowerInvariant();
        foreach (var a in Agents.All)
        {
            string name = a.Name.ToLowerInvariant();
            if (said == name || Heard.TryGetValue(name, out var alts) && alts.Contains(said)) return (a, m.Groups[2].Value.Trim());
        }
        return null;
    }

    // What a speech model tends to write when someone says an agent's name.
    static readonly Dictionary<string, string[]> Heard = new()
    {
        ["quill"] = ["quil", "kwill", "quell"], ["tick"] = ["tic", "tik"], ["sage"] = ["sayge", "siege"],
        ["patch"] = ["pach"], ["echo"] = ["eko", "ecco"], ["iris"] = ["irs", "irish", "ires"],
    };
}
