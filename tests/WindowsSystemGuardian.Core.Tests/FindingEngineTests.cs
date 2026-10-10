using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Observability;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class FindingEngineTests
{
    private readonly FindingEngine _engine = new();

    [Fact]
    public void CriticalEvidence_BecomesCriticalFinding()
    {
        var now = DateTime.UtcNow;
        var evidence = new EvidenceRecord(
            "event:System:1",
            now,
            "Eventos de Windows",
            EvidenceCategory.Reliability,
            "Reinicio inesperado",
            "Evento 41",
            "41",
            "EventId",
            EvidenceSeverity.Critical,
            DetectionConfidence.High,
            "reliability:unexpected-shutdown",
            "Windows registró un reinicio inesperado.");

        var findings = _engine.Evaluate([evidence], []);

        var finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Equal(EvidenceCategory.Reliability, finding.Category);
    }

    [Fact]
    public void SustainedMemoryPressure_RequiresMultipleSamples()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            Metric("m1", now.AddMinutes(-2), "system.memory.percent", 94),
            Metric("m2", now.AddMinutes(-1), "system.memory.percent", 93),
            Metric("m3", now, "system.memory.percent", 92)
        };

        var findings = _engine.Evaluate([], history);

        Assert.Contains(findings, f => f.Fingerprint == "performance:sustained-memory-pressure" && f.Severity == FindingSeverity.Warning);
    }

    [Fact]
    public void RapidCapacityLoss_CreatesTrendFinding()
    {
        var now = DateTime.UtcNow;
        var gib = 1024d * 1024d * 1024d;
        var history = new[]
        {
            StorageMetric("s1", now.AddMinutes(-30), 50 * gib),
            StorageMetric("s2", now.AddMinutes(-15), 45 * gib),
            StorageMetric("s3", now, 40 * gib)
        };

        var findings = _engine.Evaluate([], history);

        Assert.Contains(findings, f => f.Fingerprint.StartsWith("storage:rapid-growth:", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalMetrics_DoNotCreateFindings()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            Metric("c1", now.AddMinutes(-2), "system.cpu.percent", 25),
            Metric("c2", now.AddMinutes(-1), "system.cpu.percent", 30),
            Metric("c3", now, "system.cpu.percent", 20),
            Metric("m1", now.AddMinutes(-2), "system.memory.percent", 45),
            Metric("m2", now.AddMinutes(-1), "system.memory.percent", 46),
            Metric("m3", now, "system.memory.percent", 44)
        };

        Assert.Empty(_engine.Evaluate([], history));
    }


    [Fact]
    public void SustainedCommitPressure_CreatesFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            Metric("k1", now.AddMinutes(-2), "system.commit.percent", 96),
            Metric("k2", now.AddMinutes(-1), "system.commit.percent", 95),
            Metric("k3", now, "system.commit.percent", 94)
        };

        Assert.Contains(_engine.Evaluate([], history), f =>
            f.Fingerprint == "performance:sustained-commit-pressure");
    }

    [Fact]
    public void SustainedProcessCpu_CreatesAttributedFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            ProcessMetric("p1", now.AddMinutes(-3), "process.cpu.percent", "worker (PID 9)", 72, "%"),
            ProcessMetric("p2", now.AddMinutes(-2), "process.cpu.percent", "worker (PID 9)", 70, "%"),
            ProcessMetric("p3", now.AddMinutes(-1), "process.cpu.percent", "worker (PID 9)", 74, "%")
        };

        Assert.Contains(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("performance:process-cpu:", StringComparison.Ordinal));
    }

    [Fact]
    public void GrowingProcessMemory_CreatesTrendFindingWithoutClaimingLeak()
    {
        var now = DateTime.UtcNow;
        var gib = 1024d * 1024d * 1024d;
        var history = new[]
        {
            ProcessMetric("m1", now.AddMinutes(-60), "process.memory.private_bytes", "app (PID 7)", 0.5 * gib, "bytes"),
            ProcessMetric("m2", now.AddMinutes(-40), "process.memory.private_bytes", "app (PID 7)", 0.8 * gib, "bytes"),
            ProcessMetric("m3", now.AddMinutes(-20), "process.memory.private_bytes", "app (PID 7)", 1.1 * gib, "bytes"),
            ProcessMetric("m4", now, "process.memory.private_bytes", "app (PID 7)", 1.4 * gib, "bytes")
        };

        var finding = Assert.Single(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("performance:process-memory-growth:", StringComparison.Ordinal));
        Assert.Contains("no una confirmación de fuga", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SustainedDiskBusy_CreatesFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            ProcessMetric("d1", now.AddMinutes(-3), "storage.busy_percent", "C:", 96, "%"),
            ProcessMetric("d2", now.AddMinutes(-2), "storage.busy_percent", "C:", 95, "%"),
            ProcessMetric("d3", now.AddMinutes(-1), "storage.busy_percent", "C:", 94, "%")
        };

        Assert.Contains(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("performance:disk-busy:", StringComparison.Ordinal));
    }

    [Fact]
    public void GrowingProcessHandles_CreatesResourceGrowthFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            ProcessMetric("h1", now.AddMinutes(-60), "process.handles", "agent (PID 33)", 1000, "count"),
            ProcessMetric("h2", now.AddMinutes(-40), "process.handles", "agent (PID 33)", 3000, "count"),
            ProcessMetric("h3", now.AddMinutes(-20), "process.handles", "agent (PID 33)", 5000, "count"),
            ProcessMetric("h4", now, "process.handles", "agent (PID 33)", 7000, "count")
        };

        var finding = Assert.Single(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("performance:process-handle-growth:", StringComparison.Ordinal));

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("no una confirmación de fuga", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GrowingProcessThreads_CreatesResourceGrowthFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            ProcessMetric("t1", now.AddMinutes(-60), "process.threads", "worker (PID 88)", 100, "count"),
            ProcessMetric("t2", now.AddMinutes(-40), "process.threads", "worker (PID 88)", 300, "count"),
            ProcessMetric("t3", now.AddMinutes(-20), "process.threads", "worker (PID 88)", 500, "count"),
            ProcessMetric("t4", now, "process.threads", "worker (PID 88)", 700, "count")
        };

        Assert.Contains(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("performance:process-thread-growth:", StringComparison.Ordinal));
    }

    [Fact]
    public void SustainedNetworkUtilization_CreatesNetworkFinding()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            ProcessMetric("n1", now.AddMinutes(-3), "network.utilization.percent", "Ethernet", 94, "%"),
            ProcessMetric("n2", now.AddMinutes(-2), "network.utilization.percent", "Ethernet", 93, "%"),
            ProcessMetric("n3", now.AddMinutes(-1), "network.utilization.percent", "Ethernet", 92, "%")
        };

        var finding = Assert.Single(_engine.Evaluate([], history), f =>
            f.Fingerprint.StartsWith("network:saturation:", StringComparison.Ordinal));

        Assert.Equal(EvidenceCategory.Network, finding.Category);
        Assert.Contains("no implica por sí sola congestión externa", finding.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameCorrelationKey_MergesEvidenceInsteadOfDroppingIt()
    {
        var now = DateTime.UtcNow;
        var first = new EvidenceRecord(
            "e1", now.AddSeconds(-10), "Eventos", EvidenceCategory.Reliability,
            "Servicio A", "Evento", "7000", "EventId", EvidenceSeverity.Warning,
            DetectionConfidence.High, "reliability:service-failure:test", "Fallo 1");
        var second = first with
        {
            Id = "e2",
            TimestampUtc = now,
            Message = "Fallo 2",
            Severity = EvidenceSeverity.Critical
        };

        var findings = _engine.Evaluate([first, second], []);

        var finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Equal(2, finding.EvidenceIds.Count);
        Assert.Contains("e1", finding.EvidenceIds);
        Assert.Contains("e2", finding.EvidenceIds);
    }

    [Fact]
    public void StaleProcessGrowth_DoesNotRemainActiveAfterProcessStopsReporting()
    {
        var now = DateTime.UtcNow;
        var gib = 1024d * 1024d * 1024d;
        var history = new[]
        {
            ProcessMetric("s1", now.AddMinutes(-70), "process.memory.private_bytes", "oldapp (PID 10)", 0.5 * gib, "bytes"),
            ProcessMetric("s2", now.AddMinutes(-50), "process.memory.private_bytes", "oldapp (PID 10)", 0.9 * gib, "bytes"),
            ProcessMetric("s3", now.AddMinutes(-30), "process.memory.private_bytes", "oldapp (PID 10)", 1.3 * gib, "bytes"),
            ProcessMetric("s4", now.AddMinutes(-10), "process.memory.private_bytes", "oldapp (PID 10)", 1.8 * gib, "bytes")
        };

        Assert.DoesNotContain(_engine.Evaluate([], history, now), f =>
            f.Fingerprint.StartsWith("performance:process-memory-growth:", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalCpuSaturation_KeepsDistributedProcessConsumersForAttribution()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            Metric("c1", now.AddMinutes(-2), "system.cpu.percent", 98),
            Metric("c2", now.AddMinutes(-1), "system.cpu.percent", 97),
            Metric("c3", now, "system.cpu.percent", 96),
            ProcessMetric("p1", now.AddMinutes(-2), "process.cpu.percent", "worker-a (PID 1)", 22, "%"),
            ProcessMetric("p2", now.AddMinutes(-1), "process.cpu.percent", "worker-a (PID 1)", 21, "%"),
            ProcessMetric("p3", now, "process.cpu.percent", "worker-a (PID 1)", 20, "%")
        };

        var findings = _engine.Evaluate([], history, now);

        Assert.Contains(findings, f => f.Fingerprint == "performance:sustained-cpu-pressure");
        Assert.Contains(findings, f => f.Fingerprint.StartsWith("performance:process-cpu:", StringComparison.Ordinal));
    }

    [Fact]
    public void StaleGlobalCpuSaturation_DoesNotLowerProcessThreshold()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            Metric("c1", now.AddMinutes(-8), "system.cpu.percent", 98),
            Metric("c2", now.AddMinutes(-7), "system.cpu.percent", 97),
            Metric("c3", now.AddMinutes(-6), "system.cpu.percent", 96),
            ProcessMetric("p1", now.AddMinutes(-2), "process.cpu.percent", "worker-a (PID 1)", 20, "%"),
            ProcessMetric("p2", now.AddMinutes(-1), "process.cpu.percent", "worker-a (PID 1)", 21, "%"),
            ProcessMetric("p3", now, "process.cpu.percent", "worker-a (PID 1)", 22, "%")
        };

        Assert.DoesNotContain(_engine.Evaluate([], history, now), f =>
            f.Fingerprint.StartsWith("performance:process-cpu:", StringComparison.Ordinal));
    }

    private static MetricSample Metric(string id, DateTime timestamp, string name, double value) =>
        new(id, timestamp, EvidenceCategory.Performance, "Sistema", name, value, "%");

    private static MetricSample ProcessMetric(string id, DateTime timestamp, string name, string entity, double value, string unit) =>
        new(id, timestamp, EvidenceCategory.Performance, entity, name, value, unit);

    private static MetricSample StorageMetric(string id, DateTime timestamp, double freeBytes) =>
        new(id, timestamp, EvidenceCategory.Storage, "C:", "storage.free_bytes", freeBytes, "bytes");
}
