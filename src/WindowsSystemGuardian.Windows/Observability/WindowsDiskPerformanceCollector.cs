using System.Management;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsDiskPerformanceCollector : IObservabilityCollector
{
    public string Name => "Rendimiento de disco";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(1);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var metrics = new List<MetricSample>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PercentDiskTime, AvgDiskQueueLength, DiskBytesPersec, DiskReadsPersec, DiskWritesPersec FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk");
        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = item["Name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                continue;

            var busy = Math.Clamp(ReadDouble(item, "PercentDiskTime"), 0d, 100d);
            metrics.Add(Metric(name, "storage.busy_percent", busy, "%", timestamp));
            metrics.Add(Metric(name, "storage.queue.length", ReadDouble(item, "AvgDiskQueueLength"), "count", timestamp));
            metrics.Add(Metric(name, "storage.io.bytes_per_sec", ReadDouble(item, "DiskBytesPersec"), "bytes/s", timestamp));
            metrics.Add(Metric(name, "storage.reads_per_sec", ReadDouble(item, "DiskReadsPersec"), "ops/s", timestamp));
            metrics.Add(Metric(name, "storage.writes_per_sec", ReadDouble(item, "DiskWritesPersec"), "ops/s", timestamp));
        }

        return CollectorResult.Success(Name, Cost, timestamp, [], metrics);
    }

    private static double ReadDouble(ManagementObject item, string property)
    {
        try
        {
            var value = item[property];
            return value is null ? 0d : Convert.ToDouble(value);
        }
        catch
        {
            return 0d;
        }
    }

    private static MetricSample Metric(string entity, string name, double value, string unit, DateTime timestamp) =>
        new($"metric:{name}:{Normalize(entity)}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Performance,
            entity,
            name,
            value,
            unit);

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(64).ToArray();
        return chars.Length == 0 ? "disk" : new string(chars).ToLowerInvariant();
    }
}
