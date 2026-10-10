using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Decide si una acción de remediación está permitida para una causa raíz y un estado
/// de incidente. Fail-closed: solo dos causas raíz producen plan; cualquier otro valor,
/// estado distinto de Active o causa no soportada se rechaza con motivo explícito.
/// </summary>
public sealed class RemediationPolicyEngine
{
    public bool TryBuildPlan(
        string? rootCauseKind,
        IncidentState incidentState,
        out RemediationPlan plan,
        out string blockedReason)
    {
        plan = null!;
        blockedReason = string.Empty;

        if (string.IsNullOrWhiteSpace(rootCauseKind))
        {
            blockedReason = "Sin causa raíz identificada: la remediación no está permitida.";
            return false;
        }

        if (incidentState != IncidentState.Active)
        {
            blockedReason = $"Solo los incidentes en estado Active ofrecen remediación; el incidente está en {incidentState}.";
            return false;
        }

        switch (rootCauseKind)
        {
            case "ComponentStoreCorruption":
                plan = new RemediationPlan(
                    RemediationActionId.ComponentStoreRepair,
                    rootCauseKind,
                    CatalogFor(RemediationActionId.ComponentStoreRepair).DisplayCommand,
                    "Repara el almacén de componentes de Windows usando Windows Update como fuente. " +
                    "Puede tardar hasta 60 minutos, usar red y requerir reinicio si DISM lo indica; " +
                    "/NoRestart evita que el equipo se reinicie solo.",
                    CatalogFor(RemediationActionId.ComponentStoreRepair).Timeout);
                return true;

            case "SystemFileIntegrity":
                plan = new RemediationPlan(
                    RemediationActionId.SystemFileRepair,
                    rootCauseKind,
                    CatalogFor(RemediationActionId.SystemFileRepair).DisplayCommand,
                    "Sustituye los archivos protegidos de Windows que no pasen la verificación de integridad. " +
                    "Puede tardar hasta 45 minutos; no reinicia el equipo y deja detalle en CBS.log.",
                    CatalogFor(RemediationActionId.SystemFileRepair).Timeout);
                return true;

            case "ComponentStoreNonRepairable":
                blockedReason =
                    "DISM ya reportó el almacén de componentes como no reparable con el flujo normal de " +
                    "servicing (/RestoreHealth no lo corrige). WSG no intenta la remediación y conserva el incidente.";
                return false;

            default:
                blockedReason = $"La causa raíz '{rootCauseKind}' no está soportada para remediación en 0.5.0.";
                return false;
        }
    }

    private static RemediationCommandCatalog.CommandSpec CatalogFor(RemediationActionId action) =>
        RemediationCommandCatalog.For(action);
}
