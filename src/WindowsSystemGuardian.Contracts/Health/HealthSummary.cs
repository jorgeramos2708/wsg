namespace WindowsSystemGuardian.Contracts.Health;

public sealed record HealthSummary(int Score, HealthStatus Status, string Summary);
