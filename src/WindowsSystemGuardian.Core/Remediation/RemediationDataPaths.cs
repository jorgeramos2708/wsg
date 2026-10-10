namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Rutas de datos locales de remediación. Fuente única para el launcher (capa Windows),
/// el store de auditoría (Persistence) y PrivilegedHost. Todo vive bajo el perfil del
/// usuario: nunca en rutas temporales compartidas.
/// </summary>
public static class RemediationDataPaths
{
    public static string GetRemediationRoot() => Path.Combine(
        global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
        "WindowsSystemGuardian",
        "Data",
        "Remediation");

    public static string GetResultsDirectory() => Path.Combine(GetRemediationRoot(), "Results");

    public static string EnsureResultsDirectory()
    {
        var path = GetResultsDirectory();
        Directory.CreateDirectory(path);
        return path;
    }
}
