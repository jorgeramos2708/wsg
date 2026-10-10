using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Recommendations;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class IntegrityIncidentTests
{
    private readonly RecommendationEngine _recommendations = new();
    private readonly IncidentEngine _engine;

    public IntegrityIncidentTests()
    {
        _engine = new IncidentEngine(new RootCauseCorrelationEngine(), _recommendations);
    }

    [Fact]
    public void ComponentStoreWarning_IsCorrelatedAsRepairableCorruption()
    {
        var incident = Single(Finding("servicing:component-store", FindingSeverity.Warning, EvidenceCategory.Servicing, "Component Store"));

        Assert.Equal("ComponentStoreCorruption", incident.RootCause.Kind);
        Assert.Contains("Salud del almacén de componentes", incident.VerificationCollectors ?? []);
        Assert.All(incident.Recommendations, step => Assert.False(step.AutomationAllowed));
    }

    [Fact]
    public void ComponentStoreCritical_IsCorrelatedAsNonRepairable()
    {
        var incident = Single(Finding("servicing:component-store", FindingSeverity.Critical, EvidenceCategory.Servicing, "Component Store", "no reparable"));

        Assert.Equal("ComponentStoreNonRepairable", incident.RootCause.Kind);
        Assert.Equal(WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Critical, incident.Severity);
    }


    [Fact]
    public void ComponentStoreDeepFinding_UsesOnlyDeepCollectorForVerification()
    {
        var incident = Single(Finding("servicing:component-store-deep", FindingSeverity.Warning, EvidenceCategory.Servicing, "Component Store"));

        Assert.Equal("Escaneo profundo del almacén de componentes", Assert.Single(incident.VerificationCollectors ?? []));
    }

    [Fact]
    public void SystemFileFinding_UsesVerifyOnlyCauseAndCollector()
    {
        var incident = Single(Finding("integrity:system-files", FindingSeverity.Warning, EvidenceCategory.Integrity, "Archivos protegidos"));

        Assert.Equal("SystemFileIntegrity", incident.RootCause.Kind);
        Assert.Contains("Verificación de archivos protegidos", incident.VerificationCollectors ?? []);
        Assert.All(incident.Recommendations, step => Assert.False(step.AutomationAllowed));
    }

    [Fact]
    public void FileSystemFinding_UsesFileSystemCause()
    {
        var incident = Single(Finding("integrity:filesystem-dirty:c", FindingSeverity.Critical, EvidenceCategory.Integrity, "C:"));

        Assert.Equal("FileSystemIntegrity", incident.RootCause.Kind);
        Assert.Contains("Integridad del sistema de archivos", incident.VerificationCollectors ?? []);
    }

    [Fact]
    public void FileSystemDeepScan_RequiresDeepCollectorForVerification()
    {
        var incident = Single(Finding("integrity:filesystem-scan:c", FindingSeverity.Warning, EvidenceCategory.Integrity, "C:"));

        Assert.Equal("Escaneo profundo del sistema de archivos", Assert.Single(incident.VerificationCollectors ?? []));
    }

    [Fact]
    public void WindowsUpdateFailure_IsCorrelatedWithoutAutomaticReset()
    {
        var incident = Single(Finding("servicing:windows-update-failure", FindingSeverity.Warning, EvidenceCategory.Servicing, "Windows Update"));

        Assert.Equal("WindowsUpdateFailure", incident.RootCause.Kind);
        Assert.Contains("Salud de Windows Update", incident.VerificationCollectors ?? []);
        Assert.All(incident.Recommendations, step => Assert.False(step.AutomationAllowed));
        Assert.Contains(incident.Recommendations, step => step.Detail.Contains("SoftwareDistribution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PendingReboot_IsCorrelatedButNeverAutomated()
    {
        var incident = Single(Finding("servicing:update-reboot-required", FindingSeverity.Warning, EvidenceCategory.Servicing, "Windows Update"));

        Assert.Equal("PendingServicingReboot", incident.RootCause.Kind);
        Assert.All(incident.Recommendations, step => Assert.False(step.AutomationAllowed));
        Assert.Contains(incident.Recommendations, step => step.MayRequireRestart);
    }

    [Fact]
    public void WinReFinding_UsesRecoveryCollector()
    {
        var incident = Single(Finding("recovery:winre", FindingSeverity.Warning, EvidenceCategory.Recovery, "Windows RE"));

        Assert.Equal("RecoveryEnvironmentUnavailable", incident.RootCause.Kind);
        Assert.Contains("Preparación de recuperación WinRE", incident.VerificationCollectors ?? []);
    }

    [Fact]
    public void VssFinding_UsesBackupCollector()
    {
        var incident = Single(Finding("backup:vss-writers", FindingSeverity.Warning, EvidenceCategory.Backup, "VSS Writers"));

        Assert.Equal("VssWriterFailure", incident.RootCause.Kind);
        Assert.Contains("Preparación VSS / respaldo", incident.VerificationCollectors ?? []);
    }

    [Theory]
    [InlineData("FileSystemIntegrity")]
    [InlineData("ComponentStoreCorruption")]
    [InlineData("ComponentStoreNonRepairable")]
    [InlineData("SystemFileIntegrity")]
    [InlineData("WindowsUpdateFailure")]
    [InlineData("PendingServicingReboot")]
    [InlineData("RecoveryEnvironmentUnavailable")]
    [InlineData("VssWriterFailure")]
    public void IntegrityAndServicingPlans_AreRecommendationOnly(string kind)
    {
        var steps = _recommendations.Build(kind);

        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.False(step.AutomationAllowed));
        Assert.All(steps, step => Assert.False(string.IsNullOrWhiteSpace(step.Verification)));
    }

    private WindowsSystemGuardian.Contracts.Incidents.Incident Single(Finding finding) =>
        Assert.Single(_engine.Build([finding], [], []));

    private static Finding Finding(
        string fingerprint,
        FindingSeverity severity,
        EvidenceCategory category,
        string entity,
        string explanation = "explicación")
    {
        var now = DateTime.UtcNow;
        return new Finding(
            $"id:{fingerprint}:{now.Ticks}",
            fingerprint,
            now,
            severity,
            category,
            fingerprint,
            explanation,
            "recomendación",
            DetectionConfidence.High,
            entity,
            [$"evidence:{fingerprint}"],
            now,
            now,
            1,
            FindingLifecycleState.Active);
    }
}
