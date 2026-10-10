using System.Diagnostics.Eventing.Reader;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsEventLogCollector : IObservabilityCollector
{
    private static readonly TimeSpan InitialLookback = TimeSpan.FromMinutes(30);
    private const int MaximumRecords = 2_000;

    private long? _lastRecordId;

    public string Name => "Eventos de Windows";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(1);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var evidence = new List<EvidenceRecord>();
        var inspected = 0;
        var matched = 0;
        var correctedWhea = new List<EvidenceRecord>();
        var previousRecordId = _lastRecordId;
        var newestSystemRecordId = TryGetNewestSystemRecordId();
        var hitMaximum = false;

        // EventRecordId vuelve a valores pequeños cuando el canal es limpiado o recreado.
        if (previousRecordId.HasValue &&
            newestSystemRecordId.HasValue &&
            newestSystemRecordId.Value < previousRecordId.Value)
        {
            previousRecordId = null;
            _lastRecordId = null;
        }

        var queryText = previousRecordId.HasValue
            ? $"*[System[(Level=1 or Level=2 or Level=3) and EventRecordID > {previousRecordId.Value}]]"
            : $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {(long)InitialLookback.TotalMilliseconds}]]]";

        var query = new EventLogQuery(
            "System",
            PathType.LogName,
            queryText)
        {
            // Procesar de antiguo a nuevo permite avanzar un backlog por lotes sin
            // saltar evidencia cuando aparecen más de MaximumRecords entre lecturas.
            ReverseDirection = false,
            TolerateQueryErrors = true
        };

        using var reader = new EventLogReader(query);
        long? lastProcessedRecordId = previousRecordId;

        for (EventRecord? record = reader.ReadEvent();
             record is not null;
             record = reader.ReadEvent())
        {
            using (record)
            {
                cancellationToken.ThrowIfCancellationRequested();

                inspected++;
                if (inspected > MaximumRecords)
                {
                    hitMaximum = true;
                    break;
                }

                var recordId = record.RecordId;
                if (recordId.HasValue)
                    lastProcessedRecordId = recordId.Value;

                if (!TryClassify(record, out var classification))
                    continue;

                matched++;
                var createdUtc = record.TimeCreated?.ToUniversalTime() ?? now;
                var naturalId = recordId?.ToString() ??
                                $"{createdUtc:yyyyMMddHHmmssfffffff}:{record.Id}";
                var message = TryFormatMessage(record);
                var item = new EvidenceRecord(
                    $"event:System:{naturalId}",
                    createdUtc,
                    Name,
                    EvidenceCategory.Reliability,
                    classification.Entity,
                    $"Evento {record.Id}",
                    record.Id.ToString(),
                    "EventId",
                    classification.Severity,
                    DetectionConfidence.High,
                    classification.CorrelationKey,
                    string.IsNullOrWhiteSpace(message)
                        ? classification.DefaultMessage
                        : $"{classification.DefaultMessage} Detalle: {message}");

                evidence.Add(item);

                if (classification.IsCorrectedWhea)
                    correctedWhea.Add(item);
            }
        }

        if (lastProcessedRecordId.HasValue)
            _lastRecordId = lastProcessedRecordId;

        if (correctedWhea.Count >= 3)
        {
            var latest = correctedWhea.Max(e => e.TimestampUtc);
            evidence.Add(new EvidenceRecord(
                $"event:whea-corrected-burst:{latest:yyyyMMddHHmm}",
                latest,
                Name,
                EvidenceCategory.Reliability,
                "Hardware",
                "WHEA corregidos",
                correctedWhea.Count.ToString(),
                "count",
                EvidenceSeverity.Warning,
                DetectionConfidence.High,
                "reliability:whea-corrected-burst",
                $"WHEA registró {correctedWhea.Count} errores de hardware corregidos en la ventana reciente. Una repetición de este patrón merece revisión."));
        }

        if (hitMaximum)
        {
            evidence.Add(new EvidenceRecord(
                $"observability:eventlog-backlog:{now:yyyyMMddHHmm}",
                now,
                Name,
                EvidenceCategory.Observability,
                "System Event Log",
                "Backlog pendiente",
                MaximumRecords.ToString(),
                "records",
                EvidenceSeverity.Warning,
                DetectionConfidence.High,
                "observability:eventlog-backlog",
                $"El colector procesó el lote máximo de {MaximumRecords:N0} eventos. Existe un backlog pendiente que continuará en la siguiente ejecución; WSG no lo considera cobertura completa todavía."));
        }

        var metrics = new List<MetricSample>
        {
            new($"metric:events.inspected:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Reliability, "System Event Log",
                "events.inspected", Math.Min(inspected, MaximumRecords), "count"),
            new($"metric:events.relevant:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Reliability, "System Event Log",
                "events.relevant", matched, "count"),
            new($"metric:events.backlog:{now:yyyyMMddHHmmssfff}",
                now, EvidenceCategory.Observability, "System Event Log",
                "events.backlog", hitMaximum ? 1 : 0, "bool")
        };

        return CollectorResult.Success(Name, Cost, now, evidence, metrics);
    }

    private static long? TryGetNewestSystemRecordId()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, "*")
            {
                ReverseDirection = true,
                TolerateQueryErrors = true
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            return record?.RecordId;
        }
        catch
        {
            // No se usa para clasificar salud; solo para detectar recreación del canal.
            return null;
        }
    }

    private static bool TryClassify(
        EventRecord record,
        out EventClassification classification)
    {
        var provider = record.ProviderName ?? string.Empty;
        var p = provider.ToUpperInvariant();
        var id = record.Id;

        if (p.Contains("KERNEL-POWER") && id == 41)
        {
            classification = new(
                EvidenceSeverity.Critical,
                "Reinicio inesperado",
                "reliability:unexpected-shutdown",
                "Windows registró un reinicio sin apagado limpio (Kernel-Power 41).");
            return true;
        }

        if (p.Contains("EVENTLOG") && id == 6008)
        {
            classification = new(
                EvidenceSeverity.Warning,
                "Apagado inesperado",
                "reliability:unexpected-shutdown",
                "Windows registró un apagado inesperado.");
            return true;
        }

        if (p.Contains("NTFS") && id is 55 or 98)
        {
            classification = new(
                EvidenceSeverity.Critical,
                "Sistema de archivos",
                "reliability:storage-filesystem",
                "NTFS registró una inconsistencia que requiere diagnóstico del volumen.");
            return true;
        }

        if (p.EndsWith("DISK", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("WINDOWS-DISK"))
        {
            if (id == 7)
            {
                classification = new(
                    EvidenceSeverity.Critical,
                    "Almacenamiento",
                    "reliability:storage-io",
                    "Windows registró un bloque defectuoso o error grave de disco.");
                return true;
            }

            if (id is 51 or 153)
            {
                classification = new(
                    EvidenceSeverity.Warning,
                    "Almacenamiento",
                    "reliability:storage-io",
                    "Windows registró un error/reintento de E/S de almacenamiento.");
                return true;
            }
        }

        if (p.Contains("WHEA-LOGGER"))
        {
            if (id is 18 or 20 or 46)
            {
                classification = new(
                    EvidenceSeverity.Critical,
                    "Hardware",
                    "reliability:whea",
                    "WHEA registró un error de hardware no corregido o de alta severidad.");
                return true;
            }

            if (id is 17 or 19)
            {
                classification = new(
                    EvidenceSeverity.Informational,
                    "Hardware",
                    "reliability:whea-corrected",
                    "WHEA registró un error de hardware corregido.",
                    true);
                return true;
            }
        }

        if (p.Contains("SERVICE CONTROL MANAGER") &&
            id is 7000 or 7001 or 7009 or 7011 or 7023 or 7031 or 7034)
        {
            var service = TryGetServiceName(record);
            var entity = string.IsNullOrWhiteSpace(service)
                ? "Servicios Windows"
                : service;
            var key = string.IsNullOrWhiteSpace(service)
                ? "reliability:service-failure"
                : $"reliability:service-failure:{Normalize(service)}";

            classification = new(
                EvidenceSeverity.Warning,
                entity,
                key,
                "Service Control Manager registró un fallo o timeout de servicio.");
            return true;
        }

        if (p.Contains("WER-SYSTEMERRORREPORTING") && id == 1001)
        {
            classification = new(
                EvidenceSeverity.Critical,
                "BugCheck",
                "reliability:bugcheck",
                "Windows registró un BugCheck/BSOD.");
            return true;
        }

        classification = default!;
        return false;
    }

    private static string TryGetServiceName(EventRecord record)
    {
        try
        {
            var value = record.Properties
                .Select(p => p.Value?.ToString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return value is { Length: <= 160 } ? value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryFormatMessage(EventRecord record)
    {
        try
        {
            var message = record.FormatDescription()?
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim() ?? string.Empty;

            return message.Length <= 500
                ? message
                : message[..500] + "…";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0
            ? "service"
            : new string(chars).ToLowerInvariant();
    }

    private sealed record EventClassification(
        EvidenceSeverity Severity,
        string Entity,
        string CorrelationKey,
        string DefaultMessage,
        bool IsCorrectedWhea = false);
}
