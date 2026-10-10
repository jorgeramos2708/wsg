namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>
/// Veredicto parseado de la salida de una acción de remediación.
/// El valor 0 es Indeterminate: ante cualquier duda, el resultado no se interpreta como éxito.
/// </summary>
public enum RemediationParsedVerdict
{
    Indeterminate = 0,
    Repaired,
    NoDamageFound,
    NotRepairable,
    Cancelled
}
