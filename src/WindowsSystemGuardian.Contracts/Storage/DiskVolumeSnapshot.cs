namespace WindowsSystemGuardian.Contracts.Storage;

public sealed record DiskVolumeSnapshot(
    string Id,
    string? DriveLetter,
    string Name,
    string Label,
    string FileSystem,
    VolumeKind Kind,
    long TotalBytes,
    long FreeBytes,
    bool IsSystemVolume,
    bool IsBootVolume,
    VolumePurpose Purpose = VolumePurpose.Unknown)
{
    public IReadOnlyList<string> MountPaths { get; init; } = [];

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double PercentUsed => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100d;
    public double PercentFree => 100d - PercentUsed;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DriveLetter))
                return $"{DriveLetter} {(string.IsNullOrWhiteSpace(Label) ? string.Empty : Label)}".Trim();

            var mount = MountPaths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (!string.IsNullOrWhiteSpace(mount))
                return string.IsNullOrWhiteSpace(Label) ? mount : $"{mount} {Label}".Trim();

            return string.IsNullOrWhiteSpace(Label) ? Name : Label;
        }
    }
}
