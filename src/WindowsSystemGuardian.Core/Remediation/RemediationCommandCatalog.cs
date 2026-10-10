using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Fuente única de los comandos de remediación soportados en 0.5.0. La política usa
/// DisplayCommand para la aprobación del operador y PrivilegedHost usa ExecutableName y
/// Arguments para construir el proceso: no existe otra fuente de texto de comando.
/// </summary>
public static class RemediationCommandCatalog
{
    public sealed record CommandSpec(
        string ExecutableName,
        string Arguments,
        string DisplayCommand,
        TimeSpan Timeout);

    public static CommandSpec For(RemediationActionId action) => action switch
    {
        RemediationActionId.ComponentStoreRepair => new(
            "dism.exe",
            "/Online /Cleanup-Image /RestoreHealth /NoRestart /English",
            "DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English",
            TimeSpan.FromMinutes(60)),

        RemediationActionId.SystemFileRepair => new(
            "sfc.exe",
            "/scannow",
            "sfc.exe /scannow",
            TimeSpan.FromMinutes(45)),

        _ => throw new InvalidOperationException(
            $"La acción de remediación '{action}' no está soportada y no produce ningún comando.")
    };
}
