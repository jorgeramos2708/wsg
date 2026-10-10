using WindowsSystemGuardian.Contracts.Environment;

namespace WindowsSystemGuardian.Contracts.Observability;

public sealed record EvidenceRecord(
    string Id,
    DateTime TimestampUtc,
    string Source,
    EvidenceCategory Category,
    string Entity,
    string Metric,
    string Value,
    string Unit,
    EvidenceSeverity Severity,
    DetectionConfidence Confidence,
    string CorrelationKey,
    string Message);
