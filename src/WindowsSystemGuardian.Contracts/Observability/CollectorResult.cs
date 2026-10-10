namespace WindowsSystemGuardian.Contracts.Observability;

public sealed record CollectorResult(
    string CollectorName,
    DiagnosticCost Cost,
    DateTime CapturedAtUtc,
    bool Succeeded,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<MetricSample> Metrics,
    string? Error = null)
{
    public static CollectorResult Success(
        string collectorName,
        DiagnosticCost cost,
        DateTime capturedAtUtc,
        IReadOnlyList<EvidenceRecord>? evidence = null,
        IReadOnlyList<MetricSample>? metrics = null) =>
        new(collectorName, cost, capturedAtUtc, true, evidence ?? [], metrics ?? []);

    public static CollectorResult Failure(
        string collectorName,
        DiagnosticCost cost,
        DateTime capturedAtUtc,
        string error) =>
        new(collectorName, cost, capturedAtUtc, false, [], [], error);
}
