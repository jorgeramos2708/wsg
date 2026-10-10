using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Storage;

namespace WindowsSystemGuardian.Contracts.Dashboard;

public sealed record DashboardSnapshot(
    EnvironmentProfile Environment,
    IReadOnlyList<DiskVolumeSnapshot> Volumes,
    HealthSummary StorageHealth,
    DateTime CapturedAtUtc);
