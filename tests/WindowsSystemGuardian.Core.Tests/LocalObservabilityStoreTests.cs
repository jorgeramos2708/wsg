using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Persistence;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class LocalObservabilityStoreTests
{
    [Fact]
    public async Task Retention_DoesNotTraverseNestedDirectories()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(
                root,
                retentionDays: 1,
                maximumBytes: 10L * 1024L * 1024L);

            var nested = Path.Combine(root, "evidence", "nested");
            Directory.CreateDirectory(nested);
            var externalLike = Path.Combine(nested, "2000-01-01.jsonl");
            await File.WriteAllTextAsync(externalLike, new string('x', 1024), TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(externalLike, DateTime.UtcNow.AddDays(-30));

            await store.EnforceRetentionAsync(TestContext.Current.CancellationToken);

            Assert.True(File.Exists(externalLike));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Retention_RejectsManagedFolderReparsePoint_WhenPlatformAllowsCreatingIt()
    {
        var root = CreateTemporaryRoot();
        var external = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(root);
            var managed = Path.Combine(root, "evidence");
            var original = Path.Combine(root, "evidence-original");
            Directory.Move(managed, original);

            try
            {
                Directory.CreateSymbolicLink(managed, external);
            }
            catch (UnauthorizedAccessException)
            {
                Directory.Move(original, managed);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                Directory.Move(original, managed);
                return;
            }
            catch (IOException)
            {
                if (!Directory.Exists(managed))
                    Directory.Move(original, managed);
                return;
            }

            await Assert.ThrowsAsync<IOException>(() => store.EnforceRetentionAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteTemporaryRoot(root);
            DeleteTemporaryRoot(external);
        }
    }

    [Fact]
    public async Task WarningAndCriticalForSameFingerprint_AreBothPersisted()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(root);
            var engine = new WindowsSystemGuardian.Core.Observability.FindingEngine();
            var now = DateTime.UtcNow;

            var warning = new EvidenceRecord(
                "w1", now, "Storage", EvidenceCategory.Storage, "C:",
                "Capacidad", "9", "%", EvidenceSeverity.Warning,
                DetectionConfidence.High, "storage:capacity:test", "warning");

            var critical = warning with
            {
                Id = "c1",
                TimestampUtc = now.AddSeconds(2),
                Severity = EvidenceSeverity.Critical,
                Message = "critical"
            };

            var warningFinding = Assert.Single(engine.Evaluate([warning], []));
            var criticalFinding = Assert.Single(engine.Evaluate([critical], []));

            await store.AppendFindingsAsync([warningFinding, criticalFinding], TestContext.Current.CancellationToken);
            var loaded = await store.LoadFindingsSinceAsync(now.AddMinutes(-1), TestContext.Current.CancellationToken);

            Assert.Contains(loaded, f => f.Severity == FindingSeverity.Warning);
            Assert.Contains(loaded, f => f.Severity == FindingSeverity.Critical);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Incident_IsPersistedAndLoaded()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(root);
            var now = DateTime.UtcNow;
            var cause = new RootCauseCandidate(
                "Test", "Causa", "Explicación", DetectionConfidence.High, 90,
                ["e1"], ["f1"]);
            var incident = new Incident(
                "i1", "incident:test", now.AddMinutes(-1), now,
                IncidentSeverity.Warning, IncidentState.Active,
                "Incidente", "Resumen", "Impacto", cause,
                ["C:"], ["f1"], ["e1"], [], ["Verificar"], 1);

            await store.AppendIncidentsAsync([incident], TestContext.Current.CancellationToken);
            var loaded = await store.LoadIncidentsSinceAsync(now.AddMinutes(-5), TestContext.Current.CancellationToken);

            var persisted = Assert.Single(loaded);
            Assert.Equal("incident:test", persisted.Fingerprint);
            Assert.Equal(90, persisted.RootCause.ConfidencePercent);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Incident_LoadWindowUsesLatestObservationOrStateTransition()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(root);
            var now = DateTime.UtcNow;
            var cause = new RootCauseCandidate(
                "Test", "Causa", "Explicación", DetectionConfidence.High, 90,
                ["e1"], ["f1"]);

            var active = new Incident(
                "i-active", "incident:active", now.AddHours(-2), now,
                IncidentSeverity.Warning, IncidentState.Active,
                "Activo", "Resumen", "Impacto", cause,
                ["Sistema"], ["f1"], ["e1"], [], ["Verificar"], 2,
                StateChangedAtUtc: now.AddHours(-2));

            var monitoring = active with
            {
                Id = "i-monitoring",
                Fingerprint = "incident:monitoring",
                LastSeenUtc = now.AddHours(-2),
                State = IncidentState.Monitoring,
                StateChangedAtUtc = now
            };

            await store.AppendIncidentsAsync([active, monitoring], TestContext.Current.CancellationToken);
            var loaded = await store.LoadIncidentsSinceAsync(now.AddMinutes(-5), TestContext.Current.CancellationToken);

            Assert.Contains(loaded, i => i.Fingerprint == "incident:active");
            Assert.Contains(loaded, i => i.Fingerprint == "incident:monitoring");
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task CorruptJsonLine_DoesNotInvalidateValidHistory()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new LocalObservabilityStore(root);
            var now = DateTime.UtcNow;
            var result = CollectorResult.Success(
                "test",
                DiagnosticCost.VeryLow,
                now,
                metrics:
                [
                    new MetricSample(
                        "m1", now, EvidenceCategory.Performance,
                        "CPU", "system.cpu.percent", 10, "%")
                ]);

            await store.AppendCollectorResultAsync(result, TestContext.Current.CancellationToken);

            var file = Path.Combine(root, "metrics", $"{now:yyyy-MM-dd}.jsonl");
            await File.AppendAllTextAsync(file, "{corrupt-json" + System.Environment.NewLine, TestContext.Current.CancellationToken);

            var loaded = await store.LoadMetricsSinceAsync(now.AddMinutes(-1), TestContext.Current.CancellationToken);

            Assert.Contains(loaded, m => m.Id == "m1");
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void ReparseRoot_IsRejected_WhenPlatformAllowsCreatingIt()
    {
        var parent = CreateTemporaryRoot();
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        Directory.CreateDirectory(target);

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            Assert.Throws<IOException>(() => new LocalObservabilityStore(link));
        }
        finally
        {
            DeleteTemporaryRoot(parent);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "WindowsSystemGuardian.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup only. No product behavior depends on this helper.
        }
    }
}
