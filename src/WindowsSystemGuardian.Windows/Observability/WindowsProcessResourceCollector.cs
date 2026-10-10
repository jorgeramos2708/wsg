using System.Diagnostics;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

/// <summary>
/// Atribuye consumo de CPU/memoria a procesos sin modificar su estado.
/// Mantiene solo una muestra previa en memoria para calcular CPU por intervalo.
/// </summary>
public sealed class WindowsProcessResourceCollector : IObservabilityCollector
{
    private readonly Dictionary<ProcessIdentity, ProcessCpuState> _previousCpu = [];
    private readonly object _sync = new();

    public string Name => "Procesos";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(1);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var readings = new List<ProcessReading>();
        var currentStates = new Dictionary<ProcessIdentity, ProcessCpuState>();
        var attempted = 0;
        var unreadable = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted++;
                try
                {
                    var startUtc = process.StartTime.ToUniversalTime();
                    var identity = new ProcessIdentity(process.Id, startUtc.Ticks);
                    var totalCpu = process.TotalProcessorTime;
                    var workingSet = process.WorkingSet64;
                    var privateBytes = process.PrivateMemorySize64;
                    var handles = process.HandleCount;
                    var threads = process.Threads.Count;
                    var name = string.IsNullOrWhiteSpace(process.ProcessName)
                        ? $"PID {process.Id}"
                        : process.ProcessName;

                    var cpu = CalculateCpu(identity, totalCpu, timestamp);
                    readings.Add(new ProcessReading(identity, name, cpu, workingSet, privateBytes, handles, threads));
                    currentStates[identity] = new ProcessCpuState(totalCpu, timestamp);
                }
                catch
                {
                    // Procesos protegidos/efímeros pueden desaparecer o negar acceso.
                    unreadable++;
                }
            }
        }

        lock (_sync)
        {
            _previousCpu.Clear();
            foreach (var pair in currentStates)
                _previousCpu[pair.Key] = pair.Value;
        }

        // Garantizar representación de consumidores relevantes en dimensiones
        // distintas. Un proceso con fuga de handles/hilos puede no aparecer entre
        // los mayores consumidores de CPU o working set.
        var selected = readings
            .OrderByDescending(r => r.CpuPercent).Take(4)
            .Concat(readings.OrderByDescending(r => r.WorkingSetBytes).Take(4))
            .Concat(readings.OrderByDescending(r => r.PrivateBytes).Take(4))
            .Concat(readings.OrderByDescending(r => r.HandleCount).Take(4))
            .Concat(readings.OrderByDescending(r => r.ThreadCount).Take(4))
            .GroupBy(r => r.Identity)
            .Select(g => g.First())
            .Take(20)
            .ToList();

        var metrics = new List<MetricSample>
        {
            Metric("process.count", "Procesos", readings.Count, "count", timestamp),
            Metric("process.attempted", "Procesos", attempted, "count", timestamp),
            Metric("process.unreadable", "Procesos", unreadable, "count", timestamp)
        };

        foreach (var reading in selected)
        {
            var startUtc = new DateTime(reading.Identity.StartTicksUtc, DateTimeKind.Utc);
            var entity = $"{reading.Name} (PID {reading.Identity.ProcessId}, inicio UTC {startUtc:yyyy-MM-dd HH:mm:ss})";
            metrics.Add(Metric("process.cpu.percent", entity, reading.CpuPercent, "%", timestamp));
            metrics.Add(Metric("process.memory.working_set_bytes", entity, reading.WorkingSetBytes, "bytes", timestamp));
            metrics.Add(Metric("process.memory.private_bytes", entity, reading.PrivateBytes, "bytes", timestamp));
            metrics.Add(Metric("process.handles", entity, reading.HandleCount, "count", timestamp));
            metrics.Add(Metric("process.threads", entity, reading.ThreadCount, "count", timestamp));
        }

        var evidence = new List<EvidenceRecord>();
        if (attempted >= 10 && unreadable > attempted / 2)
        {
            evidence.Add(new EvidenceRecord(
                $"evidence:process-coverage:{timestamp:yyyyMMddHHmmssfffffff}",
                timestamp,
                Name,
                EvidenceCategory.Observability,
                "Procesos",
                "Cobertura",
                $"{readings.Count}/{attempted}",
                "procesos legibles",
                EvidenceSeverity.Warning,
                WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.High,
                "observability:process-coverage",
                $"WSG solo pudo leer métricas de {readings.Count} de {attempted} procesos. La atribución de consumo puede estar incompleta."));
        }

        return CollectorResult.Success(Name, Cost, timestamp, evidence, metrics);
    }

    private double CalculateCpu(ProcessIdentity identity, TimeSpan currentCpu, DateTime now)
    {
        lock (_sync)
        {
            if (!_previousCpu.TryGetValue(identity, out var previous))
                return 0d;

            var elapsedMs = (now - previous.TimestampUtc).TotalMilliseconds;
            var cpuMs = (currentCpu - previous.TotalProcessorTime).TotalMilliseconds;
            if (elapsedMs <= 0 || cpuMs < 0)
                return 0d;

            return Math.Clamp(
                cpuMs / elapsedMs / Math.Max(1, System.Environment.ProcessorCount) * 100d,
                0d,
                100d);
        }
    }

    private static MetricSample Metric(
        string name,
        string entity,
        double value,
        string unit,
        DateTime timestamp) =>
        new($"metric:{name}:{Normalize(entity)}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Performance,
            entity,
            name,
            value,
            unit);

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "process" : new string(chars).ToLowerInvariant();
    }

    private readonly record struct ProcessIdentity(int ProcessId, long StartTicksUtc);
    private sealed record ProcessCpuState(TimeSpan TotalProcessorTime, DateTime TimestampUtc);
    private sealed record ProcessReading(
        ProcessIdentity Identity,
        string Name,
        double CpuPercent,
        long WorkingSetBytes,
        long PrivateBytes,
        int HandleCount,
        int ThreadCount);
}
