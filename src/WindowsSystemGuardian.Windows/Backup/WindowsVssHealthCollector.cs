using System.Text.RegularExpressions;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Diagnostics;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Backup;

/// <summary>
/// Verifica el estado de VSS writers mediante vssadmin list writers. No crea,
/// elimina ni redimensiona shadow copies.
/// </summary>
public sealed class WindowsVssHealthCollector : IObservabilityCollector
{
    private readonly ReadOnlyDiagnosticProcessRunner _runner = new();
    private static readonly Regex StateRegex = new(@"(?im)^\s*[^\r\n]*?\[(?<state>\d+)\][^\r\n]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "Preparación VSS / respaldo";
    public DiagnosticCost Cost => DiagnosticCost.Medium;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(30);

    public async Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!WindowsPrivilegeContext.IsAdministrator())
            return CollectorResult.Failure(Name, Cost, now, "Este diagnóstico requiere privilegios administrativos. Ejecútelo mediante Windows System Guardian Agent o, en una fase posterior, mediante PrivilegedHost/UAC.");
        ReadOnlyDiagnosticProcessResult result;
        try
        {
            result = await _runner.RunAsync(ReadOnlyDiagnosticCommand.VssListWriters, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }

        if (result.TimedOut)
            return CollectorResult.Failure(Name, Cost, now, "vssadmin list writers excedió el tiempo máximo permitido.");

        var states = StateRegex.Matches(result.CombinedOutput)
            .Cast<Match>()
            .Select(m => int.TryParse(m.Groups["state"].Value, out var state) ? state : -1)
            .Where(state => state >= 0)
            .ToList();

        // 1 = Stable; 2-8 = transitorios normales (Waiting*); 0 y >=9 = Failed*/undefined.
        // Un estado transitorio NO es un fallo: demostrado empíricamente con estado [5]
        // en un sistema sano (docs/AUDIT_MSDOC_0.5.0.md, hallazgo H-2).
        var classification = VssWriterStateInterpreter.Classify(states);
        var metrics = new List<MetricSample>
        {
            new($"metric:backup.vss.writers_total:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_total", classification.Total, "count"),
            new($"metric:backup.vss.writers_stable:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_stable", classification.Stable, "count"),
            new($"metric:backup.vss.writers_transient:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_transient", classification.Transient, "count"),
            new($"metric:backup.vss.writers_failed:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_failed", classification.Failed, "count")
        };
        var evidence = new List<EvidenceRecord>();

        if (result.ExitCode != 0)
            return CollectorResult.Failure(Name, Cost, now,
                $"vssadmin list writers terminó con código {result.ExitCode}; WSG no pudo confirmar la preparación de VSS.");

        if (states.Count == 0)
            return CollectorResult.Failure(Name, Cost, now,
                "vssadmin no produjo estados de writers interpretables. WSG no atribuye esto a un fallo VSS sin evidencia suficiente.");

        if (classification.Failed > 0)
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                $"VSS reporta {classification.Failed} writer(s) en estado de fallo entre {classification.Total} detectados. WSG no reinició servicios ni modificó snapshots."));
        }

        if (classification.Transient > 0)
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Informational,
                $"{classification.Transient} writer(s) VSS reportan estados transitorios normales (Waiting*) entre {classification.Total} detectados; ocurren durante actividad de respaldo y no se tratan como fallo."));
        }

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static EvidenceRecord Evidence(DateTime now, EvidenceSeverity severity, string message)
    {
        const string key = "backup:vss-writers";
        return new EvidenceRecord(
            $"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "VSS",
            EvidenceCategory.Backup,
            "VSS Writers",
            "Estado de writers",
            severity.ToString(),
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);
    }
}
