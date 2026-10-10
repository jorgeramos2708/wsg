namespace WindowsSystemGuardian.Contracts.Cleanup;

public sealed record CleanupCandidate(
    string Category,
    string Path,
    long SizeBytes,
    DateTime LastWriteUtc,
    CleanupRisk Risk,
    string Reason);
