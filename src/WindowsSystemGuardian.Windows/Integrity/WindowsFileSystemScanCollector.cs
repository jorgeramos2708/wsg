using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Storage;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Integrity;

/// <summary>
/// Diagnóstico profundo manual. Ejecuta únicamente CHKDSK /scan sobre volúmenes
/// NTFS locales con letra. /scan realiza un análisis online y no contiene switches
/// de reparación como /f, /r, /spotfix u /offlinescanandfix.
/// </summary>
public sealed class WindowsFileSystemScanCollector(
    IVolumeProvider volumeProvider) : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();

    public string Name => "Escaneo profundo del sistema de archivos";
    public DiagnosticCost Cost => DiagnosticCost.High;
    public TimeSpan RecommendedInterval => TimeSpan.FromDays(7);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!WindowsPrivilegeContext.IsAdministrator())
            return CollectorResult.Failure(Name, Cost, now, "Este diagnóstico requiere privilegios administrativos. Ejecútelo mediante Windows System Guardian Agent o, en una fase posterior, mediante PrivilegedHost/UAC.");
        var evidence = new List<EvidenceRecord>();
        var metrics = new List<MetricSample>();
        var volumes = await volumeProvider.GetVolumesAsync(cancellationToken);
        var targets = volumes
            .Where(v => v.Purpose is VolumePurpose.System or VolumePurpose.Data)
            .Where(v => string.Equals(v.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
            .Where(v => !string.IsNullOrWhiteSpace(v.DriveLetter))
            .ToList();

        if (targets.Count == 0)
            return CollectorResult.Success(Name, Cost, now, evidence, metrics);

        foreach (var volume in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyDiagnosticProcessResult result;
            try
            {
                result = await _runner.RunAsync(
                    ReadOnlyDiagnosticCommand.ChkdskScan,
                    volume.DriveLetter,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CollectorResult.Failure(Name, Cost, now,
                    $"No se pudo ejecutar CHKDSK /scan en {volume.DisplayName}: {ex.GetBaseException().Message}");
            }

            metrics.Add(new MetricSample(
                $"metric:filesystem.scan.exit_code:{Normalize(volume.Id)}:{now:yyyyMMddHHmmssfff}",
                now,
                EvidenceCategory.Integrity,
                volume.DisplayName,
                "filesystem.scan.exit_code",
                result.ExitCode,
                "code"));

            var key = $"integrity:filesystem-scan:{Normalize(volume.Id)}";
            if (result.TimedOut)
            {
                evidence.Add(NewEvidence(key, now, volume.DisplayName, EvidenceSeverity.Warning,
                    "CHKDSK /scan excedió el tiempo máximo de diagnóstico. WSG no considera la verificación completa."));
                continue;
            }

            // Microsoft documenta 0 = sin errores; 2/3 pueden indicar que no se
            // realizaron reparaciones o que el volumen no pudo verificarse.
            if (result.ExitCode == 0)
                continue;

            var severity = result.ExitCode == 3
                ? EvidenceSeverity.Critical
                : EvidenceSeverity.Warning;
            evidence.Add(NewEvidence(key, now, volume.DisplayName, severity,
                $"CHKDSK /scan terminó con código {result.ExitCode}. El diagnóstico detectó una condición que requiere revisión; WSG no ejecutó ninguna reparación."));
        }

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord NewEvidence(
        string key,
        DateTime now,
        string entity,
        EvidenceSeverity severity,
        string message) =>
        new($"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "CHKDSK /scan",
            EvidenceCategory.Integrity,
            entity,
            "Escaneo de filesystem",
            "scan",
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "volume" : new string(chars).ToLowerInvariant();
    }
}
