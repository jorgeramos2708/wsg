namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>Comprobación individual del preflight de remediación.</summary>
public sealed record PreflightCheck(
    string Name,
    bool Passed,
    bool Blocking,
    string Detail);

/// <summary>
/// Resultado del preflight. Allowed solo es true si ninguna comprobación de bloqueo falló;
/// ante cualquier duda el motor produce Allowed = false con los motivos enumerados.
/// </summary>
public sealed record PreflightResult(
    IReadOnlyList<PreflightCheck> Checks,
    bool Allowed,
    IReadOnlyList<string> BlockingReasons);
