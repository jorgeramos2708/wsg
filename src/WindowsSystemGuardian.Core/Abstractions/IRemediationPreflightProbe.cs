using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Abstractions;

/// <summary>
/// Consultas de solo lectura del entorno que el preflight de remediación necesita.
/// Ninguna operación de esta interfaz eleva, reinicia ni modifica el sistema.
/// </summary>
public interface IRemediationPreflightProbe
{
    /// <summary>Indica si el proceso actual de WSG se ejecuta con privilegios de administrador.</summary>
    bool IsElevated();

    /// <summary>Indica si Windows tiene un reinicio de servicing pendiente (CBS/WU/PendingFileRename).</summary>
    bool HasPendingReboot();

    /// <summary>Espacio libre en bytes de la unidad que aloja el directorio de sistema.</summary>
    long GetSystemDriveFreeSpaceBytes();
}
