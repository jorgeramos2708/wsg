using System.Security.Principal;

namespace WindowsSystemGuardian.Windows.Diagnostics;

/// <summary>
/// Consulta únicamente el token del proceso actual. No eleva ni modifica privilegios.
/// Los diagnósticos que Windows exige ejecutar con privilegios administrativos fallan
/// de forma explícita cuando la UI se ejecuta sin Agent/PrivilegedHost.
/// </summary>
internal static class WindowsPrivilegeContext
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
