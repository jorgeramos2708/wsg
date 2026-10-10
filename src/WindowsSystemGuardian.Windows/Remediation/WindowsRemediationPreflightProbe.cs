using System.IO;
using Microsoft.Win32;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Windows.Diagnostics;

namespace WindowsSystemGuardian.Windows.Remediation;

/// <summary>
/// Sonda de solo lectura para el preflight de remediación sobre Windows real.
/// No eleva, no modifica registros ni archivos: solo consulta estado.
/// </summary>
public sealed class WindowsRemediationPreflightProbe : IRemediationPreflightProbe
{
    public bool IsElevated() => WindowsPrivilegeContext.IsAdministrator();

    public bool HasPendingReboot()
    {
        // Claves estándar de reinicio de servicing. Una lectura fallida se interpreta como
        // "sin reinicio pendiente": bloquear la remediación por un error de consulta sería
        // un falso positivo; la evidencia de WU/CBS llega además por los collectors de 0.4.1.
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (baseKey is null)
                return false;

            if (baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null)
                return true;

            if (baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null)
                return true;

            using var sessionManager = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            var pendingRenames = sessionManager?.GetValue("PendingFileRenameOperations") as string[];
            return pendingRenames is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public long GetSystemDriveFreeSpaceBytes()
    {
        var systemRoot = Path.GetPathRoot(System.Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(systemRoot))
            return 0;

        try
        {
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady &&
                    string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase));
            return drive?.AvailableFreeSpace ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
