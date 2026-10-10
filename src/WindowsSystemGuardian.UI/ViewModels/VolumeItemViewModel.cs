using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Storage;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class VolumeItemViewModel(
    DiskVolumeSnapshot volume,
    HealthStatus healthStatus)
{
    private const double GiB = 1024d * 1024d * 1024d;

    public string Name => volume.DisplayName;
    public string FileSystem => volume.FileSystem;

    public string Type => volume.Kind switch
    {
        VolumeKind.Fixed => "Local",
        VolumeKind.Removable => "Extraíble",
        VolumeKind.RamDisk => "RAM Disk",
        _ => volume.Kind.ToString()
    };

    public string Purpose => volume.Purpose switch
    {
        VolumePurpose.System => "Sistema",
        VolumePurpose.Data => "Datos",
        VolumePurpose.Recovery => "Recuperación",
        VolumePurpose.Efi => "EFI",
        VolumePurpose.Reserved => "Reservado",
        VolumePurpose.Removable => "Extraíble",
        _ => "No determinado"
    };

    public string MountPathText => volume.MountPaths.Count == 0
        ? string.Empty
        : string.Join(" · ", volume.MountPaths);

    public double PercentUsed => volume.PercentUsed;
    public string PercentUsedText => $"{volume.PercentUsed:F0}%";
    public string UsedText => $"{volume.UsedBytes / GiB:F1} GB usados";
    public string FreeText => $"{volume.FreeBytes / GiB:F1} GB libres de {volume.TotalBytes / GiB:F1} GB";
    public bool IsSystem => volume.Purpose == VolumePurpose.System;
    public bool IsEvaluable => volume.Kind == VolumeKind.Fixed &&
                               volume.Purpose is VolumePurpose.System or VolumePurpose.Data;

    public HealthStatus HealthStatus { get; } = healthStatus;

    public string HealthDisplay => HealthStatus switch
    {
        HealthStatus.Healthy => "Buen estado",
        HealthStatus.Warning => "Advertencia",
        HealthStatus.Critical => "Crítico",
        _ => "Informativo"
    };

    public string SystemBadge => IsSystem ? "SISTEMA" : Purpose.ToUpperInvariant();
}
