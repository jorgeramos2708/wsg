using System.Management;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Storage;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Integrity;

/// <summary>
/// Comprobación ligera y de solo lectura del estado expuesto por Win32_Volume.
/// No invoca CHKDSK ni modifica el volumen.
/// </summary>
public sealed class WindowsFileSystemHealthCollector(
    IVolumeProvider volumeProvider) : IObservabilityCollector
{
    public string Name => "Integridad del sistema de archivos";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(5);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var inventory = await volumeProvider.GetVolumesAsync(cancellationToken);
        var eligible = inventory
            .Where(v => v.Purpose is VolumePurpose.System or VolumePurpose.Data)
            .ToDictionary(v => v.Id, StringComparer.OrdinalIgnoreCase);

        var evidence = new List<EvidenceRecord>();
        var metrics = new List<MetricSample>();

        await Task.Run(() =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriveLetter, Name, FileSystem, DirtyBitSet, Status, ErrorDescription, LastErrorCode FROM Win32_Volume WHERE DriveType = 3");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = item["DeviceID"]?.ToString();
                if (string.IsNullOrWhiteSpace(id) || !eligible.TryGetValue(id, out var volume))
                    continue;

                var entity = volume.DisplayName;
                var fileSystem = item["FileSystem"]?.ToString() ?? volume.FileSystem;
                var dirty = item["DirtyBitSet"] is bool value && value;
                var rawStatus = item["Status"]?.ToString();
                var status = string.IsNullOrWhiteSpace(rawStatus) ? "No disponible" : rawStatus;
                var lastError = item["LastErrorCode"] is null ? 0 : Convert.ToUInt32(item["LastErrorCode"]);
                var errorDescription = item["ErrorDescription"]?.ToString() ?? string.Empty;

                metrics.Add(Metric(entity, "filesystem.dirty", dirty ? 1 : 0, "bool", now));
                metrics.Add(Metric(entity, "filesystem.last_error_code", lastError, "code", now));

                if (dirty)
                {
                    var severity = volume.Purpose == VolumePurpose.System
                        ? EvidenceSeverity.Critical
                        : EvidenceSeverity.Warning;
                    var key = $"integrity:filesystem-dirty:{Normalize(volume.Id)}";
                    evidence.Add(Evidence(
                        key,
                        now,
                        entity,
                        "Dirty bit",
                        "1",
                        severity,
                        $"{entity} ({fileSystem}) tiene el dirty bit establecido. Windows considera que el sistema de archivos puede estar en un estado inconsistente y requiere una comprobación soportada."));
                }

                if (lastError != 0 || (!string.IsNullOrWhiteSpace(rawStatus) &&
                    !rawStatus.Equals("OK", StringComparison.OrdinalIgnoreCase)))
                {
                    var key = $"integrity:filesystem-status:{Normalize(volume.Id)}";
                    var detail = string.IsNullOrWhiteSpace(errorDescription)
                        ? status
                        : $"{status}: {errorDescription}";
                    evidence.Add(Evidence(
                        key,
                        now,
                        entity,
                        "Estado WMI del volumen",
                        lastError.ToString(),
                        EvidenceSeverity.Warning,
                        $"{entity} reporta un estado de volumen que requiere revisión ({detail})."));
                }
            }
        }, cancellationToken);

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(
        string key,
        DateTime now,
        string entity,
        string metric,
        string value,
        EvidenceSeverity severity,
        string message) =>
        new($"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "Integridad de filesystem",
            EvidenceCategory.Integrity,
            entity,
            metric,
            value,
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);

    private static MetricSample Metric(string entity, string name, double value, string unit, DateTime now) =>
        new($"metric:{name}:{Normalize(entity)}:{now:yyyyMMddHHmmssfff}",
            now,
            EvidenceCategory.Integrity,
            entity,
            name,
            value,
            unit);

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "volume" : new string(chars).ToLowerInvariant();
    }
}
