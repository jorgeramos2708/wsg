using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Fuente única de los comandos de remediación soportados en 0.6.0. La política usa
/// DisplayCommand para la aprobación del operador; PrivilegedHost usa la
/// especificación para ejecutar: ProcessCommand lanza el ejecutable fijo desde
/// System32, ServiceRestart reinicia los servicios Windows Update con la API de
/// servicios de .NET. No existe otra fuente de texto de comando ni otro mecanismo.
/// </summary>
public enum RemediationCommandKind
{
    ProcessCommand = 0,
    ServiceRestart
}

public static class RemediationCommandCatalog
{
    public sealed record CommandSpec(
        string ExecutableName,
        string Arguments,
        string DisplayCommand,
        TimeSpan Timeout,
        RemediationCommandKind Kind);

    public static CommandSpec For(RemediationActionId action) => action switch
    {
        RemediationActionId.ComponentStoreRepair => new(
            "dism.exe",
            "/Online /Cleanup-Image /RestoreHealth /NoRestart /English",
            "DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English",
            TimeSpan.FromMinutes(60),
            RemediationCommandKind.ProcessCommand),

        RemediationActionId.SystemFileRepair => new(
            "sfc.exe",
            "/scannow",
            "sfc.exe /scannow",
            TimeSpan.FromMinutes(45),
            RemediationCommandKind.ProcessCommand),

        RemediationActionId.WindowsUpdateServiceRestart => new(
            string.Empty,
            string.Empty,
            "Reinicio controlado de los servicios Windows Update (bits, wuauserv)",
            TimeSpan.FromMinutes(5),
            RemediationCommandKind.ServiceRestart),

        _ => throw new InvalidOperationException(
            $"La acción de remediación '{action}' no está soportada y no produce ningún comando.")
    };
}
