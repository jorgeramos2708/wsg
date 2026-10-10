using WindowsSystemGuardian.Contracts.Incidents;

namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>
/// Plan de remediación calculado por la política para una causa raíz soportada.
/// CommandDisplay es el comando exacto que el operador aprueba y que PrivilegedHost ejecuta.
/// </summary>
public sealed record RemediationPlan(
    RemediationActionId Action,
    string RootCauseKind,
    string CommandDisplay,
    string RiskSummary,
    TimeSpan Timeout);
