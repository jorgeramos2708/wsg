using System.Text.Json;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Persistence;

public sealed class LocalObservabilityStore : IObservabilityStore
{
    private static readonly string[] ManagedFolders = ["evidence", "metrics", "findings", "incidents"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly int _retentionDays;
    private readonly long _maximumBytes;
    private readonly RecentIdCache _seenEvidence = new(50_000, TimeSpan.FromDays(2));
    private readonly RecentIdCache _seenFindings = new(25_000, TimeSpan.FromDays(2));
    private readonly RecentIdCache _seenIncidents = new(25_000, TimeSpan.FromDays(2));
    private bool _seenLoaded;

    public LocalObservabilityStore(
        string rootPath,
        int retentionDays = 30,
        long maximumBytes = 100L * 1024L * 1024L)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Se requiere una ruta de persistencia.", nameof(rootPath));

        RootPath = System.IO.Path.GetFullPath(rootPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        _retentionDays = Math.Clamp(retentionDays, 1, 365);
        _maximumBytes = Math.Max(10L * 1024L * 1024L, maximumBytes);

        EnsureSafeStoreLayout();
    }

    public string RootPath { get; }

    public static string GetDefaultUserRoot() => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "WindowsSystemGuardian",
        "Data",
        "Observability");

    public static string GetDefaultAgentRoot() => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
        "WindowsSystemGuardian",
        "Data",
        "Observability");

    public async Task AppendCollectorResultAsync(
        CollectorResult result,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSafeStoreLayout();
            await EnsureSeenIdsLoadedUnsafeAsync(cancellationToken);
            var date = result.CapturedAtUtc.Date;

            var uniqueEvidence = result.Evidence
                .Where(e => _seenEvidence.TryAdd(e.Id, e.TimestampUtc))
                .ToList();

            if (uniqueEvidence.Count > 0)
                await AppendLinesUnsafeAsync(FileFor("evidence", date), uniqueEvidence, cancellationToken);

            if (result.Metrics.Count > 0)
                await AppendLinesUnsafeAsync(FileFor("metrics", date), result.Metrics, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendFindingsAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default)
    {
        if (findings.Count == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSafeStoreLayout();
            await EnsureSeenIdsLoadedUnsafeAsync(cancellationToken);

            var groups = findings
                .Where(f => _seenFindings.TryAdd(f.Id, f.DetectedAtUtc))
                .GroupBy(f => f.DetectedAtUtc.Date);

            foreach (var group in groups)
                await AppendLinesUnsafeAsync(FileFor("findings", group.Key), group.ToList(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendIncidentsAsync(
        IReadOnlyList<Incident> incidents,
        CancellationToken cancellationToken = default)
    {
        if (incidents.Count == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSafeStoreLayout();
            await EnsureSeenIdsLoadedUnsafeAsync(cancellationToken);

            var groups = incidents
                .Where(i => _seenIncidents.TryAdd(i.Id, IncidentTimestamp(i)))
                .GroupBy(i => IncidentTimestamp(i).Date);

            foreach (var group in groups)
                await AppendLinesUnsafeAsync(FileFor("incidents", group.Key), group.ToList(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<EvidenceRecord>> LoadEvidenceSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        LoadSinceAsync<EvidenceRecord>("evidence", sinceUtc, e => e.TimestampUtc, cancellationToken);

    public Task<IReadOnlyList<MetricSample>> LoadMetricsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        LoadSinceAsync<MetricSample>("metrics", sinceUtc, m => m.TimestampUtc, cancellationToken);

    public Task<IReadOnlyList<Finding>> LoadFindingsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        LoadSinceAsync<Finding>("findings", sinceUtc, f => f.DetectedAtUtc, cancellationToken);

    public Task<IReadOnlyList<Incident>> LoadIncidentsSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        LoadSinceAsync<Incident>("incidents", sinceUtc, IncidentTimestamp, cancellationToken);

    public async Task EnforceRetentionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSafeStoreLayout();
            var cutoff = DateTime.UtcNow.Date.AddDays(-_retentionDays);
            var files = EnumerateManagedFilesUnsafe()
                .OrderBy(info => info.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Where(f => f.LastWriteTimeUtc < cutoff))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDeleteManagedFileUnsafe(file.FullName);
            }

            files = EnumerateManagedFilesUnsafe()
                .OrderBy(info => info.LastWriteTimeUtc)
                .ToList();

            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (total <= _maximumBytes)
                    break;

                cancellationToken.ThrowIfCancellationRequested();
                var length = file.Length;
                if (TryDeleteManagedFileUnsafe(file.FullName))
                    total -= length;
            }

            _seenEvidence.Prune(DateTime.UtcNow);
            _seenFindings.Prune(DateTime.UtcNow);
            _seenIncidents.Prune(DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<T>> LoadSinceAsync<T>(
        string folder,
        DateTime sinceUtc,
        Func<T, DateTime> timestampSelector,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSafeStoreLayout();
            return await LoadSinceUnsafeAsync(folder, sinceUtc, timestampSelector, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureSeenIdsLoadedUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_seenLoaded)
            return;

        var since = DateTime.UtcNow.AddDays(-2);

        var evidence = await LoadSinceUnsafeAsync<EvidenceRecord>(
            "evidence", since, e => e.TimestampUtc, cancellationToken);
        foreach (var item in evidence)
            _seenEvidence.TryAdd(item.Id, item.TimestampUtc);

        var findings = await LoadSinceUnsafeAsync<Finding>(
            "findings", since, f => f.DetectedAtUtc, cancellationToken);
        foreach (var item in findings)
            _seenFindings.TryAdd(item.Id, item.DetectedAtUtc);

        var incidents = await LoadSinceUnsafeAsync<Incident>(
            "incidents", since, IncidentTimestamp, cancellationToken);
        foreach (var item in incidents)
            _seenIncidents.TryAdd(item.Id, IncidentTimestamp(item));

        _seenLoaded = true;
    }

    private static DateTime IncidentTimestamp(Incident incident)
    {
        var latest = incident.LastSeenUtc;
        if (incident.StateChangedAtUtc is { } stateChanged && stateChanged > latest)
            latest = stateChanged;
        if (incident.ResolvedAtUtc is { } resolved && resolved > latest)
            latest = resolved;
        return latest;
    }

    private async Task<IReadOnlyList<T>> LoadSinceUnsafeAsync<T>(
        string folder,
        DateTime sinceUtc,
        Func<T, DateTime> timestampSelector,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        var path = GetValidatedFolder(folder);

        foreach (var file in System.IO.Directory
                     .EnumerateFiles(path, "*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsManagedDataFileUnsafe(file))
                continue;

            if (TryDateFromFile(file, out var fileDate) &&
                fileDate < sinceUtc.Date.AddDays(-1))
                continue;

            await foreach (var item in ReadLinesAsync<T>(file, cancellationToken))
            {
                if (timestampSelector(item) >= sinceUtc)
                    result.Add(item);
            }
        }

        return result.OrderBy(timestampSelector).ToList();
    }

    private async Task AppendLinesUnsafeAsync<T>(
        string path,
        IReadOnlyList<T> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        ValidateManagedTargetUnsafe(path);

        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(item, _jsonOptions).AsMemory(),
                cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private async IAsyncEnumerable<T> ReadLinesAsync<T>(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!IsManagedDataFileUnsafe(path))
            yield break;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            T? item;
            try
            {
                item = JsonSerializer.Deserialize<T>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                // Una línea parcial/corrupta no invalida el resto del histórico.
                continue;
            }

            if (item is not null)
                yield return item;
        }
    }

    private void EnsureSafeStoreLayout()
    {
        System.IO.Directory.CreateDirectory(RootPath);
        ThrowIfReparsePoint(RootPath, "La raíz de observabilidad no puede ser un reparse point.");

        foreach (var folder in ManagedFolders)
        {
            var path = GetFolder(folder);
            System.IO.Directory.CreateDirectory(path);
            ThrowIfReparsePoint(path, $"El directorio administrado '{folder}' no puede ser un reparse point.");
        }
    }

    private IEnumerable<FileInfo> EnumerateManagedFilesUnsafe()
    {
        foreach (var folder in ManagedFolders)
        {
            var path = GetValidatedFolder(folder);

            foreach (var file in System.IO.Directory.EnumerateFiles(path, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (!IsManagedDataFileUnsafe(file))
                    continue;

                FileInfo? info = null;
                try
                {
                    info = new FileInfo(file);
                    _ = info.Length;
                }
                catch
                {
                    // Archivo transitorio/inaccesible: la retención no lo fuerza.
                }

                if (info is not null)
                    yield return info;
            }
        }
    }

    private string GetValidatedFolder(string folder)
    {
        if (!ManagedFolders.Contains(folder, StringComparer.Ordinal))
            throw new InvalidOperationException("Directorio de observabilidad no autorizado.");

        var path = GetFolder(folder);
        System.IO.Directory.CreateDirectory(path);
        ThrowIfReparsePoint(path, $"El directorio administrado '{folder}' no puede ser un reparse point.");
        return path;
    }

    private void ValidateManagedTargetUnsafe(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!IsManagedPathNameUnsafe(full))
            throw new InvalidOperationException("La persistencia intentó escribir fuera de un archivo administrado.");

        var parent = System.IO.Path.GetDirectoryName(full)!;
        ThrowIfReparsePoint(parent, "El directorio de persistencia no puede ser un reparse point.");

        if (System.IO.File.Exists(full))
            ThrowIfReparsePoint(full, "Un archivo JSONL administrado no puede ser un reparse point.");
    }

    private bool IsManagedDataFileUnsafe(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(full) || !IsManagedPathNameUnsafe(full))
                return false;

            var parent = System.IO.Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(parent))
                return false;

            var parentAttributes = System.IO.File.GetAttributes(parent);
            if ((parentAttributes & FileAttributes.ReparsePoint) != 0)
                return false;

            var attributes = System.IO.File.GetAttributes(full);
            return (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool IsManagedPathNameUnsafe(string fullPath)
    {
        if (!string.Equals(System.IO.Path.GetExtension(fullPath), ".jsonl", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryDateFromFile(fullPath, out _))
            return false;

        var parent = System.IO.Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent))
            return false;

        return ManagedFolders
            .Select(GetFolder)
            .Any(folder => string.Equals(
                System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                System.IO.Path.GetFullPath(parent).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
    }

    private bool TryDeleteManagedFileUnsafe(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!IsManagedDataFileUnsafe(full))
                return false;

            System.IO.File.Delete(full);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ThrowIfReparsePoint(string path, string message)
    {
        var attributes = System.IO.File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(message);
    }

    private string GetFolder(string name) => System.IO.Path.Combine(RootPath, name);

    private string FileFor(string folder, DateTime date) =>
        System.IO.Path.Combine(GetValidatedFolder(folder), $"{date:yyyy-MM-dd}.jsonl");

    private static bool TryDateFromFile(string path, out DateTime date) =>
        DateTime.TryParseExact(
            System.IO.Path.GetFileNameWithoutExtension(path),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out date);

    private sealed class RecentIdCache(int maximumCount, TimeSpan retention)
    {
        private readonly Dictionary<string, DateTime> _items = new(StringComparer.OrdinalIgnoreCase);

        public bool TryAdd(string id, DateTime timestampUtc)
        {
            if (_items.ContainsKey(id))
                return false;

            _items[id] = timestampUtc;
            if (_items.Count > maximumCount)
                Prune(DateTime.UtcNow);

            if (_items.Count > maximumCount)
            {
                foreach (var key in _items
                             .OrderBy(kvp => kvp.Value)
                             .Take(_items.Count - maximumCount)
                             .Select(kvp => kvp.Key)
                             .ToList())
                {
                    _items.Remove(key);
                }
            }

            return true;
        }

        public void Prune(DateTime nowUtc)
        {
            var cutoff = nowUtc - retention;
            foreach (var key in _items
                         .Where(kvp => kvp.Value < cutoff)
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                _items.Remove(key);
            }
        }
    }
}
