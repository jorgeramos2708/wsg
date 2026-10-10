using System.Text.RegularExpressions;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
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

        var unhealthy = states.Count(state => state != 1);
        var metrics = new List<MetricSample>
        {
            new($"metric:backup.vss.writers_total:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_total", states.Count, "count"),
            new($"metric:backup.vss.writers_unhealthy:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Backup, "VSS",
                "backup.vss.writers_unhealthy", unhealthy, "count")
        };
        var evidence = new List<EvidenceRecord>();

        if (result.ExitCode != 0)
            return CollectorResult.Failure(Name, Cost, now,
                $"vssadmin list writers terminó con código {result.ExitCode}; WSG no pudo confirmar la preparación de VSS.");

        if (states.Count == 0)
            return CollectorResult.Failure(Name, Cost, now,
                "vssadmin no produjo estados de writers interpretables. WSG no atribuye esto a un fallo VSS sin evidencia suficiente.");

        if (unhealthy > 0)
        {
            evidence.Add(Evidence(now, EvidenceSeverity.Warning,
                $"VSS reporta {unhealthy} writer(s) fuera del estado Stable entre {states.Count} detectados. WSG no reinició servicios ni modificó snapshots."));
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
