using WindowsSystemGuardian.Contracts.Cleanup;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class CleanupCandidateViewModel(CleanupCandidate candidate)
{
    private const double MiB = 1024d * 1024d;
    public string Category => candidate.Category;
    public string Path => candidate.Path;
    public string Size => candidate.SizeBytes >= MiB ? $"{candidate.SizeBytes / MiB:F1} MB" : $"{candidate.SizeBytes / 1024d:F0} KB";
    public string Age => $"{Math.Max(0, (DateTime.UtcNow - candidate.LastWriteUtc).TotalDays):F0} días";
    public string Risk => candidate.Risk.ToString().ToUpperInvariant();
}
