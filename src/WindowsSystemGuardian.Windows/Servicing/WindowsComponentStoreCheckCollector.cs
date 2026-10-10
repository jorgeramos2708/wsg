using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Servicing;

/// <summary>
/// Comprobación rápida de Component Store mediante DISM /CheckHealth.
/// Es una operación de diagnóstico; no usa /RestoreHealth ni cleanup destructivo.
/// </summary>
public sealed class WindowsComponentStoreCheckCollector : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();

    public string Name => "Salud del almacén de componentes";
    public DiagnosticCost Cost => DiagnosticCost.Medium;
    public TimeSpan RecommendedInterval => TimeSpan.FromHours(6);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!WindowsPrivilegeContext.IsAdministrator())
            return CollectorResult.Failure(Name, Cost, now, "Este diagnóstico requiere privilegios administrativos. Ejecútelo mediante Windows System Guardian Agent o, en una fase posterior, mediante PrivilegedHost/UAC.");
        ReadOnlyDiagnosticProcessResult result;
        try
        {
            result = await _runner.RunAsync(ReadOnlyDiagnosticCommand.DismCheckHealth, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }

        if (result.TimedOut)
            return CollectorResult.Failure(Name, Cost, now, "DISM /CheckHealth excedió el tiempo máximo permitido.");

        var output = result.CombinedOutput;
        var metrics = new List<MetricSample>
        {
            new($"metric:servicing.component_store.exit_code:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Servicing, "Component Store",
                "servicing.component_store.exit_code", result.ExitCode, "code")
        };
        var evidence = new List<EvidenceRecord>();

        if (Contains(output, "The component store cannot be repaired") ||
            Contains(output, "component store corruption was detected but could not be repaired"))
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Critical,
                "El almacén de componentes de Windows está marcado como no reparable. WSG no intentó modificarlo."));
        }
        else if (Contains(output, "The component store is repairable") ||
                 Contains(output, "The component store is corrupted"))
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                "DISM detectó corrupción reparable en el almacén de componentes. La reparación permanece bloqueada hasta una fase de remediación controlada."));
        }
        else if (result.ExitCode != 0 && !Contains(output, "No component store corruption detected"))
        {
            return CollectorResult.Failure(Name, Cost, now,
                $"DISM /CheckHealth terminó con código {result.ExitCode}; la salud del Component Store no pudo confirmarse.");
        }

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(DateTime now, EvidenceSeverity severity, string message)
    {
        const string key = "servicing:component-store";
        return new EvidenceRecord(
            $"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "DISM /CheckHealth",
            EvidenceCategory.Servicing,
            "Component Store",
            "Salud del almacén de componentes",
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
