using WindowsSystemGuardian.Contracts.Environment;

namespace WindowsSystemGuardian.Contracts.Incidents;

public sealed record RootCauseCandidate(
    string Kind,
    string Title,
    string Explanation,
    DetectionConfidence Confidence,
    int ConfidencePercent,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> FindingFingerprints);
