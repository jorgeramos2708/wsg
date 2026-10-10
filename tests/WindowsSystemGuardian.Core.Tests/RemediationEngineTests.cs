using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class RemediationEngineTests
{
    private static readonly string ComponentStoreCommand =
        RemediationCommandCatalog.For(RemediationActionId.ComponentStoreRepair).DisplayCommand;

    private static RemediationApproval Approval(Guid requestId) => new(
        requestId,
        RemediationActionId.ComponentStoreRepair,
        ComponentStoreCommand,
        DateTime.UtcNow);

    private static RemediationOutcome Outcome(RemediationActionId action, Guid requestId) => new(
        RemediationOutcome.CurrentSchemaVersion,
        action,
        requestId,
        ExitCode: 0,
        TimedOut: false,
        OutputTail: "The restore operation completed successfully.",
        RemediationParsedVerdict.Repaired,
        DateTime.UtcNow);

    private static RemediationEngine CreateEngine(
        FakeProbe? probe = null,
        Func<RemediationPlan, Guid, CancellationToken, Task<RemediationOutcome>>? launcher = null,
        FakeRecordStore? store = null)
    {
        return new RemediationEngine(
            new RemediationPolicyEngine(),
            new PreflightEngine(),
            new ApprovalGate(),
            probe ?? new FakeProbe(),
            new FakeLauncher(launcher ?? ((plan, requestId, _) => Task.FromResult(Outcome(plan.Action, requestId)))),
            store ?? new FakeRecordStore());
    }

    [Fact]
    public async Task HappyPath_ExecutesAndRecordsEveryPhase()
    {
        var requestId = Guid.NewGuid();
        var store = new FakeRecordStore();
        var engine = CreateEngine(store: store);
        var approval = Approval(requestId);

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, approval, TestContext.Current.CancellationToken);

        Assert.True(result.Executed);
        Assert.Equal(RemediationPhase.Execution, result.ReachedPhase);
        Assert.NotNull(result.Outcome);
        Assert.Equal(RemediationParsedVerdict.Repaired, result.Outcome!.Verdict);
        Assert.Equal(requestId, result.RequestId);
        Assert.Contains(store.Records, r => r.Phase == RemediationPhase.Preflight);
        Assert.Contains(store.Records, r => r.Phase == RemediationPhase.Approval);
        Assert.Contains(store.Records, r => r.Phase == RemediationPhase.Execution);
    }

    [Fact]
    public async Task MissingApproval_DoesNotExecute()
    {
        var store = new FakeRecordStore();
        var engine = CreateEngine(store: store);

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, approval: null, TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Equal(RemediationPhase.Approval, result.ReachedPhase);
        Assert.Null(result.Outcome);
        Assert.DoesNotContain(store.Records, r => r.Phase == RemediationPhase.Execution);
        Assert.Contains(result.Messages, m => m.Contains("aprobación", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NotElevated_BlocksAtPreflightWithoutLaunching()
    {
        var launched = false;
        var engine = CreateEngine(
            probe: new FakeProbe(Elevated: false),
            launcher: (_, _, _) => { launched = true; return Task.FromResult(Outcome(RemediationActionId.ComponentStoreRepair, Guid.NewGuid())); });

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, Approval(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Equal(RemediationPhase.Preflight, result.ReachedPhase);
        Assert.False(launched);
        Assert.Contains(result.Messages, m => m.Contains("administrador", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApprovalForDifferentAction_IsRejected()
    {
        var requestId = Guid.NewGuid();
        var wrongActionApproval = new RemediationApproval(
            requestId,
            RemediationActionId.SystemFileRepair,
            "sfc.exe /scannow",
            DateTime.UtcNow);
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, wrongActionApproval, TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Equal(RemediationPhase.Approval, result.ReachedPhase);
    }

    [Fact]
    public async Task ApprovalForDifferentCommand_IsRejected()
    {
        var requestId = Guid.NewGuid();
        var tamperedApproval = new RemediationApproval(
            requestId,
            RemediationActionId.ComponentStoreRepair,
            "DISM.exe /Online /Cleanup-Image /RestoreHealth",
            DateTime.UtcNow);
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, tamperedApproval, TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Equal(RemediationPhase.Approval, result.ReachedPhase);
        Assert.Contains(result.Messages, m => m.Contains("no coincide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondConcurrentExecutionForSameAction_IsBlocked()
    {
        var firstLaunched = new TaskCompletionSource();
        var engine = CreateEngine(launcher: (_, _, _) => firstLaunched.Task.ContinueWith(
            _ => Outcome(RemediationActionId.ComponentStoreRepair, Guid.NewGuid()),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default));

        var first = engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, Approval(Guid.NewGuid()), TestContext.Current.CancellationToken);

        var second = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, Approval(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(second.Executed);
        Assert.Contains(second.Messages, m => m.Contains("en curso", StringComparison.OrdinalIgnoreCase));

        firstLaunched.SetResult();
        var firstResult = await first;
        Assert.True(firstResult.Executed);
    }

    [Fact]
    public async Task LauncherFailure_IsReportedAndRecorded()
    {
        var store = new FakeRecordStore();
        var engine = CreateEngine(
            launcher: (_, _, _) => throw new InvalidOperationException("UAC denegado"),
            store: store);

        var result = await engine.ExecuteAsync(
            "ComponentStoreCorruption", IncidentState.Active, Approval(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Contains(result.Messages, m => m.Contains("No se pudo lanzar", StringComparison.Ordinal));
        Assert.Contains(store.Records, r => r.Phase == RemediationPhase.Execution && r.Summary.Contains("Fallo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedRootCause_IsBlockedByPolicy()
    {
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync(
            "WindowsUpdateFailure", IncidentState.Active, Approval(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(result.Executed);
        Assert.Equal(RemediationPhase.Preflight, result.ReachedPhase);
        Assert.Contains(result.Messages, m => m.Contains("no está soportada", StringComparison.Ordinal));
    }

    private sealed record FakeProbe(
        bool Elevated = true,
        bool PendingReboot = false,
        long FreeBytes = 64_000_000_000L) : IRemediationPreflightProbe
    {
        public bool IsElevated() => Elevated;
        public bool HasPendingReboot() => PendingReboot;
        public long GetSystemDriveFreeSpaceBytes() => FreeBytes;
    }

    private sealed class FakeLauncher(
        Func<RemediationPlan, Guid, CancellationToken, Task<RemediationOutcome>> behavior) : IElevatedRemediationLauncher
    {
        public Task<RemediationOutcome> LaunchAsync(
            RemediationPlan plan, Guid requestId, CancellationToken cancellationToken) =>
            behavior(plan, requestId, cancellationToken);
    }

    private sealed class FakeRecordStore : IRemediationRecordStore
    {
        public List<RemediationRecord> Records { get; } = [];

        public void Append(RemediationRecord record) => Records.Add(record);
    }
}
