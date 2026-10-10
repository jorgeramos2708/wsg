using WindowsSystemGuardian.Contracts.Storage;

namespace WindowsSystemGuardian.Core.Abstractions;

public interface IVolumeProvider
{
    Task<IReadOnlyList<DiskVolumeSnapshot>> GetVolumesAsync(CancellationToken cancellationToken = default);
}
