using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Incidents;

namespace WindowsSystemGuardian.Core.Observability;

public sealed class ObservabilityService(
    IEnumerable<IObservabilityCollector> collectors,
    IObservabilityStore store,
    FindingEngine findingEngine,
    IncidentEngine incidentEngine,
    IncidentLifecycleTracker incidentLifecycleTracker)
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetentionCadence = TimeSpan.FromHours(6);

    private readonly IReadOnlyList<IObservabilityCollector> _collectors = collectors.ToList();
    private readonly MetricHistoryBuffer _history = new(HistoryWindow);
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private DateTime _nextRetentionUtc = DateTime.MinValue;

    public IReadOnlyList<IObservabilityCollector> Collectors => _collectors;

    public Task<ObservabilitySnapshot> CaptureAsync(
        DiagnosticCost maximumCost = DiagnosticCost.Medium,
        CancellationToken cancellationToken = default) =>
        CaptureCollectorsAsync(
            _collectors.Where(c => c.Cost <= maximumCost).ToList(),
            cancellationToken);

    // Compatibilidad de fuente con 0.3.x: permite pasar CancellationToken como
    // primer argumento sin confundirlo con DiagnosticCost.
    public Task<ObservabilitySnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        CaptureAsync(DiagnosticCost.Medium, cancellationToken);

    public async Task<ObservabilitySnapshot> CaptureCollectorsAsync(
        IReadOnlyList<IObservabilityCollector> selectedCollectors,
        CancellationToken cancellationToken = default)
    {
        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var persistenceErrors = new List<string>();

            await InitializeHistoryBestEffortAsync(now, persistenceErrors, cancellationToken);
            await InitializeIncidentsBestEffortAsync(now, persistenceErrors, cancellationToken);

            var tasks = selectedCollectors
                .Select(c => SafeCollectAsync(c, cancellationToken))
                .ToList();
            var results = (await Task.WhenAll(tasks)).ToList();

            var evidence = results.SelectMany(r => r.Evidence).ToList();
            var metrics = results.SelectMany(r => r.Metrics).ToList();

            _history.AddRange(metrics, now);
            var history = _history.Snapshot(now);
            var findings = findingEngine.Evaluate(evidence, history, now);
            var rawIncidents = incidentEngine.Build(findings, evidence, history);
            var successfulCollectors = store.IsReadOnly
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : results
                    .Where(r => r.Succeeded)
                    .Select(r => r.CollectorName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var incidentUpdates = incidentLifecycleTracker.Apply(rawIncidents, successfulCollectors, now);
            var incidents = incidentLifecycleTracker.Snapshot()
                .Where(i => i.State != WindowsSystemGuardian.Contracts.Incidents.IncidentState.Resolved)
                .ToList();

            foreach (var result in results)
            {
                try
                {
                    await store.AppendCollectorResultAsync(result, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    persistenceErrors.Add($"{result.CollectorName}: {ex.GetBaseException().Message}");
                }
            }

            try
            {
                await store.AppendFindingsAsync(findings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                persistenceErrors.Add($"Hallazgos: {ex.GetBaseException().Message}");
            }

            try
            {
                await store.AppendIncidentsAsync(incidentUpdates, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                persistenceErrors.Add($"Incidentes: {ex.GetBaseException().Message}");
            }

            if (now >= _nextRetentionUtc)
            {
                try
                {
                    await store.EnforceRetentionAsync(cancellationToken);
                    _nextRetentionUtc = now + RetentionCadence;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    persistenceErrors.Add($"Retención: {ex.GetBaseException().Message}");
                    _nextRetentionUtc = now + TimeSpan.FromMinutes(30);
                }
            }

            var score = CalculateHealthScore(findings, incidents, results, persistenceErrors.Count > 0);
            return new ObservabilitySnapshot(
                now,
                results,
                evidence,
                metrics,
                findings,
                score,
                persistenceErrors.Count > 0,
                persistenceErrors.Count == 0
                    ? null
                    : string.Join(" | ", persistenceErrors.Distinct(StringComparer.OrdinalIgnoreCase).Take(4)))
            {
                Incidents = incidents
            };
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task InitializeHistoryBestEffortAsync(
        DateTime now,
        List<string> persistenceErrors,
        CancellationToken cancellationToken)
    {
        if (_history.IsInitialized)
            return;

        try
        {
            var initial = await store.LoadMetricsSinceAsync(now - HistoryWindow, cancellationToken);
            _history.Initialize(initial, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _history.Initialize([], now);
            persistenceErrors.Add($"Lectura histórica: {ex.GetBaseException().Message}");
        }
    }

    private async Task InitializeIncidentsBestEffortAsync(
        DateTime now,
        List<string> persistenceErrors,
        CancellationToken cancellationToken)
    {
        if (incidentLifecycleTracker.IsInitialized)
            return;

        try
        {
            var previous = await store.LoadIncidentsSinceAsync(now.AddDays(-2), cancellationToken);
            incidentLifecycleTracker.Initialize(previous);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            incidentLifecycleTracker.Initialize([]);
            persistenceErrors.Add($"Lectura de incidentes: {ex.GetBaseException().Message}");
        }
    }

    private static async Task<CollectorResult> SafeCollectAsync(
        IObservabilityCollector collector,
        CancellationToken cancellationToken)
    {
        try
        {
            return await collector.CollectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CollectorResult.Failure(
                collector.Name,
                collector.Cost,
                DateTime.UtcNow,
                ex.GetBaseException().Message);
        }
    }

    private static int CalculateHealthScore(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<WindowsSystemGuardian.Contracts.Incidents.Incident> incidents,
        IReadOnlyList<CollectorResult> collectors,
        bool persistenceDegraded)
    {
        var score = 100;

        // Una captura parcial puede no volver a emitir el Finding que originó un
        // incidente, pero ese incidente sigue abierto hasta ser verificado. El score
        // debe reflejar ese estado y no aparentar salud por falta de cobertura.
        if (incidents.Count > 0)
        {
            score -= incidents.Count(i =>
                i.State == WindowsSystemGuardian.Contracts.Incidents.IncidentState.Active &&
                i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Warning) * 8;
            score -= incidents.Count(i =>
                i.State == WindowsSystemGuardian.Contracts.Incidents.IncidentState.Active &&
                i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Critical) * 22;
            score -= incidents.Count(i =>
                i.State == WindowsSystemGuardian.Contracts.Incidents.IncidentState.Monitoring &&
                i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Warning) * 3;
            score -= incidents.Count(i =>
                i.State == WindowsSystemGuardian.Contracts.Incidents.IncidentState.Monitoring &&
                i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Critical) * 8;
        }
        else
        {
            // Fallback para la primera captura antes de que exista estado de incidentes.
            score -= findings.Count(f =>
                f.State == FindingLifecycleState.Active &&
                f.Severity == FindingSeverity.Warning) * 8;
            score -= findings.Count(f =>
                f.State == FindingLifecycleState.Active &&
                f.Severity == FindingSeverity.Critical) * 22;
        }

        // Los findings de la propia observabilidad (por ejemplo cobertura truncada
        // del Event Log) no se convierten en incidentes del sistema, pero sí deben
        // reducir moderadamente la confianza/score de la captura.
        score -= findings.Count(f =>
            f.Category == EvidenceCategory.Observability &&
            f.Severity == FindingSeverity.Warning) * 4;
        score -= findings.Count(f =>
            f.Category == EvidenceCategory.Observability &&
            f.Severity == FindingSeverity.Critical) * 10;

        score -= collectors.Count(c => !c.Succeeded) * 4;

        if (persistenceDegraded)
            score -= 3;

        return Math.Clamp(score, 0, 100);
    }
}
