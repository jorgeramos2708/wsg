using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Servicing;

/// <summary>
/// Escaneo profundo manual mediante DISM /ScanHealth. No repara la imagen.
/// </summary>
public sealed class WindowsComponentStoreScanCollector : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();

    public string Name => "Escaneo profundo del almacén de componentes";
    public DiagnosticCost Cost => DiagnosticCost.High;
    public TimeSpan RecommendedInterval => TimeSpan.FromDays(1);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!WindowsPrivilegeContext.IsAdministrator())
            return CollectorResult.Failure(Name, Cost, now, "Este diagnóstico requiere privilegios administrativos. Ejecútelo mediante Windows System Guardian Agent o, en una fase posterior, mediante PrivilegedHost/UAC.");
        ReadOnlyDiagnosticProcessResult result;
        try
        {
            result = await _runner.RunAsync(ReadOnlyDiagnosticCommand.DismScanHealth, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }

        if (result.TimedOut)
            return CollectorResult.Failure(Name, Cost, now, "DISM /ScanHealth excedió el tiempo máximo permitido.");

        var output = result.CombinedOutput;
        var evidence = new List<EvidenceRecord>();
        var metrics = new List<MetricSample>
        {
            new($"metric:servicing.component_store.scan_exit_code:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Servicing, "Component Store",
                "servicing.component_store.scan_exit_code", result.ExitCode, "code")
        };

        if (Contains(output, "The component store cannot be repaired"))
            evidence.Add(Evidence(now, EvidenceSeverity.Critical,
                "DISM /ScanHealth encontró corrupción que Windows reporta como no reparable."));
        else if (Contains(output, "The component store is repairable") ||
                 Contains(output, "The component store is corrupted"))
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                "DISM /ScanHealth confirmó corrupción reparable del Component Store. WSG no ejecutó /RestoreHealth."));
        else if (result.ExitCode != 0 && !Contains(output, "No component store corruption detected"))
            return CollectorResult.Failure(Name, Cost, now,
                $"DISM /ScanHealth terminó con código {result.ExitCode}; no se pudo confirmar un estado saludable.");

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(DateTime now, EvidenceSeverity severity, string message)
    {
        const string key = "servicing:component-store-deep";
        return new EvidenceRecord(
            $"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "DISM /ScanHealth",
            EvidenceCategory.Servicing,
            "Component Store",
            "Escaneo profundo",
            severity.ToString(),
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
