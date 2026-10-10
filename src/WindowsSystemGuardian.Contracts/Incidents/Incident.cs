namespace WindowsSystemGuardian.Contracts.Incidents;

public sealed record Incident(
    string Id,
    string Fingerprint,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    IncidentSeverity Severity,
    IncidentState State,
    string Title,
    string Summary,
    string Impact,
    RootCauseCandidate RootCause,
    IReadOnlyList<string> AffectedEntities,
    IReadOnlyList<string> FindingFingerprints,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<RecommendationStep> Recommendations,
    IReadOnlyList<string> VerificationCriteria,
    int OccurrenceCount = 1,
    int ConsecutiveClearChecks = 0,
    DateTime? ResolvedAtUtc = null,
    IReadOnlyList<string>? VerificationCollectors = null,
    DateTime? StateChangedAtUtc = null);
