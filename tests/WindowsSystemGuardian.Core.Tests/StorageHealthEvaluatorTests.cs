using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Contracts.Storage;
using WindowsSystemGuardian.Core.Health;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class StorageHealthEvaluatorTests
{
    private readonly StorageHealthEvaluator _sut = new();
    private const long GiB = 1024L * 1024L * 1024L;

    [Fact]
    public void CriticalSmallFreeSpace_IsReportedAsCritical()
    {
        var volume = new DiskVolumeSnapshot(
            "C", "C:", "C:\\", "Windows", "NTFS", VolumeKind.Fixed,
            100 * GiB, 4 * GiB, true, true, VolumePurpose.System);

        var result = _sut.Evaluate([volume]);

        Assert.Equal(HealthStatus.Critical, result.Status);
        Assert.True(result.Score <= 65);
    }

    [Fact]
    public void HealthyCapacity_RemainsHealthy()
    {
        var volume = new DiskVolumeSnapshot(
            "C", "C:", "C:\\", "Windows", "NTFS", VolumeKind.Fixed,
            500 * GiB, 200 * GiB, true, true, VolumePurpose.System);

        var result = _sut.Evaluate([volume]);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void RecoveryVolume_DoesNotPenalizeGlobalHealth()
    {
        var system = new DiskVolumeSnapshot(
            "C", "C:", "C:\\", "Windows", "NTFS", VolumeKind.Fixed,
            500 * GiB, 200 * GiB, true, true, VolumePurpose.System);
        var recovery = new DiskVolumeSnapshot(
            "R", null, "Recovery", "Recovery", "NTFS", VolumeKind.Fixed,
            1 * GiB, 40 * 1024 * 1024, false, false, VolumePurpose.Recovery);

        var result = _sut.Evaluate([system, recovery]);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void LargeVolume_WithLowPercentageButLargeAbsoluteFreeSpace_RemainsHealthy()
    {
        var volume = new DiskVolumeSnapshot(
            "D", "D:", "D:\\", "Data", "NTFS", VolumeKind.Fixed,
            8_000 * GiB, 1_200 * GiB, false, false, VolumePurpose.Data);

        var result = _sut.Evaluate([volume]);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void MountedDataVolume_IsRepresentableWithoutDriveLetter()
    {
        var volume = new DiskVolumeSnapshot(
            "VOL", null, "\\\\?\\Volume{TEST}\\", "Data", "NTFS", VolumeKind.Fixed,
            500 * GiB, 200 * GiB, false, false, VolumePurpose.Data)
        {
            MountPaths = ["C:\\Mounts\\Data01\\"]
        };

        Assert.Contains("C:\\Mounts\\Data01\\", volume.DisplayName);
        Assert.Equal(HealthStatus.Healthy, _sut.EvaluateVolume(volume));
    }

}
