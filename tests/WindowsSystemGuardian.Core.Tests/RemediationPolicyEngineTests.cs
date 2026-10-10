using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class RemediationPolicyEngineTests
{
    private readonly RemediationPolicyEngine _sut = new();

    [Fact]
    public void ComponentStoreCorruption_Active_IsAllowedWithRestoreHealth()
    {
        var allowed = _sut.TryBuildPlan(
            "ComponentStoreCorruption",
            IncidentState.Active,
            out var plan,
            out var blockedReason);

        Assert.True(allowed);
        Assert.Equal(RemediationActionId.ComponentStoreRepair, plan.Action);
        Assert.Contains("/RestoreHealth", plan.CommandDisplay, StringComparison.Ordinal);
        Assert.Contains("/NoRestart", plan.CommandDisplay, StringComparison.Ordinal);
        Assert.True(plan.Timeout > TimeSpan.Zero);
        Assert.Equal(string.Empty, blockedReason);
    }

    [Fact]
    public void SystemFileIntegrity_Active_IsAllowedWithScannow()
    {
        var allowed = _sut.TryBuildPlan(
            "SystemFileIntegrity",
            IncidentState.Active,
            out var plan,
            out var blockedReason);

        Assert.True(allowed);
        Assert.Equal(RemediationActionId.SystemFileRepair, plan.Action);
        Assert.Equal("sfc.exe /scannow", plan.CommandDisplay);
        Assert.True(plan.Timeout > TimeSpan.Zero);
        Assert.Equal(string.Empty, blockedReason);
    }

    [Theory]
    [InlineData(IncidentState.Monitoring)]
    [InlineData(IncidentState.Resolved)]
    public void NonActiveIncidents_AreBlocked(IncidentState state)
    {
        var allowed = _sut.TryBuildPlan(
            "ComponentStoreCorruption",
            state,
            out var plan,
            out var blockedReason);

        Assert.False(allowed);
        Assert.Null(plan);
        Assert.Contains("Active", blockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentStoreNonRepairable_IsBlockedExplicitly()
    {
        var allowed = _sut.TryBuildPlan(
            "ComponentStoreNonRepairable",
            IncidentState.Active,
            out var plan,
            out var blockedReason);

        Assert.False(allowed);
        Assert.Null(plan);
        Assert.Contains("no reparable", blockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("WindowsUpdateFailure")]
    [InlineData("PendingServicingReboot")]
    [InlineData("RecoveryEnvironmentUnavailable")]
    [InlineData("VssWriterFailure")]
    [InlineData("FileSystemIntegrity")]
    [InlineData("MemoryPressure")]
    [InlineData("Generic")]
    public void OtherKnownRootCauses_AreBlocked(string rootCauseKind)
    {
        var allowed = _sut.TryBuildPlan(
            rootCauseKind,
            IncidentState.Active,
            out var plan,
            out var blockedReason);

        Assert.False(allowed);
        Assert.Null(plan);
        Assert.Contains("no está soportada", blockedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingRootCause_IsFailClosed(string? rootCauseKind)
    {
        var allowed = _sut.TryBuildPlan(
            rootCauseKind,
            IncidentState.Active,
            out var plan,
            out var blockedReason);

        Assert.False(allowed);
        Assert.Null(plan);
        Assert.False(string.IsNullOrWhiteSpace(blockedReason));
    }

    [Fact]
    public void Catalog_HasExactlyTheTwoSupportedActions()
    {
        var componentStore = RemediationCommandCatalog.For(RemediationActionId.ComponentStoreRepair);
        var systemFiles = RemediationCommandCatalog.For(RemediationActionId.SystemFileRepair);

        Assert.Equal("dism.exe", componentStore.ExecutableName);
        Assert.Equal("/Online /Cleanup-Image /RestoreHealth /NoRestart /English", componentStore.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(60), componentStore.Timeout);
        Assert.Equal("sfc.exe", systemFiles.ExecutableName);
        Assert.Equal("/scannow", systemFiles.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(45), systemFiles.Timeout);
    }

    [Fact]
    public void Catalog_ForNoneOrUnsupported_ThrowsFailClosed()
    {
        Assert.Throws<InvalidOperationException>(
            () => RemediationCommandCatalog.For(RemediationActionId.None));
        Assert.Throws<InvalidOperationException>(
            () => RemediationCommandCatalog.For((RemediationActionId)999));
    }
}
