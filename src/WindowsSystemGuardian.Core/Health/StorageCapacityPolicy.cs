using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Storage;

namespace WindowsSystemGuardian.Core.Health;

/// <summary>
/// Política única de capacidad para evitar que la UI y los colectores diverjan.
/// Combina porcentaje y capacidad absoluta: un volumen muy grande no se marca
/// automáticamente como crítico solo por tener un porcentaje bajo si conserva
/// suficiente espacio absoluto.
/// </summary>
public sealed class StorageCapacityPolicy
{
    private const double GiB = 1024d * 1024d * 1024d;

    public HealthStatus Evaluate(DiskVolumeSnapshot volume)
    {
        if (volume.Kind != VolumeKind.Fixed || volume.TotalBytes <= 0 ||
            volume.Purpose is not (VolumePurpose.System or VolumePurpose.Data))
            return HealthStatus.Unknown;

        var freeGiB = volume.FreeBytes / GiB;
        var freePct = volume.PercentFree;

        if (freeGiB < 5 || (freePct < 5 && freeGiB < 20))
            return HealthStatus.Critical;

        if (freeGiB < 10 || (freePct < 20 && freeGiB < 50))
            return HealthStatus.Warning;

        return HealthStatus.Healthy;
    }
}
