using WindowsSystemGuardian.Contracts.Environment;

namespace WindowsSystemGuardian.Contracts.Observability;

public sealed record Finding(
    string Id,
    string Fingerprint,
    DateTime DetectedAtUtc,
    FindingSeverity Severity,
    EvidenceCategory Category,
    string Title,
    string Explanation,
    string Recommendation,
    DetectionConfidence Confidence,
    string AffectedEntity,
    IReadOnlyList<string> EvidenceIds,
    DateTime? FirstSeenUtc = null,
    DateTime? LastSeenUtc = null,
    int OccurrenceCount = 1,
    FindingLifecycleState State = FindingLifecycleState.Active);
