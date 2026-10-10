using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Incidents;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class IncidentLifecycleTrackerTests
{
    [Fact]
    public void TwoFullCleanChecks_ResolveIncident()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        var incident = CreateIncident(now);

        tracker.Apply([incident], true, now);
        var firstClear = tracker.Apply([], true, now.AddMinutes(1));
        var secondClear = tracker.Apply([], true, now.AddMinutes(2));

        Assert.Contains(firstClear, i => i.State == IncidentState.Monitoring);
        var resolved = Assert.Single(secondClear, i => i.State == IncidentState.Resolved);
        Assert.NotNull(resolved.ResolvedAtUtc);
        Assert.Equal(2, resolved.ConsecutiveClearChecks);
    }

    [Fact]
    public void PartialCoverage_DoesNotResolveIncident()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        tracker.Apply([CreateIncident(now)], true, now);

        var transitions = tracker.Apply([], false, now.AddMinutes(1));
        var latest = Assert.Single(tracker.Snapshot());

        Assert.Empty(transitions);
        Assert.Equal(IncidentState.Active, latest.State);
    }

    [Fact]
    public void ReappearingIncident_ResetsVerificationState()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        var incident = CreateIncident(now);
        tracker.Apply([incident], true, now);
        tracker.Apply([], true, now.AddMinutes(1));

        var update = Assert.Single(tracker.Apply([CreateIncident(now.AddMinutes(2))], true, now.AddMinutes(2)));

        Assert.Equal(IncidentState.Active, update.State);
        Assert.Equal(0, update.ConsecutiveClearChecks);
        Assert.Null(update.ResolvedAtUtc);
        Assert.Equal(incident.FirstSeenUtc, update.FirstSeenUtc);
    }

    [Fact]
    public void ReappearingAfterResolution_StartsNewEpisode()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        tracker.Apply([CreateIncident(now)], true, now);
        tracker.Apply([], true, now.AddMinutes(1));
        tracker.Apply([], true, now.AddMinutes(2));

        var reappearedAt = now.AddMinutes(10);
        var update = Assert.Single(tracker.Apply([CreateIncident(reappearedAt)], true, reappearedAt));

        Assert.Equal(IncidentState.Active, update.State);
        Assert.Equal(reappearedAt, update.FirstSeenUtc);
    }

    [Fact]
    public void DomainVerification_OnlyAdvancesWhenRequiredCollectorSucceeded()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        var incident = CreateIncident(now) with { VerificationCollectors = ["Rendimiento"] };
        tracker.Apply([incident], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" }, now);

        var noCoverage = tracker.Apply([], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Red" }, now.AddMinutes(1));
        Assert.Empty(noCoverage);
        Assert.Equal(IncidentState.Active, Assert.Single(tracker.Snapshot()).State);

        var firstClear = tracker.Apply([], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" }, now.AddMinutes(2));
        Assert.Contains(firstClear, i => i.State == IncidentState.Monitoring);

        var secondClear = tracker.Apply([], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" }, now.AddMinutes(3));
        var resolved = Assert.Single(secondClear, i => i.State == IncidentState.Resolved);
        Assert.Equal(now.AddMinutes(3), resolved.StateChangedAtUtc);
    }

    [Fact]
    public void ActiveObservation_PreservesStateTransitionTimestamp()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        var first = Assert.Single(tracker.Apply([CreateIncident(now)], true, now));

        var later = Assert.Single(tracker.Apply([CreateIncident(now.AddMinutes(5))], true, now.AddMinutes(5)));

        Assert.Equal(IncidentState.Active, later.State);
        Assert.Equal(first.StateChangedAtUtc, later.StateChangedAtUtc);
        Assert.Equal(now.AddMinutes(5), later.LastSeenUtc);
        Assert.Equal(now.AddMinutes(5), IncidentLifecycleTracker.StateTimestamp(later));
    }

    [Fact]
    public void ReappearingFromMonitoring_RecordsNewStateTransitionTimestamp()
    {
        var tracker = new IncidentLifecycleTracker();
        var now = DateTime.UtcNow;
        tracker.Initialize([]);
        var incident = CreateIncident(now) with { VerificationCollectors = ["Rendimiento"] };
        tracker.Apply([incident], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" }, now);
        tracker.Apply([], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" }, now.AddMinutes(1));

        var reappearedAt = now.AddMinutes(2);
        var update = Assert.Single(tracker.Apply(
            [CreateIncident(reappearedAt) with { VerificationCollectors = ["Rendimiento"] }],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rendimiento" },
            reappearedAt));

        Assert.Equal(IncidentState.Active, update.State);
        Assert.Equal(reappearedAt, update.StateChangedAtUtc);
    }

    private static Incident CreateIncident(DateTime timestamp)
    {
        var cause = new RootCauseCandidate(
            "Test", "Causa", "Explicación", DetectionConfidence.High, 90, [], []);
        return new Incident(
            $"incident:test:{timestamp.Ticks}",
            "incident:test",
            timestamp,
            timestamp,
            IncidentSeverity.Warning,
            IncidentState.Active,
            "Prueba",
            "Resumen",
            "Impacto",
            cause,
            ["Sistema"],
            [],
            [],
            [],
            ["Verificar"],
            1);
    }
}
