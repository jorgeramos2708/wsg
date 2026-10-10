namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>Fase del flujo de remediación a la que pertenece un registro de auditoría.</summary>
public enum RemediationPhase
{
    Preflight = 0,
    Approval,
    Execution,
    Verification
}

/// <summary>
/// Registro de auditoría append-only de una remediación. Cada fase escribe su propio
/// registro; no se edita ni se elimina ninguno (solo aplica la retención general del store).
/// </summary>
public sealed record RemediationRecord(
    DateTime RecordedAtUtc,
    Guid RequestId,
    RemediationActionId Action,
    RemediationPhase Phase,
    string Summary,
    string Detail);
