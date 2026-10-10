namespace WindowsSystemGuardian.Contracts.Incidents;

public enum RecommendationRisk
{
    Informational = 0,
    Low,
    Medium,
    High,
    Blocked
}

public sealed record RecommendationStep(
    int Order,
    string Title,
    string Detail,
    RecommendationRisk Risk,
    bool RequiresElevation,
    bool MayRequireRestart,
    bool AutomationAllowed,
    string Verification);
