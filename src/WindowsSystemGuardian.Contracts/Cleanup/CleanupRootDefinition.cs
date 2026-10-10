namespace WindowsSystemGuardian.Contracts.Cleanup;

public sealed record CleanupRootDefinition(
    string Category,
    string Path,
    TimeSpan MinimumAge,
    CleanupRisk Risk,
    string Description);
