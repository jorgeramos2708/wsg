namespace WindowsSystemGuardian.Contracts.Observability;

public sealed record MetricSample(
    string Id,
    DateTime TimestampUtc,
    EvidenceCategory Category,
    string Entity,
    string Name,
    double Value,
    string Unit);
