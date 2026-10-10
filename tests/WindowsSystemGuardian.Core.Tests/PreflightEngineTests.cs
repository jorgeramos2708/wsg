using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class PreflightEngineTests
{
    private static readonly RemediationPlan ComponentStorePlan = new(
        RemediationActionId.ComponentStoreRepair,
        "ComponentStoreCorruption",
        "DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English",
        "riesgo de prueba",
        TimeSpan.FromMinutes(60));

    private static readonly RemediationPlan SystemFilePlan = new(
        RemediationActionId.SystemFileRepair,
        "SystemFileIntegrity",
        "sfc.exe /scannow",
        "riesgo de prueba",
        TimeSpan.FromMinutes(45));

    private static readonly RemediationPlan ServiceRestartPlan = new(
        RemediationActionId.WindowsUpdateServiceRestart,
        "WindowsUpdateFailure",
        "Reinicio controlado de los servicios Windows Update (bits, wuauserv)",
        "riesgo de prueba",
        TimeSpan.FromMinutes(5));

    private readonly PreflightEngine _sut = new();

    private static IRemediationPreflightProbe Probe(
        bool elevated = true,
        bool pendingReboot = false,
        long freeBytes = 64_000_000_000L) => new FakeProbe(elevated, pendingReboot, freeBytes);

    [Fact]
    public void HealthyEnvironment_IsAllowed()
    {
        var result = _sut.Evaluate(ComponentStorePlan, Probe(), remediationAlreadyRunning: false);

        Assert.True(result.Allowed);
        Assert.Empty(result.BlockingReasons);
        Assert.Equal(5, result.Checks.Count);
    }

    [Fact]
    public void NotElevated_IsBlockedWithAdministratorReason()
    {
        var result = _sut.Evaluate(ComponentStorePlan, Probe(elevated: false), remediationAlreadyRunning: false);

        Assert.False(result.Allowed);
        Assert.Contains(result.BlockingReasons, r => r.Contains("administrador", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PendingReboot_IsBlocked()
    {
        var result = _sut.Evaluate(ComponentStorePlan, Probe(pendingReboot: true), remediationAlreadyRunning: false);

        Assert.False(result.Allowed);
        Assert.Contains(result.BlockingReasons, r => r.Contains("reinicio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemediationAlreadyRunning_IsBlocked()
    {
        var result = _sut.Evaluate(ComponentStorePlan, Probe(), remediationAlreadyRunning: true);

        Assert.False(result.Allowed);
        Assert.Contains(result.BlockingReasons, r => r.Contains("en curso", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsufficientDiskSpace_IsBlocked()
    {
        var result = _sut.Evaluate(
            ComponentStorePlan,
            Probe(freeBytes: PreflightEngine.MinimumSystemDriveFreeBytes - 1),
            remediationAlreadyRunning: false);

        Assert.False(result.Allowed);
        Assert.Contains(result.BlockingReasons, r => r.Contains("Espacio libre", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceRestart_SkipsDiskSpaceCheck_EvenWithNoSpace()
    {
        // Spec 0.6.0 AC3: el chequeo de espacio no aplica a acciones que no consumen disco.
        var result = _sut.Evaluate(
            ServiceRestartPlan,
            Probe(freeBytes: 0),
            remediationAlreadyRunning: false);

        Assert.True(result.Allowed);
        var space = Assert.Single(result.Checks, c => c.Name == "Espacio libre en la unidad del sistema");
        Assert.Contains("No aplica", space.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentStoreRepair_IncludesNonBlockingSourceWarning()
    {
        var result = _sut.Evaluate(ComponentStorePlan, Probe(), remediationAlreadyRunning: false);

        var source = Assert.Single(result.Checks, c => c.Name == "Fuente de reparación");
        Assert.False(source.Blocking);
        Assert.True(source.Passed);
        Assert.Contains("Windows Update", source.Detail, StringComparison.Ordinal);
        Assert.True(result.Allowed);
    }

    [Fact]
    public void SystemFileRepair_DoesNotIncludeSourceWarning()
    {
        var result = _sut.Evaluate(SystemFilePlan, Probe(), remediationAlreadyRunning: false);

        Assert.DoesNotContain(result.Checks, c => c.Name == "Fuente de reparación");
        Assert.True(result.Allowed);
    }

    [Fact]
    public void ProbeFailure_IsConservative()
    {
        // Un probe que lanza produce valores conservadores: sin elevación y sin espacio.
        var result = _sut.Evaluate(ComponentStorePlan, new ThrowingProbe(), remediationAlreadyRunning: false);

        Assert.False(result.Allowed);
        Assert.Contains(result.BlockingReasons, r => r.Contains("administrador", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.BlockingReasons, r => r.Contains("Espacio libre", StringComparison.Ordinal));
    }

    private sealed record FakeProbe(
        bool Elevated,
        bool PendingReboot,
        long FreeBytes) : IRemediationPreflightProbe
    {
        public bool IsElevated() => Elevated;
        public bool HasPendingReboot() => PendingReboot;
        public long GetSystemDriveFreeSpaceBytes() => FreeBytes;
    }

    private sealed class ThrowingProbe : IRemediationPreflightProbe
    {
        public bool IsElevated() => throw new InvalidOperationException("probe roto");
        public bool HasPendingReboot() => throw new InvalidOperationException("probe roto");
        public long GetSystemDriveFreeSpaceBytes() => throw new InvalidOperationException("probe roto");
    }
}
