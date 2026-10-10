using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Verifica que exista una aprobación explícita del operador, vigente y coincidente
/// con el plan y el requestId de la ejecución. No persiste aprobaciones: cada
/// ejecución exige su propia aprobación en memoria.
/// </summary>
public sealed class ApprovalGate
{
    public bool IsApproved(
        RemediationPlan plan,
        Guid requestId,
        RemediationApproval? approval,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (approval is null)
        {
            reason = "Se requiere la aprobación explícita del operador antes de ejecutar la remediación.";
            return false;
        }

        if (approval.RequestId != requestId)
        {
            reason = "La aprobación no corresponde a esta solicitud de ejecución.";
            return false;
        }

        if (approval.Action != plan.Action)
        {
            reason = "La aprobación corresponde a una acción distinta del plan calculado.";
            return false;
        }

        if (!string.Equals(approval.CommandDisplay, plan.CommandDisplay, StringComparison.Ordinal))
        {
            // La aprobación mostró al operador un comando diferente al plan actual: no ejecutar.
            reason = "El comando aprobado no coincide con el comando del plan calculado.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
