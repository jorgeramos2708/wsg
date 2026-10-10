using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Abstractions;

/// <summary>
/// Lanzador de la ejecución elevada de una remediación ya aprobada. Core no conoce
/// Process: la implementación real (capa Windows) lanza PrivilegedHost con UAC y
/// devuelve el resultado estructurado que este escribe a disco.
/// </summary>
public interface IElevatedRemediationLauncher
{
    Task<RemediationOutcome> LaunchAsync(
        RemediationPlan plan,
        Guid requestId,
        CancellationToken cancellationToken);
}
