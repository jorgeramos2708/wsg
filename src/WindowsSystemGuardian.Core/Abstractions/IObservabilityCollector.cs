using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Abstractions;

public interface IObservabilityCollector
{
    string Name { get; }
    DiagnosticCost Cost { get; }
    TimeSpan RecommendedInterval { get; }
    Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default);
}
