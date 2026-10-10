using System.ComponentModel;
using System.Runtime.InteropServices;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsPerformanceCollector : IObservabilityCollector
{
    public string Name => "Rendimiento";
    public DiagnosticCost Cost => DiagnosticCost.VeryLow;
    public TimeSpan RecommendedInterval => TimeSpan.FromSeconds(30);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var cpu = await ReadCpuPercentAsync(cancellationToken);
        var memory = ReadMemory();
        var timestamp = DateTime.UtcNow;
        var commitUsed = Math.Max(0d, memory.TotalCommitBytes - memory.AvailableCommitBytes);
        var commitPercent = memory.TotalCommitBytes <= 0
            ? 0d
            : Math.Clamp(commitUsed / memory.TotalCommitBytes * 100d, 0d, 100d);

        var metrics = new List<MetricSample>
        {
            Metric("system.cpu.percent", "CPU", cpu, "%", timestamp),
            Metric("system.memory.percent", "Memoria", memory.LoadPercent, "%", timestamp),
            Metric("system.memory.total_bytes", "Memoria", memory.TotalPhysicalBytes, "bytes", timestamp),
            Metric("system.memory.available_bytes", "Memoria", memory.AvailablePhysicalBytes, "bytes", timestamp),
            Metric("system.commit.percent", "Memoria", commitPercent, "%", timestamp),
            Metric("system.commit.used_bytes", "Memoria", commitUsed, "bytes", timestamp),
            Metric("system.commit.limit_bytes", "Memoria", memory.TotalCommitBytes, "bytes", timestamp),
            Metric("system.uptime.seconds", "Sistema", TimeSpan.FromMilliseconds(System.Environment.TickCount64).TotalSeconds, "s", timestamp)
        };

        var evidence = new List<EvidenceRecord>();
        if (memory.LoadPercent >= 90)
        {
            evidence.Add(new EvidenceRecord(
                ObservationId("performance:memory", timestamp),
                timestamp,
                Name,
                EvidenceCategory.Performance,
                "Memoria",
                "Uso",
                $"{memory.LoadPercent:F0}",
                "%",
                EvidenceSeverity.Informational,
                DetectionConfidence.High,
                "performance:memory-pressure-sample",
                $"El uso de memoria actual es {memory.LoadPercent:F0}%. Se requieren varias muestras para confirmar presión sostenida."));
        }

        if (commitPercent >= 90)
        {
            evidence.Add(new EvidenceRecord(
                ObservationId("performance:commit", timestamp),
                timestamp,
                Name,
                EvidenceCategory.Performance,
                "Commit de memoria",
                "Commit",
                $"{commitPercent:F0}",
                "%",
                EvidenceSeverity.Informational,
                DetectionConfidence.High,
                "performance:commit-pressure-sample",
                $"El compromiso de memoria alcanzó {commitPercent:F0}% del límite disponible."));
        }

        return CollectorResult.Success(Name, Cost, started, evidence, metrics);
    }

    private static async Task<double> ReadCpuPercentAsync(CancellationToken cancellationToken)
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemTimes falló al iniciar la muestra de CPU.");

        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemTimes falló al finalizar la muestra de CPU.");

        var idleDelta = ToUInt64(idle2) - ToUInt64(idle1);
        var kernelDelta = ToUInt64(kernel2) - ToUInt64(kernel1);
        var userDelta = ToUInt64(user2) - ToUInt64(user1);
        var total = kernelDelta + userDelta;
        if (total == 0)
            throw new InvalidOperationException("La muestra de CPU no produjo un intervalo medible.");

        return Math.Clamp((1d - idleDelta / (double)total) * 100d, 0d, 100d);
    }

    private static MemoryReading ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalMemoryStatusEx no pudo leer la memoria del sistema.");

        return new MemoryReading(
            status.MemoryLoad,
            status.TotalPhys,
            status.AvailPhys,
            status.TotalPageFile,
            status.AvailPageFile);
    }

    private static MetricSample Metric(
        string name,
        string entity,
        double value,
        string unit,
        DateTime timestamp) =>
        new($"metric:{name}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Performance,
            entity,
            name,
            value,
            unit);

    private static string ObservationId(string prefix, DateTime timestamp) =>
        $"evidence:{prefix}:{timestamp:yyyyMMddHHmmssfffffff}";

    private static ulong ToUInt64(FileTime value) =>
        ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    private sealed record MemoryReading(
        double LoadPercent,
        double TotalPhysicalBytes,
        double AvailablePhysicalBytes,
        double TotalCommitBytes,
        double AvailableCommitBytes);
}
