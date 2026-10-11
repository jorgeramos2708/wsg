using WindowsSystemGuardian.Core.Monitoring;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class ProcessLiveClassifierTests
{
    private static ProcessLiveSampleRecord Sample(
        int pid, string name, double cpuSeconds, long workingSet, DateTime timestampUtc) =>
        new(pid, name, cpuSeconds, workingSet, workingSet / 2, timestampUtc);

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CpuPercent_IsDeltaOverElapsedAndProcessors()
    {
        // 4 s de CPU en 2 s de pared con 4 núcleos = 50 % (por núcleo).
        var percent = ProcessLiveClassifier.ComputeCpuPercent(
            previousCpuSeconds: 10, previousTimestampUtc: T0,
            currentCpuSeconds: 14, currentTimestampUtc: T0.AddSeconds(2),
            processorCount: 4);

        Assert.Equal(50, percent);
    }

    [Fact]
    public void CpuPercent_IsClampedToHundred()
    {
        var percent = ProcessLiveClassifier.ComputeCpuPercent(
            0, T0, 100, T0.AddSeconds(1), processorCount: 2);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void NoBaseline_ReportsZeroCpuWithoutInventingDelta()
    {
        var consumption = ProcessLiveClassifier.Classify(
            [Sample(100, "app", 50, 300_000_000, T0.AddSeconds(2))],
            previousCpuByProcess: new Dictionary<long, ProcessCpuBaseline>(),
            processorCount: 4);

        var item = Assert.Single(consumption);
        Assert.Equal(0, item.CpuPercent);
        Assert.False(item.CpuSignificant);
    }

    [Fact]
    public void SignificanceThresholds_MarkCpuAndMemory()
    {
        // CPU: 2 s de CPU en 2 s de pared con 2 núcleos = 50 % (≥ 10 %); memoria 300 MB (≥ 200 MB).
        var states = new Dictionary<long, ProcessCpuBaseline> { [100] = new(0, T0) };
        var consumption = ProcessLiveClassifier.Classify(
            [Sample(100, "app", 2, 300_000_000, T0.AddSeconds(2))],
            states, processorCount: 2);

        var item = Assert.Single(consumption);
        Assert.True(item.CpuSignificant);
        Assert.True(item.MemorySignificant);
    }

    [Fact]
    public void BelowThresholds_IsNotSignificant()
    {
        var states = new Dictionary<long, ProcessCpuBaseline> { [100] = new(0, T0) };
        var consumption = ProcessLiveClassifier.Classify(
            [Sample(100, "idle-ish", 0.05, 50_000_000, T0.AddSeconds(2))],
            states, processorCount: 8);

        var item = Assert.Single(consumption);
        Assert.False(item.CpuSignificant);
        Assert.False(item.MemorySignificant);
    }

    [Fact]
    public void ServiceAttribution_IsAttachedToTheProcess()
    {
        var states = new Dictionary<long, ProcessCpuBaseline>();
        var services = new Dictionary<int, IReadOnlyList<string>>
        {
            [1234] = ["wuauserv", "bits"]
        };

        var consumption = ProcessLiveClassifier.Classify(
            [Sample(1234, "svchost", 0, 100_000_000, T0)],
            states, 4, services);

        var item = Assert.Single(consumption);
        Assert.Equal(["wuauserv", "bits"], item.ServiceNames);
    }

    [Fact]
    public void MergeTopConsumers_UnionsCpuAndMemoryRankingsWithoutDuplicates()
    {
        var consumption = new List<LiveProcessConsumption>
        {
            new(1, "cpu-hog", 90, 100_000_000, 50_000_000, Array.Empty<string>(), true, false),
            new(2, "mem-hog", 1, 1_000_000_000, 900_000_000, Array.Empty<string>(), false, true),
            new(3, "small", 0.5, 10_000_000, 5_000_000, Array.Empty<string>(), false, false)
        };

        var top = ProcessLiveClassifier.MergeTopConsumers(consumption, topN: 2);

        Assert.Equal(2, top.Count); // cpu-hog y mem-hog; "small" queda fuera
        Assert.Equal("cpu-hog", top[0].Name);
        Assert.Equal("mem-hog", top[1].Name);
    }
}
