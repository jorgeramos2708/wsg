using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Incidents;

namespace WindowsSystemGuardian.Core.Abstractions;

public interface IObservabilityStore
{
    string RootPath { get; }
    bool IsReadOnly => false;

    Task AppendCollectorResultAsync(CollectorResult result, CancellationToken cancellationToken = default);
    Task AppendFindingsAsync(IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default);
    Task AppendIncidentsAsync(IReadOnlyList<Incident> incidents, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EvidenceRecord>> LoadEvidenceSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MetricSample>> LoadMetricsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Finding>> LoadFindingsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Incident>> LoadIncidentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task EnforceRetentionAsync(CancellationToken cancellationToken = default);
}
