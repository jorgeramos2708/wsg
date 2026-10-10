using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Observability;

namespace WindowsSystemGuardian.Core.Scheduling;

public sealed class DiagnosticScheduler(
    ObservabilityService observabilityService,
    ScanLoadController loadController)
{
    private readonly Dictionary<string, DateTime> _nextRunUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ObservabilitySnapshot?> RunDueAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var due = observabilityService.Collectors
                .Where(c => c.Cost <= loadController.MaximumAllowedCost)
                .Where(c => !_nextRunUtc.TryGetValue(c.Name, out var next) || next <= now)
                .ToList();

            if (due.Count == 0)
                return null;

            foreach (var collector in due)
                _nextRunUtc[collector.Name] = now + collector.RecommendedInterval;

            var snapshot = await observabilityService.CaptureCollectorsAsync(due, cancellationToken);
            loadController.UpdateFrom(snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }
}
