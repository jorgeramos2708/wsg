using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Monitoring;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class LiveResourceSampleReaderTests
{
    private static CollectorResult SampleResult(double cpu = 12.5, double load = 43, double total = 17_000_000_000, double avail = 9_000_000_000, double commit = 61.5) =>
        CollectorResult.Success("Rendimiento", DiagnosticCost.VeryLow, DateTime.UtcNow, metrics:
        [
            new("m1", DateTime.UtcNow, EvidenceCategory.Performance, "CPU", "system.cpu.percent", cpu, "%"),
            new("m2", DateTime.UtcNow, EvidenceCategory.Performance, "Memoria", "system.memory.percent", load, "%"),
            new("m3", DateTime.UtcNow, EvidenceCategory.Performance, "Memoria", "system.memory.total_bytes", total, "bytes"),
            new("m4", DateTime.UtcNow, EvidenceCategory.Performance, "Memoria", "system.memory.available_bytes", avail, "bytes"),
            new("m5", DateTime.UtcNow, EvidenceCategory.Performance, "Memoria", "system.commit.percent", commit, "%")
        ]);

    [Fact]
    public void CompleteSuccessfulResult_MapsAllMetrics()
    {
        var sample = LiveResourceSampleReader.FromCollectorResult(SampleResult());

        Assert.NotNull(sample);
        Assert.Equal(12.5, sample!.CpuPercent);
        Assert.Equal(43, sample.MemoryLoadPercent);
        Assert.Equal(17_000_000_000, sample.MemoryTotalBytes);
        Assert.Equal(9_000_000_000, sample.MemoryAvailableBytes);
        Assert.Equal(61.5, sample.CommitPercent);
    }

    [Fact]
    public void FailedResult_ReturnsNull()
    {
        var failed = CollectorResult.Failure("Rendimiento", DiagnosticCost.VeryLow, DateTime.UtcNow, "boom");

        Assert.Null(LiveResourceSampleReader.FromCollectorResult(failed));
    }

    [Fact]
    public void MissingMetric_ReturnsNull()
    {
        var incomplete = CollectorResult.Success("Rendimiento", DiagnosticCost.VeryLow, DateTime.UtcNow, metrics:
        [
            new("m1", DateTime.UtcNow, EvidenceCategory.Performance, "CPU", "system.cpu.percent", 10, "%")
        ]);

        Assert.Null(LiveResourceSampleReader.FromCollectorResult(incomplete));
    }

    [Fact]
    public void NullResult_ReturnsNull()
    {
        Assert.Null(LiveResourceSampleReader.FromCollectorResult(null));
    }
}
