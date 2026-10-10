namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>
/// Resultado estructurado que PrivilegedHost escribe a disco al terminar una ejecución.
/// SchemaVersion permite evolucionar el contrato sin ambigüedad (cambiarlo requiere aprobación explícita).
/// </summary>
public sealed record RemediationOutcome(
    int SchemaVersion,
    RemediationActionId Action,
    Guid RequestId,
    int ExitCode,
    bool TimedOut,
    string OutputTail,
    RemediationParsedVerdict Verdict,
    DateTime CompletedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
}
