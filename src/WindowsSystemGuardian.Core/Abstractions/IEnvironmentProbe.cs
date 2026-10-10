using WindowsSystemGuardian.Contracts.Environment;

namespace WindowsSystemGuardian.Core.Abstractions;

public interface IEnvironmentProbe
{
    Task<EnvironmentFacts> ProbeAsync(CancellationToken cancellationToken = default);
}
