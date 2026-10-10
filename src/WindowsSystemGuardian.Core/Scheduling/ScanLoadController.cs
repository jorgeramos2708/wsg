using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Scheduling;

public sealed class ScanLoadController
{
    private DiagnosticCost _maximumAllowedCost = DiagnosticCost.Medium;

    public DiagnosticCost MaximumAllowedCost => _maximumAllowedCost;

    public void UpdateFrom(ObservabilitySnapshot snapshot)
    {
        var cpu = Latest(snapshot.Metrics, "system.cpu.percent");
        var memory = Latest(snapshot.Metrics, "system.memory.percent");

        _maximumAllowedCost = cpu >= 85 || memory >= 95
            ? DiagnosticCost.Low
            : DiagnosticCost.Medium;
    }

    private static double Latest(IReadOnlyList<MetricSample> metrics, string name) => metrics
        .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(m => m.TimestampUtc)
        .Select(m => m.Value)
        .FirstOrDefault();
}
