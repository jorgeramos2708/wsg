using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Integrity;

/// <summary>
/// Verifica archivos protegidos con SFC /verifyonly. Nunca usa /scannow.
/// </summary>
public sealed class WindowsSystemFileVerificationCollector : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();

    public string Name => "Verificación de archivos protegidos";
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
            result = await _runner.RunAsync(ReadOnlyDiagnosticCommand.SfcVerifyOnly, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }

        if (result.TimedOut)
            return CollectorResult.Failure(Name, Cost, now, "SFC /verifyonly excedió el tiempo máximo permitido.");

        var output = result.CombinedOutput;
        var evidence = new List<EvidenceRecord>();
        var metrics = new List<MetricSample>
        {
            new($"metric:integrity.sfc.exit_code:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Integrity, "Windows Resource Protection",
                "integrity.sfc.exit_code", result.ExitCode, "code")
        };

        var healthy = ContainsAny(output,
            "did not find any integrity violations",
            "no encontró ninguna infracción de integridad",
            "no encontro ninguna infraccion de integridad");
        var violations = ContainsAny(output,
            "found integrity violations",
            "found corrupt files",
            "encontró infracciones de integridad",
            "encontro infracciones de integridad",
            "encontró archivos dañados",
            "encontro archivos danados");
        var unable = ContainsAny(output,
            "could not perform the requested operation",
            "no pudo realizar la operación solicitada",
            "no pudo realizar la operacion solicitada");

        if (violations)
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                "SFC /verifyonly detectó violaciones de integridad en archivos protegidos. WSG no ejecutó reparaciones."));
        }
        else if (unable || !healthy)
        {
            return CollectorResult.Failure(Name, Cost, now,
                $"SFC /verifyonly no pudo confirmar de forma concluyente la integridad de archivos protegidos (código {result.ExitCode}). La salida puede no ser interpretable en este idioma o la operación puede haber fallado.");
        }

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(DateTime now, EvidenceSeverity severity, string message)
    {
        const string key = "integrity:system-files";
        return new EvidenceRecord(
            $"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "SFC /verifyonly",
            EvidenceCategory.Integrity,
            "Windows Resource Protection",
            "Archivos protegidos",
            severity.ToString(),
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
