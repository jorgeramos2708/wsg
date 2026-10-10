using System.Runtime.InteropServices;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Servicing;

/// <summary>
/// Usa el modelo COM oficial de Windows Update Agent para consultar historial y
/// si WUA requiere reinicio. No busca, descarga, instala ni desinstala updates.
/// </summary>
public sealed class WindowsUpdateHealthCollector : IObservabilityCollector
{
    private const int MaximumHistoryEntries = 100;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromDays(14);

    public string Name => "Salud de Windows Update";
    public DiagnosticCost Cost => DiagnosticCost.Medium;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(15);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        object? sessionObject = null;
        object? searcherObject = null;
        object? historyObject = null;
        object? systemInfoObject = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                              ?? throw new InvalidOperationException("Windows Update Agent no está disponible.");
            sessionObject = Activator.CreateInstance(sessionType)
                            ?? throw new InvalidOperationException("No se pudo crear Microsoft.Update.Session.");
            dynamic session = sessionObject;
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;

            var total = (int)searcher.GetTotalHistoryCount();
            var count = Math.Min(total, MaximumHistoryEntries);
            var entries = new List<UpdateHistorySnapshot>();
            if (count > 0)
            {
                historyObject = searcher.QueryHistory(0, count);
                dynamic history = historyObject;
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    dynamic entry = history.Item(i);
                    try
                    {
                        entries.Add(new UpdateHistorySnapshot(
                            ((DateTime)entry.Date).ToUniversalTime(),
                            (string?)entry.Title ?? "Actualización de Windows",
                            (int)entry.ResultCode,
                            (int)entry.HResult));
                    }
                    finally
                    {
                        ReleaseCom(entry);
                    }
                }
            }

            var systemInfoType = Type.GetTypeFromProgID("Microsoft.Update.SystemInfo")
                                 ?? throw new InvalidOperationException("Microsoft.Update.SystemInfo no está disponible.");
            systemInfoObject = Activator.CreateInstance(systemInfoType)
                               ?? throw new InvalidOperationException("No se pudo consultar el estado de reinicio de Windows Update.");
            dynamic systemInfo = systemInfoObject;
            var rebootRequired = (bool)systemInfo.RebootRequired;

            var cutoff = now - FailureWindow;
            var recent = entries.Where(e => e.DateUtc >= cutoff).ToList();
            var failed = recent.Where(e => e.ResultCode == 4).ToList();
            var aborted = recent.Where(e => e.ResultCode == 5).ToList();
            var partial = recent.Where(e => e.ResultCode == 3).ToList();

            var metrics = new List<MetricSample>
            {
                new($"metric:servicing.windows_update.failed:{now:yyyyMMddHHmmssfff}",
                    now, EvidenceCategory.Servicing, "Windows Update",
                    "servicing.windows_update.failed_14d", failed.Count, "count"),
                new($"metric:servicing.windows_update.partial:{now:yyyyMMddHHmmssfff}",
                    now, EvidenceCategory.Servicing, "Windows Update",
                    "servicing.windows_update.partial_14d", partial.Count, "count"),
                new($"metric:servicing.windows_update.aborted:{now:yyyyMMddHHmmssfff}",
                    now, EvidenceCategory.Servicing, "Windows Update",
                    "servicing.windows_update.aborted_14d", aborted.Count, "count"),
                new($"metric:servicing.windows_update.reboot_required:{now:yyyyMMddHHmmssfff}",
                    now, EvidenceCategory.Servicing, "Windows Update",
                    "servicing.windows_update.reboot_required", rebootRequired ? 1 : 0, "bool")
            };
            var evidence = new List<EvidenceRecord>();

            if (failed.Count > 0)
            {
                var newest = failed.OrderByDescending(e => e.DateUtc).First();
                var critical = failed.Count(e => e.DateUtc >= now.AddHours(-24)) >= 3;
                var severity = critical ? EvidenceSeverity.Critical : EvidenceSeverity.Warning;
                var hr = unchecked((uint)newest.HResult);
                evidence.Add(NewEvidence(
                    "servicing:windows-update-failure",
                    now,
                    severity,
                    "Historial de actualizaciones",
                    failed.Count.ToString(),
                    $"Windows Update registra {failed.Count} operación(es) fallida(s) en los últimos 14 días. Última: {newest.Title} · HRESULT 0x{hr:X8}."));
            }

            if (partial.Count > 0)
            {
                evidence.Add(NewEvidence(
                    "servicing:windows-update-partial",
                    now,
                    EvidenceSeverity.Warning,
                    "Actualizaciones con errores",
                    partial.Count.ToString(),
                    $"Windows Update registra {partial.Count} operación(es) completada(s) con errores en los últimos 14 días."));
            }

            if (aborted.Count > 0)
            {
                evidence.Add(NewEvidence(
                    "servicing:windows-update-aborted",
                    now,
                    EvidenceSeverity.Informational,
                    "Operaciones canceladas",
                    aborted.Count.ToString(),
                    $"Windows Update registra {aborted.Count} operación(es) cancelada(s) en los últimos 14 días. WSG las conserva como contexto y no las trata por sí solas como fallo del sistema."));
            }

            var oldestLoaded = entries.Count == 0 ? (DateTime?)null : entries.Min(e => e.DateUtc);
            if (total > count && count == MaximumHistoryEntries && oldestLoaded is not null && oldestLoaded >= cutoff)
            {
                evidence.Add(new EvidenceRecord(
                    $"evidence:observability:windows-update-history-truncated:{now:yyyyMMddHHmmssfffffff}",
                    now,
                    "Windows Update Agent",
                    EvidenceCategory.Observability,
                    "Windows Update",
                    "Cobertura de historial",
                    $"{count}/{total}",
                    string.Empty,
                    EvidenceSeverity.Warning,
                    DetectionConfidence.High,
                    "observability:windows-update-history-truncated",
                    $"El historial de Windows Update contiene más de {MaximumHistoryEntries} entradas dentro de la ventana analizada. Los conteos de los últimos 14 días pueden estar incompletos; WSG no oculta esta limitación."));
            }

            if (rebootRequired)
            {
                evidence.Add(NewEvidence(
                    "servicing:update-reboot-required",
                    now,
                    EvidenceSeverity.Warning,
                    "Reinicio requerido",
                    "1",
                    "Windows Update Agent indica que se requiere reiniciar para completar una instalación o desinstalación. WSG no reiniciará el equipo automáticamente."));
            }

            return CollectorResult.Success(Name, Cost, now, evidence, metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CollectorResult.Failure(Name, Cost, now, ex.GetBaseException().Message);
        }
        finally
        {
            ReleaseCom(historyObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
            ReleaseCom(systemInfoObject);
        }
    }

    private static EvidenceRecord NewEvidence(
        string key,
        DateTime now,
        EvidenceSeverity severity,
        string metric,
        string value,
        string message) =>
        new($"evidence:{key}:{severity}:{now:yyyyMMddHHmmssfffffff}",
            now,
            "Windows Update Agent",
            EvidenceCategory.Servicing,
            "Windows Update",
            metric,
            value,
            string.Empty,
            severity,
            DetectionConfidence.High,
            key,
            message);

    private static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;
        try { Marshal.FinalReleaseComObject(value); }
        catch { }
    }

    private sealed record UpdateHistorySnapshot(
        DateTime DateUtc,
        string Title,
        int ResultCode,
        int HResult);
}
