using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Recommendations;
using WindowsSystemGuardian.Core.Scheduling;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class ObservabilityServiceTests
{
    [Fact]
    public async Task Capture_PersistsCollectorOutput()
    {
        var store = new MemoryStore();
        var collector = new FakeCollector("fake", TimeSpan.FromMinutes(1));
        var service = CreateService([collector], store);

        var snapshot = await service.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Single(snapshot.Collectors);
        Assert.Single(snapshot.Metrics);
        Assert.Single(store.Metrics);
        Assert.Equal(1, snapshot.SuccessfulCollectorCount);
    }

    [Fact]
    public async Task Scheduler_DoesNotRunCollectorBeforeIntervalExpires()
    {
        var store = new MemoryStore();
        var collector = new FakeCollector("slow", TimeSpan.FromHours(1));
        var service = CreateService([collector], store);
        var scheduler = new DiagnosticScheduler(service, new ScanLoadController());

        var first = await scheduler.RunDueAsync(TestContext.Current.CancellationToken);
        var second = await scheduler.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, collector.RunCount);
    }


    [Fact]
    public async Task Capture_PersistenceFailure_DoesNotDiscardCurrentObservation()
    {
        var collector = new FakeCollector("fake", TimeSpan.FromMinutes(1));
        var service = CreateService([collector], new ThrowingStore());

        var snapshot = await service.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Single(snapshot.Collectors);
        Assert.Single(snapshot.Metrics);
        Assert.True(snapshot.PersistenceDegraded);
        Assert.NotNull(snapshot.PersistenceError);
    }

    [Fact]
    public async Task Capture_HistoryIsLoadedOnceThenMaintainedIncrementally()
    {
        var store = new MemoryStore();
        var collector = new FakeCollector("fake", TimeSpan.FromMinutes(1));
        var service = CreateService([collector], store);

        await service.CaptureAsync(TestContext.Current.CancellationToken);
        await service.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.LoadMetricsCalls);
        Assert.Equal(2, collector.RunCount);
    }

    private static ObservabilityService CreateService(
        IEnumerable<IObservabilityCollector> collectors,
        IObservabilityStore store) =>
        new(collectors, store, new FindingEngine(),
            new IncidentEngine(new RootCauseCorrelationEngine(), new RecommendationEngine()),
            new IncidentLifecycleTracker());

    private sealed class FakeCollector(string name, TimeSpan interval) : IObservabilityCollector
    {
        public int RunCount { get; private set; }
        public string Name => name;
        public DiagnosticCost Cost => DiagnosticCost.VeryLow;
        public TimeSpan RecommendedInterval => interval;

        public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            var now = DateTime.UtcNow;
            MetricSample metric = new($"metric:{RunCount}", now, EvidenceCategory.Performance, "test", "test.metric", RunCount, "count");
            return Task.FromResult(CollectorResult.Success(Name, Cost, now, metrics: [metric]));
        }
    }

    private sealed class MemoryStore : IObservabilityStore
    {
        public List<EvidenceRecord> Evidence { get; } = [];
        public List<MetricSample> Metrics { get; } = [];
        public List<Finding> Findings { get; } = [];
        public List<Incident> Incidents { get; } = [];
        public int LoadMetricsCalls { get; private set; }
        public string RootPath => "memory";

        public Task AppendCollectorResultAsync(CollectorResult result, CancellationToken cancellationToken = default)
        {
            Evidence.AddRange(result.Evidence);
            Metrics.AddRange(result.Metrics);
            return Task.CompletedTask;
        }

        public Task AppendFindingsAsync(IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default)
        {
            Findings.AddRange(findings);
            return Task.CompletedTask;
        }

        public Task AppendIncidentsAsync(IReadOnlyList<Incident> incidents, CancellationToken cancellationToken = default)
        {
            Incidents.AddRange(incidents);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EvidenceRecord>> LoadEvidenceSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceRecord>>(Evidence.Where(e => e.TimestampUtc >= sinceUtc).ToList());

        public Task<IReadOnlyList<MetricSample>> LoadMetricsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            LoadMetricsCalls++;
            return Task.FromResult<IReadOnlyList<MetricSample>>(Metrics.Where(m => m.TimestampUtc >= sinceUtc).ToList());
        }

        public Task<IReadOnlyList<Finding>> LoadFindingsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Finding>>(Findings.Where(f => f.DetectedAtUtc >= sinceUtc).ToList());

        public Task<IReadOnlyList<Incident>> LoadIncidentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Incident>>(Incidents.Where(i => i.LastSeenUtc >= sinceUtc).ToList());

        public Task EnforceRetentionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingStore : IObservabilityStore
    {
        public string RootPath => "throwing";

        public Task AppendCollectorResultAsync(CollectorResult result, CancellationToken cancellationToken = default) =>
            throw new IOException("write failure");

        public Task AppendFindingsAsync(IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default) =>
            throw new IOException("finding write failure");

        public Task AppendIncidentsAsync(IReadOnlyList<Incident> incidents, CancellationToken cancellationToken = default) =>
            throw new IOException("incident write failure");

        public Task<IReadOnlyList<EvidenceRecord>> LoadEvidenceSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            throw new IOException("read failure");

        public Task<IReadOnlyList<MetricSample>> LoadMetricsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            throw new IOException("read failure");

        public Task<IReadOnlyList<Finding>> LoadFindingsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            throw new IOException("read failure");

        public Task<IReadOnlyList<Incident>> LoadIncidentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            throw new IOException("read failure");

        public Task EnforceRetentionAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("retention failure");
    }

}
