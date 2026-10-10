using WindowsSystemGuardian.Contracts.Incidents;

namespace WindowsSystemGuardian.Contracts.Observability;

public sealed record ObservabilitySnapshot(
    DateTime CapturedAtUtc,
    IReadOnlyList<CollectorResult> Collectors,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<MetricSample> Metrics,
    IReadOnlyList<Finding> Findings,
    int HealthScore,
    bool PersistenceDegraded = false,
    string? PersistenceError = null)
{
    public IReadOnlyList<Incident> Incidents { get; init; } = [];
    public int WarningCount => Incidents.Count > 0
        ? Incidents.Count(i =>
            i.State != IncidentState.Resolved &&
            i.Severity == IncidentSeverity.Warning)
        : Findings.Count(f =>
            f.State == FindingLifecycleState.Active &&
            f.Severity == FindingSeverity.Warning);

    public int CriticalCount => Incidents.Count > 0
        ? Incidents.Count(i =>
            i.State != IncidentState.Resolved &&
            i.Severity == IncidentSeverity.Critical)
        : Findings.Count(f =>
            f.State == FindingLifecycleState.Active &&
            f.Severity == FindingSeverity.Critical);

    public int SuccessfulCollectorCount => Collectors.Count(c => c.Succeeded);
    public int FailedCollectorCount => Collectors.Count(c => !c.Succeeded);
}
