using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class EvidenceCategoryCompatibilityTests
{
    [Fact]
    public void ExistingPersistedNumericValues_RemainStable()
    {
        Assert.Equal(0, (int)EvidenceCategory.Unknown);
        Assert.Equal(1, (int)EvidenceCategory.Environment);
        Assert.Equal(2, (int)EvidenceCategory.Storage);
        Assert.Equal(3, (int)EvidenceCategory.Performance);
        Assert.Equal(4, (int)EvidenceCategory.Reliability);
        Assert.Equal(5, (int)EvidenceCategory.Services);
        Assert.Equal(6, (int)EvidenceCategory.Network);
        Assert.Equal(7, (int)EvidenceCategory.Integrity);
        Assert.Equal(8, (int)EvidenceCategory.Servicing);
        Assert.Equal(9, (int)EvidenceCategory.Security);
        Assert.Equal(10, (int)EvidenceCategory.Observability);
    }

    [Fact]
    public void NewCategories_AreAppendedAfterExistingValues()
    {
        Assert.Equal(11, (int)EvidenceCategory.Recovery);
        Assert.Equal(12, (int)EvidenceCategory.Backup);
    }
}
