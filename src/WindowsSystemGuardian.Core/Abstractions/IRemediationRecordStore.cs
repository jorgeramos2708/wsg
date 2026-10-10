using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Abstractions;

/// <summary>
/// Log de auditoría append-only de remediaciones. Cada fase del flujo escribe su
/// registro; no existe edición ni borrado individual.
/// </summary>
public interface IRemediationRecordStore
{
    void Append(RemediationRecord record);
}
