using WindowsSystemGuardian.Contracts.Cleanup;

namespace WindowsSystemGuardian.Core.Abstractions;

public interface ICleanupRootProvider
{
    IReadOnlyList<CleanupRootDefinition> GetRoots();
}
