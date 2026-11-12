using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mindspace.Services;

public sealed record ProcInfo(string Name, double CpuPercent, double MemMb, int Count);

public sealed class Snapshot
{
    public double Cpu; public int MemPercent; public double MemUsedGb, MemTotalGb;
    public int? GpuPercent; public double? VramUsedGb, VramTotalGb; public int? GpuTempC; public string GpuName = "";
    public double DiskFreeGb, DiskTotalGb; public TimeSpan Uptime; public int ProcessCount;
    public int? BatteryPercent; public bool? PluggedIn;
    public List<ProcInfo> TopCpu = [], TopMem = [];

    public string ToPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CPU: {Cpu:0}% total across {Environment.ProcessorCount} threads");
        sb.AppendLine($"RAM: {MemUsedGb:0.0} of {MemTotalGb:0.0} GB used ({MemPercent}%)");
        if (GpuPercent != null) sb.AppendLine($"GPU ({GpuName}): {GpuPercent}% load, VRAM {VramUsedGb:0.0} of {VramTotalGb:0.0} GB, {GpuTempC}°C");
        sb.AppendLine($"Disk C: {DiskFreeGb:0} GB free of {DiskTotalGb:0} GB");
        sb.AppendLine($"Uptime: {(int)Uptime.TotalDays}d {Uptime.Hours}h. Processes: {ProcessCount}.");
        if (BatteryPercent != null) sb.AppendLine($"Battery: {BatteryPercent}% ({(PluggedIn == true ? "plugged in" : "on battery")})");
        sb.AppendLine("Top by CPU: " + string.Join(", ", TopCpu.Select(p => $"{p.Name} {p.CpuPercent:0.#}%")));
        sb.AppendLine("Top by memory: " + string.Join(", ", TopMem.Select(p => $"{p.Name} {p.MemMb / 1024:0.0} GB{(p.Count > 1 ? $" ({p.Count} processes)" : "")}")));
        return sb.ToString();
    }
}

/// <summary>
/// Read-only system metrics. The always-on part is one syscall every few seconds (CPU + RAM);
/// the expensive parts (process list, nvidia-smi) only run while Patch is on screen.
/// </summary>
public sealed class SystemMonitor
{
    public double Cpu { get; private set; }
    public int MemPercent { get; private set; }
    public double MemUsedGb { get; private set; }
    public double MemTotalGb { get; private set; }
    int _hotTicks;
    DateTime _lastAlert = DateTime.MinValue;
    public event Action<string>? Alert;

    /// <summary>Cheap heartbeat. Call every ~3 s.</summary>
    public void Tick(bool alertsEnabled)
    {
        Cpu = Native.CpuPercent();
        (MemUsedGb, MemTotalGb, MemPercent) = Native.Memory();
        bool hot = Cpu >= 88 || MemPercent >= 92;
        _hotTicks = hot ? _hotTicks + 1 : 0;
        if (alertsEnabled && _hotTicks >= 6 && DateTime.Now - _lastAlert > TimeSpan.FromMinutes(15))
        {
            _lastAlert = DateTime.Now; _hotTicks = 0;
            Alert?.Invoke(MemPercent >= 92 ? $"Memory is at {MemPercent}% — want a look?" : $"CPU has been at {Cpu:0}% — want a look?");
        }
    }

    public async Task<Snapshot> SnapshotAsync(CancellationToken ct = default)
    {
        var s = new Snapshot { Uptime = Native.Uptime() };
        var gpuTask = QueryGpuAsync(ct);
        await Task.Run(async () =>
        {
            var first = Sample();
            Native.CpuPercent();
            await Task.Delay(700, ct);
            var second = Sample();
            s.Cpu = Native.CpuPercent();
            double interval = 0.7 * Environment.ProcessorCount * 1000;
            var groups = second.Values.GroupBy(p => p.name).Select(g =>
            {
                double cpuMs = g.Sum(p => first.TryGetValue(p.pid, out var before) ? Math.Max(0, p.cpuMs - before.cpuMs) : 0);
                return new ProcInfo(g.Key, 100.0 * cpuMs / interval, g.Sum(p => p.memMb), g.Count());
            }).ToList();
            s.ProcessCount = second.Count;
            s.TopCpu = groups.Where(g => g.Name != "Idle").OrderByDescending(g => g.CpuPercent).Take(5).ToList();
            s.TopMem = groups.OrderByDescending(g => g.MemMb).Take(5).ToList();
        }, ct);
        (s.MemUsedGb, s.MemTotalGb, s.MemPercent) = Native.Memory();
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            s.DiskFreeGb = d.AvailableFreeSpace / 1073741824.0; s.DiskTotalGb = d.TotalSize / 1073741824.0;
        }
        catch { }
        var ps = System.Windows.SystemParameters.PowerLineStatus;
        try
        {
            var gpu = await gpuTask;
            if (gpu != null) (s.GpuName, s.GpuPercent, s.VramUsedGb, s.VramTotalGb, s.GpuTempC) = gpu.Value;
        }
        catch { }
        return s;
    }

    record struct P(int pid, string name, double cpuMs, double memMb);

    static Dictionary<int, P> Sample()
    {
        var d = new Dictionary<int, P>();
        foreach (var p in Process.GetProcesses())
        {
            try { d[p.Id] = new P(p.Id, p.ProcessName, p.TotalProcessorTime.TotalMilliseconds, p.WorkingSet64 / 1048576.0); }
            catch { /* protected system process: skip, we never ask for more rights */ }
            finally { p.Dispose(); }
        }
        return d;
    }

    static readonly string? NvidiaSmi = new[] {
        Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe") }
        .FirstOrDefault(File.Exists);

    /// <summary>Asks the NVIDIA driver's own tool (absolute path under System32) for GPU load. Null on machines without one.</summary>
    public static async Task<(string name, int load, double usedGb, double totalGb, int temp)?> QueryGpuAsync(CancellationToken ct = default)
    {
        if (NvidiaSmi == null) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(NvidiaSmi, "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            if (p == null) return null;
            var line = await p.StandardOutput.ReadLineAsync(ct);
            await p.WaitForExitAsync(ct);
            var f = line?.Split(',').Select(x => x.Trim()).ToArray();
            if (f == null || f.Length < 5) return null;
            return (f[0].Replace("NVIDIA ", "").Replace("GeForce ", ""), int.Parse(f[1]), double.Parse(f[2]) / 1024, double.Parse(f[3]) / 1024, int.Parse(f[4]));
        }
        catch { return null; }
    }
}
