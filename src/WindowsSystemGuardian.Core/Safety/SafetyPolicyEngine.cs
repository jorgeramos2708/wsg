using WindowsSystemGuardian.Contracts.Cleanup;

namespace WindowsSystemGuardian.Core.Safety;

public sealed class SafetyPolicyEngine
{
    private static readonly HashSet<string> AllowedCleanupCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "USER_TEMP",
            "WINDOWS_TEMP"
        };

    public bool IsCleanupRootAllowed(
        CleanupRootDefinition root,
        out string reason)
    {
        if (!AllowedCleanupCategories.Contains(root.Category))
        {
            reason =
                $"La categoría {root.Category} no pertenece a la allowlist de limpieza segura.";
            return false;
        }

        if (root.Risk != CleanupRisk.Safe)
        {
            reason = "Solo se analizan categorías clasificadas explícitamente como SAFE.";
            return false;
        }

        string path;
        try
        {
            path = Path.GetFullPath(root.Path)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            reason = "La ruta no pudo canonicalizarse.";
            return false;
        }

        if (IsFileSystemRoot(path))
        {
            reason = "Una raíz completa de volumen nunca puede ser un objetivo de limpieza.";
            return false;
        }

        if (root.Category.Equals("WINDOWS_TEMP", StringComparison.OrdinalIgnoreCase))
        {
            var windowsTemp = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),
                "Temp");

            if (!PathsEqual(path, windowsTemp))
            {
                reason = "WINDOWS_TEMP solo puede apuntar exactamente a %WINDIR%\\Temp.";
                return false;
            }

            reason = "WINDOWS_TEMP coincide exactamente con la allowlist.";
            return true;
        }

        foreach (var protectedPath in GetProtectedPaths())
        {
            if (IsSameOrChild(path, protectedPath))
            {
                reason = $"La ruta cae dentro de un área protegida: {protectedPath}";
                return false;
            }
        }

        if (root.Category.Equals("USER_TEMP", StringComparison.OrdinalIgnoreCase))
        {
            var userTemp = Path.GetTempPath();
            if (!PathsEqual(path, userTemp))
            {
                reason =
                    "USER_TEMP solo puede apuntar exactamente al directorio temporal resuelto por Windows para el usuario actual.";
                return false;
            }

            var windows = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows) &&
                IsSameOrChild(path, windows))
            {
                reason =
                    "USER_TEMP no puede reutilizar una ubicación interna de Windows. WINDOWS_TEMP tiene una política separada.";
                return false;
            }
        }

        reason = "Allowlist y protección validadas.";
        return true;
    }

    private static IEnumerable<string> GetProtectedPaths()
    {
        var windows = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.Windows);
        var programFiles = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ProgramFilesX86);
        var commonData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.CommonApplicationData);

        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "System32");
            yield return Path.Combine(windows, "SysWOW64");
            yield return Path.Combine(windows, "servicing");
            yield return Path.Combine(windows, "Installer");
            yield return Path.Combine(windows, "WinSxS");
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return programFiles;
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return programFilesX86;
        if (!string.IsNullOrWhiteSpace(commonData))
            yield return commonData;
    }

    private static bool IsFileSystemRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root) && PathsEqual(path, root);
    }

    private static bool IsSameOrChild(string path, string root)
    {
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar);
        if (PathsEqual(path, canonicalRoot))
            return true;

        return path.StartsWith(
            canonicalRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
