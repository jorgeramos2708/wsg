using WindowsSystemGuardian.Contracts.Dashboard;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Environment;
using WindowsSystemGuardian.Core.Health;

namespace WindowsSystemGuardian.Core.Dashboard;

public sealed class SystemSnapshotService(
    IEnvironmentProbe environmentProbe,
    IVolumeProvider volumeProvider,
    EnvironmentClassifier environmentClassifier,
    StorageHealthEvaluator storageHealthEvaluator)
{
    public async Task<DashboardSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var environmentTask = environmentProbe.ProbeAsync(cancellationToken);
        var volumesTask = volumeProvider.GetVolumesAsync(cancellationToken);

        await Task.WhenAll(environmentTask, volumesTask);

        var profile = environmentClassifier.Classify(await environmentTask);
        var volumes = await volumesTask;
        var health = storageHealthEvaluator.Evaluate(volumes);

        return new DashboardSnapshot(profile, volumes, health, DateTime.UtcNow);
    }
}
