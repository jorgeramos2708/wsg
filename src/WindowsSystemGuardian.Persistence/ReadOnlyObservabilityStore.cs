using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Persistence;

/// <summary>
/// Vista de solo lectura del store autoritativo del Agent.
/// La UI puede ejecutar capturas ad-hoc en memoria sin competir como segundo escritor.
/// </summary>
public sealed class ReadOnlyObservabilityStore(IObservabilityStore inner) : IObservabilityStore
{
    public string RootPath => inner.RootPath;
    public bool IsReadOnly => true;

    public Task AppendCollectorResultAsync(
        CollectorResult result,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AppendFindingsAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AppendIncidentsAsync(
        IReadOnlyList<Incident> incidents,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<EvidenceRecord>> LoadEvidenceSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        inner.LoadEvidenceSinceAsync(sinceUtc, cancellationToken);

    public Task<IReadOnlyList<MetricSample>> LoadMetricsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        inner.LoadMetricsSinceAsync(sinceUtc, cancellationToken);

    public Task<IReadOnlyList<Finding>> LoadFindingsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        inner.LoadFindingsSinceAsync(sinceUtc, cancellationToken);

    public Task<IReadOnlyList<Incident>> LoadIncidentsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        inner.LoadIncidentsSinceAsync(sinceUtc, cancellationToken);

    public Task EnforceRetentionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
