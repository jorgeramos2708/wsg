using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Persistence;

public static class ObservabilityDataPathResolver
{
    private const string AgentMarker = ".agent-owner";

    public static IObservabilityStore CreateInteractiveStore()
    {
        var agentRoot = LocalObservabilityStore.GetDefaultAgentRoot();

        try
        {
            if (HasAgentOwnershipMarker(agentRoot))
                return new ReadOnlyObservabilityStore(new LocalObservabilityStore(agentRoot));
        }
        catch
        {
            // Si el store compartido no puede abrirse de forma segura, la UI no lo fuerza.
        }

        return new LocalObservabilityStore(LocalObservabilityStore.GetDefaultUserRoot());
    }

    public static void MarkAgentOwnership(string rootPath)
    {
        var full = System.IO.Path.GetFullPath(rootPath);
        System.IO.Directory.CreateDirectory(full);

        var attributes = System.IO.File.GetAttributes(full);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("El store del Agent no puede residir sobre un reparse point.");

        var marker = System.IO.Path.Combine(full, AgentMarker);
        if (System.IO.File.Exists(marker))
        {
            var markerAttributes = System.IO.File.GetAttributes(marker);
            if ((markerAttributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("El marcador del Agent no puede ser un reparse point.");
        }

        System.IO.File.WriteAllText(
            marker,
            $"Windows System Guardian Agent store{System.Environment.NewLine}schema=1{System.Environment.NewLine}");
    }

    public static bool IsSharedAgentRoot(string path) =>
        string.Equals(
            System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(LocalObservabilityStore.GetDefaultAgentRoot())
                .TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool HasAgentOwnershipMarker(string rootPath)
    {
        if (!System.IO.Directory.Exists(rootPath))
            return false;

        var attributes = System.IO.File.GetAttributes(rootPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        var marker = System.IO.Path.Combine(rootPath, AgentMarker);
        if (!System.IO.File.Exists(marker))
            return false;

        var markerAttributes = System.IO.File.GetAttributes(marker);
        return (markerAttributes & FileAttributes.ReparsePoint) == 0;
    }
}
