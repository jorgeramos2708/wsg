using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>Resultado estructurado de la verificación posterior a una remediación.</summary>
public sealed record RemediationVerificationResult(
    RemediationParsedVerdict ExecutionVerdict,
    bool ExecutionReportedSuccess,
    bool VerificationClean,
    IReadOnlyList<string> Notes);

/// <summary>
/// Interpreta el resultado de una ejecución de remediación y la evidencia posterior de
/// verificación. NO duplica el conteo de verificaciones limpias del ciclo de vida: ese
/// conteo sigue en <see cref="IncidentLifecycleTracker"/> y se alimenta de las capturas
/// que ejecuten los collectors de verificación del incidente (ver
/// <see cref="GetVerificationCollectorNames"/>).
/// </summary>
public sealed class RemediationVerificationService
{
    /// <summary>
    /// Collectors read-only que deben ejecutarse para verificar el incidente tras la
    /// remediación. Es la lista que el propio incidente declara en VerificationCollectors.
    /// </summary>
    public IReadOnlyList<string> GetVerificationCollectorNames(Incident incident) =>
        incident.VerificationCollectors ?? Array.Empty<string>();

    public RemediationVerificationResult Evaluate(
        RemediationOutcome outcome,
        Incident incident,
        IReadOnlyList<Finding> postVerificationFindings)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(postVerificationFindings);

        var executionReportedSuccess = outcome.Verdict
            is RemediationParsedVerdict.Repaired
            or RemediationParsedVerdict.NoDamageFound;

        var incidentFindingFingerprints = incident.FindingFingerprints
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stillActive = postVerificationFindings
            .Where(f => f.State == FindingLifecycleState.Active)
            .Where(f => incidentFindingFingerprints.Contains(f.Fingerprint))
            .Select(f => f.Fingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var verificationClean = stillActive.Count == 0;

        var notes = new List<string>();
        if (outcome.TimedOut)
            notes.Add("La ejecución alcanzó el tiempo máximo y fue detenida; el resultado no se interpreta como éxito.");

        notes.Add(executionReportedSuccess
            ? $"La ejecución reportó: {outcome.Verdict}."
            : $"La ejecución NO reportó un resultado concluyente ({outcome.Verdict}). El incidente permanece sin cambios.");

        if (!verificationClean)
            notes.Add($"La evidencia posterior de verificación aún contiene hallazgos activos del incidente: {string.Join(", ", stillActive)}.");
        else if (executionReportedSuccess)
            notes.Add("La verificación posterior no encuentra hallazgos activos del incidente. " +
                      "Se requieren dos capturas limpias de sus collectors de verificación para dar el incidente por resuelto.");

        if (outcome.Verdict == RemediationParsedVerdict.NotRepairable)
            notes.Add("La reparación reporta no reparable: considere las opciones avanzadas documentadas fuera de WSG.");

        return new RemediationVerificationResult(
            outcome.Verdict,
            executionReportedSuccess,
            verificationClean,
            notes);
    }
}
