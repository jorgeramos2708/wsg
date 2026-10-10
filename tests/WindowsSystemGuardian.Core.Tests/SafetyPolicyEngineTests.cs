using WindowsSystemGuardian.Contracts.Cleanup;
using WindowsSystemGuardian.Core.Safety;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class SafetyPolicyEngineTests
{
    private readonly SafetyPolicyEngine _sut = new();

    [Fact]
    public void UnknownCleanupCategory_IsFailClosed()
    {
        var root = new CleanupRootDefinition(
            "UNKNOWN_CACHE",
            Path.GetTempPath(),
            TimeSpan.FromDays(7),
            CleanupRisk.Safe,
            "test");

        var allowed = _sut.IsCleanupRootAllowed(root, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void WindowsTempRoot_UsesSystemEnvironmentAndIsAllowedWhenExact()
    {
        var windows = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows))
            return;

        var root = new CleanupRootDefinition(
            "WINDOWS_TEMP",
            Path.Combine(windows, "Temp"),
            TimeSpan.FromDays(7),
            CleanupRisk.Safe,
            "test");

        var allowed = _sut.IsCleanupRootAllowed(root, out _);

        Assert.True(allowed);
    }

    [Fact]
    public void ReviewCategory_IsNotAllowedInAnalysisFirstBuild()
    {
        var root = new CleanupRootDefinition(
            "USER_TEMP",
            Path.GetTempPath(),
            TimeSpan.FromDays(7),
            CleanupRisk.Review,
            "test");

        var allowed = _sut.IsCleanupRootAllowed(root, out _);

        Assert.False(allowed);
    }
}
