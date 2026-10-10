using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Recommendations;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class IncidentEngineTests
{
    private readonly IncidentEngine _engine = new(
        new RootCauseCorrelationEngine(),
        new RecommendationEngine());

    [Fact]
    public void MemoryPressurePlusProcessMemory_CorrelatesProcessCause()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("performance:sustained-memory-pressure", now.AddMinutes(-2), FindingSeverity.Critical, EvidenceCategory.Performance, "Memoria"),
            Finding("performance:process-memory-share:sqlservr", now, FindingSeverity.Warning, EvidenceCategory.Performance, "sqlservr (PID 42)")
        };

        var incident = Assert.Single(_engine.Build(findings, [], []));

        Assert.Equal("MemoryPressureByProcess", incident.RootCause.Kind);
        Assert.Contains("sqlservr", incident.RootCause.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        Assert.NotEmpty(incident.Recommendations);
        Assert.All(incident.Recommendations, step => Assert.False(step.AutomationAllowed));
        Assert.NotEmpty(incident.VerificationCriteria);
    }

    [Fact]
    public void StoragePressurePlusIoEvent_CorrelatesStorageReliability()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("storage:rapid-growth:c", now.AddMinutes(-3), FindingSeverity.Warning, EvidenceCategory.Storage, "C:"),
            Finding("evidence:reliability:storage-io", now, FindingSeverity.Critical, EvidenceCategory.Reliability, "C:")
        };

        var incident = Assert.Single(_engine.Build(findings, [], []));

        Assert.Equal("StorageIoReliability", incident.RootCause.Kind);
        Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        Assert.True(incident.RootCause.ConfidencePercent >= 90);
    }

    [Fact]
    public void DistantFindings_AreSeparatedIntoDifferentIncidents()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("performance:sustained-memory-pressure", now.AddHours(-1), FindingSeverity.Warning, EvidenceCategory.Performance, "Memoria"),
            Finding("network:saturation:ethernet", now, FindingSeverity.Warning, EvidenceCategory.Network, "Ethernet")
        };

        var incidents = _engine.Build(findings, [], []);

        Assert.Equal(2, incidents.Count);
    }

    [Fact]
    public void ProcessResourceGrowth_UsesNonAutomaticRecommendationPlan()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("performance:process-handle-growth:agent", now, FindingSeverity.Warning, EvidenceCategory.Performance, "agent (PID 33)")
        };

        var incident = Assert.Single(_engine.Build(findings, [], []));

        Assert.Equal("ProcessResourceGrowth", incident.RootCause.Kind);
        Assert.Contains(incident.Recommendations, r => r.Title.Contains("crecimiento", StringComparison.OrdinalIgnoreCase));
        Assert.All(incident.Recommendations, r => Assert.False(r.AutomationAllowed));
    }

    [Fact]
    public void DifferentStorageVolumes_AreNotMerged()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("storage:rapid-growth:c", now, FindingSeverity.Warning, EvidenceCategory.Storage, "C:"),
            Finding("storage:rapid-growth:d", now, FindingSeverity.Warning, EvidenceCategory.Storage, "D:")
        };

        var incidents = _engine.Build(findings, [], []);

        Assert.Equal(2, incidents.Count);
        Assert.Contains(incidents, i => i.AffectedEntities.Contains("C:"));
        Assert.Contains(incidents, i => i.AffectedEntities.Contains("D:"));
    }

    [Fact]
    public void DifferentNetworkInterfaces_AreNotMerged()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("network:saturation:ethernet", now, FindingSeverity.Warning, EvidenceCategory.Network, "Ethernet"),
            Finding("network:saturation:backup", now, FindingSeverity.Warning, EvidenceCategory.Network, "Backup NIC")
        };

        Assert.Equal(2, _engine.Build(findings, [], []).Count);
    }

    [Fact]
    public void DifferentServices_AreNotMerged()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("evidence:reliability:service-failure:spooler", now, FindingSeverity.Warning, EvidenceCategory.Services, "Spooler"),
            Finding("evidence:reliability:service-failure:w32time", now, FindingSeverity.Warning, EvidenceCategory.Services, "W32Time")
        };

        Assert.Equal(2, _engine.Build(findings, [], []).Count);
    }

    [Fact]
    public void SystemMemoryIncident_KeepsStableFingerprintWhenCauseIsRefined()
    {
        var now = DateTime.UtcNow;
        var initial = _engine.Build(
            [Finding("performance:sustained-memory-pressure", now, FindingSeverity.Warning, EvidenceCategory.Performance, "Memoria")],
            [],
            []);

        var refined = _engine.Build(
            [
                Finding("performance:sustained-memory-pressure", now.AddMinutes(1), FindingSeverity.Warning, EvidenceCategory.Performance, "Memoria"),
                Finding("performance:process-memory-share:sql", now.AddMinutes(1), FindingSeverity.Warning, EvidenceCategory.Performance, "sqlservr (PID 42)")
            ],
            [],
            []);

        Assert.Equal(Assert.Single(initial).Fingerprint, Assert.Single(refined).Fingerprint);
        Assert.Equal("MemoryPressureByProcess", Assert.Single(refined).RootCause.Kind);
    }

    [Fact]
    public void MultipleMemoryConsumers_DoNotAttributeRootCauseToOneProcess()
    {
        var now = DateTime.UtcNow;
        var incident = Assert.Single(_engine.Build(
            [
                Finding("performance:sustained-memory-pressure", now, FindingSeverity.Critical, EvidenceCategory.Performance, "Memoria"),
                Finding("performance:process-memory-share:sql", now, FindingSeverity.Warning, EvidenceCategory.Performance, "sqlservr (PID 42)"),
                Finding("performance:process-memory-share:java", now, FindingSeverity.Warning, EvidenceCategory.Performance, "java (PID 88)")
            ],
            [],
            []));

        Assert.Equal("MemoryPressureWithMultipleProcesses", incident.RootCause.Kind);
        Assert.Contains("varios", incident.RootCause.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericStorageIoEvent_IsNotAttributedToLogicalVolumeWithoutIdentity()
    {
        var now = DateTime.UtcNow;
        var findings = new[]
        {
            Finding("storage:rapid-growth:c", now, FindingSeverity.Warning, EvidenceCategory.Storage, "C:"),
            Finding("evidence:reliability:storage-io", now, FindingSeverity.Critical, EvidenceCategory.Reliability, "Almacenamiento")
        };

        var incidents = _engine.Build(findings, [], []);

        Assert.Equal(2, incidents.Count);
        Assert.Contains(incidents, i => i.RootCause.Kind == "StoragePressure");
        Assert.Contains(incidents, i => i.RootCause.Kind == "StorageReliability");
    }

    [Fact]
    public void MemoryProcessFindingOutsideCorrelationWindow_IsNotAttributedToCurrentPressure()
    {
        var now = DateTime.UtcNow;
        var system = Finding("performance:sustained-memory-pressure", now, FindingSeverity.Critical, EvidenceCategory.Performance, "Memoria");
        var oldProcess = Finding("performance:process-memory-growth:sqlservr", now.AddMinutes(-30), FindingSeverity.Warning, EvidenceCategory.Performance, "sqlservr (PID 42)") with
        {
            FirstSeenUtc = now.AddMinutes(-60),
            LastSeenUtc = now.AddMinutes(-30)
        };

        var incidents = _engine.Build([system, oldProcess], [], []);

        Assert.Equal(2, incidents.Count);
        Assert.Contains(incidents, i => i.RootCause.Kind == "MemoryPressure");
        Assert.Contains(incidents, i => i.RootCause.Kind == "ProcessMemoryGrowth");
    }

    [Fact]
    public void WheaAndCrashOutsideTenMinutes_AreNotMergedAsHardwareInstability()
    {
        var now = DateTime.UtcNow;
        var whea = Finding("evidence:reliability:whea", now.AddMinutes(-25), FindingSeverity.Critical, EvidenceCategory.Reliability, "Hardware");
        var crash = Finding("evidence:reliability:bugcheck", now, FindingSeverity.Critical, EvidenceCategory.Reliability, "BugCheck");

        var incidents = _engine.Build([whea, crash], [], []);

        Assert.Equal(2, incidents.Count);
        Assert.Contains(incidents, i => i.RootCause.Kind == "HardwareError");
        Assert.Contains(incidents, i => i.RootCause.Kind == "WindowsInstability");
    }

    [Fact]
    public void WheaAndCrashWithinTenMinutes_AreMergedAsHardwareInstability()
    {
        var now = DateTime.UtcNow;
        var whea = Finding("evidence:reliability:whea", now.AddMinutes(-3), FindingSeverity.Critical, EvidenceCategory.Reliability, "Hardware");
        var crash = Finding("evidence:reliability:bugcheck", now, FindingSeverity.Critical, EvidenceCategory.Reliability, "BugCheck");

        var incident = Assert.Single(_engine.Build([whea, crash], [], []));

        Assert.Equal("HardwareInstability", incident.RootCause.Kind);
    }

    private static Finding Finding(
        string fingerprint,
        DateTime timestamp,
        FindingSeverity severity,
        EvidenceCategory category,
        string entity) =>
        new(
            $"id:{fingerprint}:{timestamp.Ticks}",
            fingerprint,
            timestamp,
            severity,
            category,
            fingerprint,
            "explicación",
            "recomendación",
            DetectionConfidence.High,
            entity,
            [$"evidence:{fingerprint}"],
            timestamp,
            timestamp,
            1,
            FindingLifecycleState.Active);
}
