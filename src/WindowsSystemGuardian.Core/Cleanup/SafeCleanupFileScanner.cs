using WindowsSystemGuardian.Contracts.Cleanup;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Core.Cleanup;

public sealed class SafeCleanupFileScanner(IFileLockProbe fileLockProbe)
{
    public sealed record ScanOutput(
        IReadOnlyList<CleanupCandidate> Candidates,
        int FilesScanned,
        int FilesSkipped,
        int LockedFiles,
        bool Truncated);

    public ScanOutput Scan(
        CleanupRootDefinition root,
        int maximumFiles,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CleanupCandidate>();
        var filesScanned = 0;
        var filesSkipped = 0;
        var lockedFiles = 0;
        var truncated = false;

        maximumFiles = Math.Clamp(maximumFiles, 1, 1_000_000);
        var maximumDirectories = Math.Max(1_000, maximumFiles);
        var directoriesVisited = 0;

        if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
            return new ScanOutput(candidates, 0, 0, 0, false);

        string canonicalRoot;
        try
        {
            canonicalRoot = EnsureTrailingSeparator(Path.GetFullPath(root.Path));
        }
        catch
        {
            return new ScanOutput(candidates, 0, 1, 0, false);
        }

        var pending = new Stack<string>();
        pending.Push(canonicalRoot);
        var cutoff = DateTime.UtcNow - root.MinimumAge;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            directoriesVisited++;
            if (directoriesVisited > maximumDirectories)
            {
                truncated = true;
                break;
            }

            var directory = pending.Pop();

            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    filesSkipped++;
                    continue;
                }
            }
            catch
            {
                filesSkipped++;
                continue;
            }

            foreach (var subdirectory in EnumerateSafely(() => Directory.EnumerateDirectories(directory)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var full = EnsureTrailingSeparator(Path.GetFullPath(subdirectory));
                    if (!full.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        filesSkipped++;
                        continue;
                    }

                    var attributes = File.GetAttributes(subdirectory);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        filesSkipped++;
                        continue;
                    }

                    pending.Push(full);
                }
                catch
                {
                    filesSkipped++;
                }
            }

            foreach (var file in EnumerateSafely(() => Directory.EnumerateFiles(directory)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                filesScanned++;
                if (filesScanned > maximumFiles)
                {
                    truncated = true;
                    return new ScanOutput(
                        candidates,
                        filesScanned,
                        filesSkipped,
                        lockedFiles,
                        truncated);
                }

                try
                {
                    var full = Path.GetFullPath(file);
                    if (!full.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        filesSkipped++;
                        continue;
                    }

                    var info = new FileInfo(full);
                    if ((info.Attributes &
                         (FileAttributes.ReparsePoint | FileAttributes.System)) != 0)
                    {
                        filesSkipped++;
                        continue;
                    }

                    if (info.LastWriteTimeUtc > cutoff)
                        continue;

                    if (fileLockProbe.IsLocked(full))
                    {
                        lockedFiles++;
                        continue;
                    }

                    candidates.Add(new CleanupCandidate(
                        root.Category,
                        full,
                        info.Length,
                        info.LastWriteTimeUtc,
                        root.Risk,
                        $"Archivo temporal con antigüedad mínima de {root.MinimumAge.TotalDays:0} días."));
                }
                catch
                {
                    filesSkipped++;
                }
            }
        }

        return new ScanOutput(
            candidates,
            filesScanned,
            filesSkipped,
            lockedFiles,
            truncated);
    }

    private static IEnumerable<string> EnumerateSafely(
        Func<IEnumerable<string>> sourceFactory)
    {
        IEnumerator<string>? enumerator = null;

        try
        {
            enumerator = sourceFactory().GetEnumerator();
        }
        catch
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                string? current;
                bool moved;

                try
                {
                    moved = enumerator.MoveNext();
                    current = moved ? enumerator.Current : null;
                }
                catch
                {
                    yield break;
                }

                if (!moved || current is null)
                    yield break;

                yield return current;
            }
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
