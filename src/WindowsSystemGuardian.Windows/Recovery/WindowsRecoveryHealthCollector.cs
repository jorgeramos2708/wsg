using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Diagnostics;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Recovery;

/// <summary>
/// Consulta Windows Recovery Environment con REAgentC /info. Solo lectura.
/// </summary>
public sealed class WindowsRecoveryHealthCollector : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();

    public string Name => "Preparación de recuperación WinRE";
    public DiagnosticCost Cost => DiagnosticCost.Medium;
    public TimeSpan RecommendedInterval => TimeSpan.FromHours(12);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        ReadOnlyDiagnosticProcessResult result;
        try
        {
            result = await _runner.RunAsync(ReadOnlyDiagnosticCommand.ReAgentInfo, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }

        if (result.TimedOut)
            return CollectorResult.Failure(Name, Cost, now, "REAgentC /info excedió el tiempo máximo permitido.");

        var output = result.CombinedOutput;
        var status = WinReStatusInterpreter.Parse(output);
        var enabled = status == WinReStatus.Enabled;
        var disabled = status == WinReStatus.Disabled;

        if (result.ExitCode != 0)
            return CollectorResult.Failure(Name, Cost, now,
                $"REAgentC /info terminó con código {result.ExitCode}; WSG no pudo consultar WinRE.");

        if (!enabled && !disabled)
            return CollectorResult.Failure(Name, Cost, now,
                "REAgentC /info respondió, pero WSG no pudo interpretar el estado de WinRE de forma concluyente en el idioma/formato actual. No se realizaron cambios.");

        var metrics = new List<MetricSample>
        {
            new($"metric:recovery.winre.enabled:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Recovery, "Windows RE",
                "recovery.winre.enabled", enabled ? 1 : 0, "bool")
        };
        var evidence = new List<EvidenceRecord>();

        if (disabled)
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                "Windows Recovery Environment (WinRE) está deshabilitado. Esto reduce las opciones de recuperación local si Windows no inicia correctamente."));
        }

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(DateTime now, EvidenceSeverity severity, string message)
    {
        const string key = "recovery:winre";
        return new EvidenceRecord(
            $"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "REAgentC /info",
            EvidenceCategory.Recovery,
            "Windows RE",
            "Estado de recuperación",
            severity.ToString(),
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);
    }

}
