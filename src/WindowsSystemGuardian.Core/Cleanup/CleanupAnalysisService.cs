using System.Diagnostics;
using WindowsSystemGuardian.Contracts.Cleanup;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Safety;

namespace WindowsSystemGuardian.Core.Cleanup;

public sealed class CleanupAnalysisService(
    ICleanupRootProvider cleanupRootProvider,
    SafeCleanupFileScanner scanner,
    SafetyPolicyEngine safetyPolicyEngine)
{
    public async Task<CleanupAnalysisResult> AnalyzeAsync(int maximumFilesPerRoot = 50_000, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var candidates = new List<CleanupCandidate>();
            var scanned = 0;
            var skipped = 0;
            var locked = 0;
            var truncated = false;

            foreach (var root in cleanupRootProvider.GetRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!safetyPolicyEngine.IsCleanupRootAllowed(root, out _))
                {
                    skipped++;
                    continue;
                }

                var result = scanner.Scan(root, maximumFilesPerRoot, cancellationToken);
                candidates.AddRange(result.Candidates);
                scanned += result.FilesScanned;
                skipped += result.FilesSkipped;
                locked += result.LockedFiles;
                truncated |= result.Truncated;
            }

            sw.Stop();
            return new CleanupAnalysisResult(
                candidates,
                candidates.Sum(x => x.SizeBytes),
                scanned,
                skipped,
                locked,
                truncated,
                sw.Elapsed);
        }, cancellationToken);
    }
}
