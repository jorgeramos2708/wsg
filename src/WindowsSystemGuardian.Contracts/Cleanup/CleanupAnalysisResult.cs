namespace WindowsSystemGuardian.Contracts.Cleanup;

public sealed record CleanupAnalysisResult(
    IReadOnlyList<CleanupCandidate> Candidates,
    long TotalRecoverableBytes,
    int FilesScanned,
    int FilesSkipped,
    int LockedFiles,
    bool Truncated,
    TimeSpan Duration);
