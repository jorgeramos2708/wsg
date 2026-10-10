namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>
/// Aprobación explícita del operador para una ejecución concreta. Es válida solo para
/// la misma acción y el mismo requestId; nunca se persiste ni se reutiliza entre ejecuciones.
/// </summary>
public sealed record RemediationApproval(
    Guid RequestId,
    RemediationActionId Action,
    string CommandDisplay,
    DateTime ApprovedAtUtc);
