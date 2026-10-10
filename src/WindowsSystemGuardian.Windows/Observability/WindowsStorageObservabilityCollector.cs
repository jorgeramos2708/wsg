using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Storage;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Health;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsStorageObservabilityCollector(
    IVolumeProvider volumeProvider,
    StorageHealthEvaluator healthEvaluator) : IObservabilityCollector
{
    public string Name => "Almacenamiento";
    public DiagnosticCost Cost => DiagnosticCost.VeryLow;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(1);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;
        var volumes = await volumeProvider.GetVolumesAsync(cancellationToken);
        var evidence = new List<EvidenceRecord>();
        var metrics = new List<MetricSample>();

        foreach (var volume in volumes.Where(v =>
                     v.TotalBytes > 0 &&
                     v.Purpose is VolumePurpose.System or VolumePurpose.Data))
        {
            var entity = volume.DisplayName;
            metrics.Add(Metric(entity, "storage.free_bytes", volume.FreeBytes, "bytes", timestamp));
            metrics.Add(Metric(entity, "storage.free_percent", volume.PercentFree, "%", timestamp));
            metrics.Add(Metric(entity, "storage.used_percent", volume.PercentUsed, "%", timestamp));

            var health = healthEvaluator.EvaluateVolume(volume);
            if (health is not (HealthStatus.Warning or HealthStatus.Critical))
                continue;

            var severity = health == HealthStatus.Critical
                ? EvidenceSeverity.Critical
                : EvidenceSeverity.Warning;
            var freeGiB = volume.FreeBytes / (1024d * 1024d * 1024d);
            var correlationKey = $"storage:capacity:{Normalize(volume.Id)}";

            evidence.Add(new EvidenceRecord(
                ObservationId(correlationKey, severity, timestamp),
                timestamp,
                Name,
                EvidenceCategory.Storage,
                entity,
                "Capacidad disponible",
                $"{volume.PercentFree:F1}",
                "% libre",
                severity,
                DetectionConfidence.High,
                correlationKey,
                $"{entity} tiene {freeGiB:F1} GB disponibles ({volume.PercentFree:F1}% libre)."));
        }

        return CollectorResult.Success(Name, Cost, timestamp, evidence, metrics);
    }

    private static MetricSample Metric(
        string entity,
        string name,
        double value,
        string unit,
        DateTime timestamp) =>
        new($"metric:{name}:{Normalize(entity)}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Storage,
            entity,
            name,
            value,
            unit);

    private static string ObservationId(
        string prefix,
        EvidenceSeverity severity,
        DateTime timestamp) =>
        $"evidence:{prefix}:{severity}:{timestamp:yyyyMMddHHmmssfffffff}";

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(64).ToArray();
        return chars.Length == 0 ? "volume" : new string(chars).ToLowerInvariant();
    }
}
