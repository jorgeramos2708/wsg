namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>
/// Acciones de remediación soportadas en 0.5.0. Fail-closed: cualquier valor
/// fuera de este enum no ejecuta nada y ningún comando se construye desde strings externos.
/// </summary>
public enum RemediationActionId
{
    None = 0,
    ComponentStoreRepair,
    SystemFileRepair
}
