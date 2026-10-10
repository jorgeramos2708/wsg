using WindowsSystemGuardian.Contracts.Cleanup;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Cleanup;

public sealed class WindowsCleanupRootProvider : ICleanupRootProvider
{
    public IReadOnlyList<CleanupRootDefinition> GetRoots()
    {
        var roots = new List<CleanupRootDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRoot(roots, seen,
            "USER_TEMP",
            Path.GetTempPath(),
            "Temporales del usuario con más de 7 días.");

        var windows = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            AddRoot(roots, seen,
                "WINDOWS_TEMP",
                Path.Combine(windows, "Temp"),
                "Temporales de Windows con más de 7 días; archivos en uso se omiten.");
        }

        return roots;
    }

    private static void AddRoot(List<CleanupRootDefinition> roots, HashSet<string> seen, string category, string path, string description)
    {
        try
        {
            var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!seen.Add(canonical))
                return;

            roots.Add(new CleanupRootDefinition(
                category,
                canonical,
                TimeSpan.FromDays(7),
                CleanupRisk.Safe,
                description));
        }
        catch
        {
            // Ruta inválida: no se añade a allowlist.
        }
    }
}
