using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Evalúa las comprobaciones previas a una remediación. Fail-closed: si alguna
/// comprobación de bloqueo no se puede afirmar, el resultado es Blocked con motivos.
/// </summary>
public sealed class PreflightEngine
{
    /// <summary>Espacio mínimo en la unidad del sistema para ejecutar servicing con garantías.</summary>
    public const long MinimumSystemDriveFreeBytes = 1_000_000_000L;

    public PreflightResult Evaluate(
        RemediationPlan plan,
        IRemediationPreflightProbe probe,
        bool remediationAlreadyRunning)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(probe);

        var checks = new List<PreflightCheck>();
        var blockingReasons = new List<string>();

        var isElevated = SafeProbe(() => probe.IsElevated(), false);
        checks.Add(new PreflightCheck(
            "Elevación administrativa",
            isElevated,
            Blocking: true,
            isElevated
                ? "WSG se ejecuta con privilegios de administrador."
                : "WSG no se está ejecutando como administrador. La UI no se auto-eleva: " +
                  "reinicie WSG como administrador para permitir la remediación."));

        var hasPendingReboot = SafeProbe(() => probe.HasPendingReboot(), true);
        checks.Add(new PreflightCheck(
            "Reinicio de servicing pendiente",
            !hasPendingReboot,
            Blocking: true,
            hasPendingReboot
                ? "Windows tiene un reinicio de servicing pendiente (CBS/Windows Update). " +
                  "Complete el reinicio antes de reparar."
                : "No se detecta reinicio de servicing pendiente."));

        checks.Add(new PreflightCheck(
            "Remediación ya en curso",
            !remediationAlreadyRunning,
            Blocking: true,
            remediationAlreadyRunning
                ? "Ya existe una remediación en curso para esta acción."
                : "No hay ninguna remediación en curso."));

        var freeBytes = SafeProbe(() => probe.GetSystemDriveFreeSpaceBytes(), 0L);
        var enoughSpace = freeBytes >= MinimumSystemDriveFreeBytes;
        checks.Add(new PreflightCheck(
            "Espacio libre en la unidad del sistema",
            enoughSpace,
            Blocking: true,
            enoughSpace
                ? $"Espacio libre suficiente: {freeBytes / 1_000_000_000.0:0.#} GB."
                : $"Espacio libre insuficiente en la unidad del sistema " +
                  $"({freeBytes / 1_000_000_000.0:0.##} GB; se requieren al menos " +
                  $"{MinimumSystemDriveFreeBytes / 1_000_000_000.0:0.#} GB)."));

        if (plan.Action == RemediationActionId.ComponentStoreRepair)
        {
            // Advertencia informativa aprobada en la spec 0.5.0: no bloquea.
            checks.Add(new PreflightCheck(
                "Fuente de reparación",
                Passed: true,
                Blocking: false,
                "DISM /RestoreHealth puede requerir Windows Update o conectividad como fuente " +
                "de reparación; sin ella la reparación puede no completarse."));
        }

        foreach (var check in checks)
        {
            if (check.Blocking && !check.Passed)
                blockingReasons.Add($"{check.Name}: {check.Detail}");
        }

        return new PreflightResult(checks, Allowed: blockingReasons.Count == 0, blockingReasons);
    }

    private static T SafeProbe<T>(Func<T> probe, T failureValue)
    {
        try
        {
            return probe();
        }
        catch
        {
            // Un probe que falla no puede afirmar una condición segura: se usa el valor conservador.
            return failureValue;
        }
    }
}
