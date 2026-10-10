using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Persistence;

/// <summary>
/// Log de auditoría append-only de remediaciones en JSONL diario, bajo el perfil del
/// usuario. Misma disciplina de retención que el store de observabilidad: carpeta
/// gestionada única, enumeración TopDirectoryOnly, sin seguir reparse points, sin
/// edición ni borrado individual de registros.
/// </summary>
public sealed class RemediationRecordStore : IRemediationRecordStore
{
    private const string FilePrefix = "remediation-";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _rootPath;
    private readonly int _retentionDays;

    public RemediationRecordStore(string? rootPath = null, int retentionDays = 90)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            rootPath = RemediationDataPaths.GetRemediationRoot();

        RootPath = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _retentionDays = Math.Clamp(retentionDays, 7, 365);
    }

    public string RootPath { get; }

    public void Append(RemediationRecord record)
    {
        _gate.Wait();
        try
        {
            Directory.CreateDirectory(RootPath);
            ApplyRetentionUnsafe(DateTime.UtcNow);
            var line = JsonSerializer.Serialize(record, JsonOptions);
            File.AppendAllText(FileFor(record.RecordedAtUtc.Date), line + Environment.NewLine);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Registros más recientes, para la vista de Historial. Solo lectura.</summary>
    public IReadOnlyList<RemediationRecord> ReadRecent(int maximum)
    {
        _gate.Wait();
        try
        {
            if (!Directory.Exists(RootPath))
                return Array.Empty<RemediationRecord>();

            return Directory.EnumerateFiles(RootPath, $"{FilePrefix}*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .SelectMany(ReadLinesSafe)
                .OrderByDescending(r => r.RecordedAtUtc)
                .Take(Math.Clamp(maximum, 1, 1000))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private IEnumerable<RemediationRecord> ReadLinesSafe(string path)
    {
        foreach (var line in ReadLines(path))
        {
            RemediationRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<RemediationRecord>(line, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (record is not null)
                yield return record;
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private string FileFor(DateTime date) =>
        Path.Combine(RootPath, $"{FilePrefix}{date:yyyyMMdd}.jsonl");

    private void ApplyRetentionUnsafe(DateTime nowUtc)
    {
        var rootAttributes = File.GetAttributes(RootPath);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            return;

        foreach (var file in Directory.EnumerateFiles(RootPath, $"{FilePrefix}*.jsonl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (File.GetLastWriteTimeUtc(file) < nowUtc.AddDays(-_retentionDays))
                    File.Delete(file);
            }
            catch
            {
                // Un archivo en uso o no borrable se conserva; la retención lo reintentará.
            }
        }
    }
}
