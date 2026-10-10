using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class RemediationVerificationTests
{
    private static readonly RemediationOutcome RepairedOutcome = new(
        RemediationOutcome.CurrentSchemaVersion,
        RemediationActionId.ComponentStoreRepair,
        Guid.NewGuid(),
        ExitCode: 0,
        TimedOut: false,
        OutputTail: "The restore operation completed successfully.",
        RemediationParsedVerdict.Repaired,
        DateTime.UtcNow);

    private static readonly RemediationOutcome IndeterminateOutcome = new(
        RemediationOutcome.CurrentSchemaVersion,
        RemediationActionId.ComponentStoreRepair,
        Guid.NewGuid(),
        ExitCode: 1,
        TimedOut: false,
        OutputTail: "salida no concluyente",
        RemediationParsedVerdict.Indeterminate,
        DateTime.UtcNow);

    private static readonly Incident Incident = new(
        Id: "incident:integrity:system:test",
        Fingerprint: "incident:integrity:system:test",
        FirstSeenUtc: DateTime.UtcNow.AddHours(-2),
        LastSeenUtc: DateTime.UtcNow,
        IncidentSeverity.Critical,
        IncidentState.Active,
        Title: "Corrupción del Component Store",
        Summary: "resumen",
        Impact: "impacto",
        RootCause: new RootCauseCandidate(
            "ComponentStoreCorruption",
            "Corrupción del Component Store",
            "explicación",
            DetectionConfidence.High,
            94,
            Array.Empty<string>(),
            Array.Empty<string>()),
        AffectedEntities: Array.Empty<string>(),
        FindingFingerprints: ["dism:component-store:check"],
        EvidenceIds: Array.Empty<string>(),
        Recommendations: Array.Empty<RecommendationStep>(),
        VerificationCriteria: Array.Empty<string>(),
        VerificationCollectors: ["Salud del almacén de componentes"]);

    private readonly RemediationVerificationService _sut = new();

    [Fact]
    public void RepairedOutcome_WithoutActiveFindings_IsSuccessAndClean()
    {
        var result = _sut.Evaluate(RepairedOutcome, Incident, Array.Empty<Finding>());

        Assert.True(result.ExecutionReportedSuccess);
        Assert.True(result.VerificationClean);
        Assert.Contains(result.Notes, n => n.Contains("dos capturas limpias", StringComparison.Ordinal));
    }

    [Fact]
    public void RepairedOutcome_WithStillActiveFindings_IsNotClean()
    {
        var stillActive = FindingFor("dism:component-store:check");

        var result = _sut.Evaluate(RepairedOutcome, Incident, [stillActive]);

        Assert.True(result.ExecutionReportedSuccess);
        Assert.False(result.VerificationClean);
        Assert.Contains(result.Notes, n => n.Contains("aún contiene hallazgos activos", StringComparison.Ordinal));
    }

    [Fact]
    public void IndeterminateOutcome_NeverReportsSuccess()
    {
        var result = _sut.Evaluate(IndeterminateOutcome, Incident, Array.Empty<Finding>());

        Assert.False(result.ExecutionReportedSuccess);
        Assert.Contains(result.Notes, n => n.Contains("NO reportó", StringComparison.Ordinal));
    }

    [Fact]
    public void TimedOutOutcome_AddsExplicitNote()
    {
        var timedOut = RepairedOutcome with { TimedOut = true, Verdict = RemediationParsedVerdict.Indeterminate };

        var result = _sut.Evaluate(timedOut, Incident, Array.Empty<Finding>());

        Assert.False(result.ExecutionReportedSuccess);
        Assert.Contains(result.Notes, n => n.Contains("tiempo máximo", StringComparison.Ordinal));
    }

    [Fact]
    public void NotRepairableOutcome_AddsAdvancedOptionsNote()
    {
        var notRepairable = RepairedOutcome with { Verdict = RemediationParsedVerdict.NotRepairable };

        var result = _sut.Evaluate(notRepairable, Incident, Array.Empty<Finding>());

        Assert.False(result.ExecutionReportedSuccess);
        Assert.Contains(result.Notes, n => n.Contains("no reparable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OtherFindings_DoNotAffectIncidentCleanliness()
    {
        var unrelated = FindingFor("performance:sustained-cpu-pressure");

        var result = _sut.Evaluate(RepairedOutcome, Incident, [unrelated]);

        Assert.True(result.VerificationClean);
    }

    [Fact]
    public void GetVerificationCollectorNames_UsesIncidentDeclaredCollectors()
    {
        var names = _sut.GetVerificationCollectorNames(Incident);

        Assert.Equal(["Salud del almacén de componentes"], names);
    }

    [Fact]
    public void GetVerificationCollectorNames_WithoutDeclaredCollectors_IsEmpty()
    {
        var withoutCollectors = Incident with { VerificationCollectors = null };

        Assert.Empty(_sut.GetVerificationCollectorNames(withoutCollectors));
    }

    private static Finding FindingFor(string fingerprint) => new(
        Id: $"finding:{fingerprint}",
        Fingerprint: fingerprint,
        DetectedAtUtc: DateTime.UtcNow,
        FindingSeverity.Warning,
        EvidenceCategory.Servicing,
        Title: "hallazgo de prueba",
        Explanation: "explicación",
        Recommendation: "recomendación",
        DetectionConfidence.High,
        AffectedEntity: "system",
        EvidenceIds: Array.Empty<string>());
}
