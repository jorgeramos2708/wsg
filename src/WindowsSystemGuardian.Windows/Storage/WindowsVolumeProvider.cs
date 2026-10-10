using System.Management;
using WindowsSystemGuardian.Contracts.Storage;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Storage;

public sealed class WindowsVolumeProvider : IVolumeProvider
{
    private const long GiB = 1024L * 1024L * 1024L;

    public Task<IReadOnlyList<DiskVolumeSnapshot>> GetVolumesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DiskVolumeSnapshot>>(() => GetVolumes(cancellationToken), cancellationToken);

    private static IReadOnlyList<DiskVolumeSnapshot> GetVolumes(CancellationToken cancellationToken)
    {
        var volumes = new List<DiskVolumeSnapshot>();

        try
        {
            var mountPaths = QueryMountPaths(cancellationToken);

            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriveLetter, Name, Label, FileSystem, DriveType, Capacity, FreeSpace, SystemVolume, BootVolume FROM Win32_Volume");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capacity = ReadInt64(item, "Capacity");
                if (capacity <= 0)
                    continue;

                var kind = MapDriveType(ReadUInt32(item, "DriveType"));
                if (kind == VolumeKind.Network || kind == VolumeKind.Optical)
                    continue;

                var id = ReadString(item, "DeviceID", Guid.NewGuid().ToString("N"));
                var driveLetter = NormalizeDriveLetter(ReadString(item, "DriveLetter", string.Empty));
                var name = ReadString(item, "Name", driveLetter ?? id);
                var label = ReadString(item, "Label", string.Empty);
                var fileSystem = ReadString(item, "FileSystem", "Unknown");
                var isSystem = ReadBool(item, "SystemVolume");
                var isBoot = ReadBool(item, "BootVolume");
                var paths = mountPaths.TryGetValue(id, out var discovered)
                    ? discovered.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                    : [];
                var purpose = ClassifyPurpose(kind, driveLetter, label, fileSystem, capacity, isSystem, isBoot, paths);

                volumes.Add(new DiskVolumeSnapshot(
                    id,
                    driveLetter,
                    name,
                    label,
                    fileSystem,
                    kind,
                    capacity,
                    ReadInt64(item, "FreeSpace"),
                    isSystem,
                    isBoot,
                    purpose)
                {
                    MountPaths = paths
                });
            }
        }
        catch
        {
            volumes.AddRange(GetDriveInfoFallback());
        }

        return volumes
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(v => v.Purpose == VolumePurpose.System)
            .ThenBy(v => v.DriveLetter ?? v.MountPaths.FirstOrDefault() ?? v.Name)
            .ToList();
    }

    private static Dictionary<string, HashSet<string>> QueryMountPaths(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Directory, Volume FROM Win32_MountPoint");
            using var associations = searcher.Get();
            foreach (ManagementObject association in associations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directoryReference = association["Directory"]?.ToString();
                var volumeReference = association["Volume"]?.ToString();
                if (string.IsNullOrWhiteSpace(directoryReference) || string.IsNullOrWhiteSpace(volumeReference))
                    continue;

                var directoryName = ReadReferencedProperty(directoryReference, "Name");
                var volumeId = ReadReferencedProperty(volumeReference, "DeviceID");
                if (string.IsNullOrWhiteSpace(directoryName) || string.IsNullOrWhiteSpace(volumeId))
                    continue;

                if (!result.TryGetValue(volumeId, out var paths))
                {
                    paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[volumeId] = paths;
                }

                paths.Add(directoryName.TrimEnd('\\') + "\\");
            }
        }
        catch
        {
            // Mount point discovery is additive. Failure does not invalidate the base volume inventory.
        }

        return result;
    }

    private static string? ReadReferencedProperty(string managementPath, string property)
    {
        try
        {
            using var referenced = new ManagementObject(managementPath);
            referenced.Get();
            return referenced[property]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<DiskVolumeSnapshot> GetDriveInfoFallback()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.CDRom)
                continue;

            string label;
            string fileSystem;
            try
            {
                label = drive.VolumeLabel;
                fileSystem = drive.DriveFormat;
            }
            catch
            {
                label = string.Empty;
                fileSystem = "Unknown";
            }

            var driveLetter = drive.Name.TrimEnd('\\');
            var isSystem = string.Equals(
                drive.Name,
                Path.GetPathRoot(System.Environment.SystemDirectory),
                StringComparison.OrdinalIgnoreCase);
            var kind = drive.DriveType == DriveType.Removable ? VolumeKind.Removable : VolumeKind.Fixed;
            var purpose = kind == VolumeKind.Removable
                ? VolumePurpose.Removable
                : isSystem ? VolumePurpose.System : VolumePurpose.Data;

            yield return new DiskVolumeSnapshot(
                drive.Name,
                driveLetter,
                drive.Name,
                label,
                fileSystem,
                kind,
                drive.TotalSize,
                drive.AvailableFreeSpace,
                isSystem,
                false,
                purpose);
        }
    }

    private static VolumePurpose ClassifyPurpose(
        VolumeKind kind,
        string? driveLetter,
        string label,
        string fileSystem,
        long capacity,
        bool isSystem,
        bool isBoot,
        IReadOnlyList<string> mountPaths)
    {
        if (kind == VolumeKind.Removable)
            return VolumePurpose.Removable;

        var labelUpper = label.ToUpperInvariant();
        if (labelUpper.Contains("RECOVERY") || labelUpper.Contains("WINRE") || labelUpper.Contains("RECUPER"))
            return VolumePurpose.Recovery;

        if (string.IsNullOrWhiteSpace(driveLetter) &&
            (fileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ||
             fileSystem.Equals("FAT", StringComparison.OrdinalIgnoreCase)) &&
            capacity <= 2 * GiB)
            return VolumePurpose.Efi;

        if (isBoot || isSystem)
            return VolumePurpose.System;

        if (kind == VolumeKind.Fixed &&
            (!string.IsNullOrWhiteSpace(driveLetter) || mountPaths.Count > 0))
            return VolumePurpose.Data;

        return VolumePurpose.Unknown;
    }

    private static VolumeKind MapDriveType(uint value) => value switch
    {
        2 => VolumeKind.Removable,
        3 => VolumeKind.Fixed,
        4 => VolumeKind.Network,
        5 => VolumeKind.Optical,
        6 => VolumeKind.RamDisk,
        _ => VolumeKind.Unknown
    };

    private static string? NormalizeDriveLetter(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ReadString(ManagementObject item, string name, string fallback) =>
        item.Properties[name]?.Value?.ToString() ?? fallback;

    private static long ReadInt64(ManagementObject item, string name) =>
        item.Properties[name]?.Value is null ? 0 : Convert.ToInt64(item.Properties[name]!.Value);

    private static uint ReadUInt32(ManagementObject item, string name) =>
        item.Properties[name]?.Value is null ? 0 : Convert.ToUInt32(item.Properties[name]!.Value);

    private static bool ReadBool(ManagementObject item, string name) =>
        item.Properties[name]?.Value is bool value && value;
}
