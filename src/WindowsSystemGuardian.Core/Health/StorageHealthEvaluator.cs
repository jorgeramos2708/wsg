using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Storage;

namespace WindowsSystemGuardian.Core.Health;

public sealed class StorageHealthEvaluator(StorageCapacityPolicy capacityPolicy)
{
    public StorageHealthEvaluator() : this(new StorageCapacityPolicy()) { }

    public HealthSummary Evaluate(IReadOnlyList<DiskVolumeSnapshot> volumes)
    {
        var evaluableVolumes = volumes
            .Where(v => v.Kind == VolumeKind.Fixed && v.TotalBytes > 0)
            .Where(v => v.Purpose is VolumePurpose.System or VolumePurpose.Data)
            .ToList();

        if (evaluableVolumes.Count == 0)
            return new HealthSummary(70, HealthStatus.Warning, "No se encontraron volúmenes de sistema/datos evaluables.");

        var score = 100;
        var worst = HealthStatus.Healthy;
        var messages = new List<string>();

        foreach (var volume in evaluableVolumes)
        {
            var status = capacityPolicy.Evaluate(volume);
            var freePct = volume.PercentFree;
            var freeGiB = volume.FreeBytes / (1024d * 1024d * 1024d);

            switch (status)
            {
                case HealthStatus.Critical:
                    score -= 35;
                    messages.Add($"{volume.DisplayName}: espacio crítico ({freeGiB:F1} GB / {freePct:F1}% libre)");
                    break;
                case HealthStatus.Warning:
                    score -= freePct < 10 || freeGiB < 10 ? 20 : 8;
                    messages.Add($"{volume.DisplayName}: advertencia de capacidad ({freeGiB:F1} GB / {freePct:F1}% libre)");
                    break;
            }

            worst = Max(worst, status);
        }

        score = Math.Clamp(score, 0, 100);
        var summary = messages.Count == 0
            ? "La capacidad de los volúmenes de sistema y datos está dentro de los umbrales configurados."
            : string.Join(" · ", messages.Take(3));

        return new HealthSummary(score, worst, summary);
    }

    public HealthStatus EvaluateVolume(DiskVolumeSnapshot volume) => capacityPolicy.Evaluate(volume);

    private static HealthStatus Max(HealthStatus a, HealthStatus b) => Severity(a) >= Severity(b) ? a : b;

    private static int Severity(HealthStatus value) => value switch
    {
        HealthStatus.Critical => 3,
        HealthStatus.Warning => 2,
        HealthStatus.Healthy => 1,
        _ => 0
    };
}
