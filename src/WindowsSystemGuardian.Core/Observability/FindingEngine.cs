using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Observability;

public sealed class FindingEngine
{
    private const double GiB = 1024d * 1024d * 1024d;

    public IReadOnlyList<Finding> Evaluate(
        IReadOnlyList<EvidenceRecord> currentEvidence,
        IReadOnlyList<MetricSample> history,
        DateTime? nowUtc = null)
    {
        var evaluationTime = nowUtc ?? DateTime.UtcNow;
        var findings = new List<Finding>();
        findings.AddRange(FromCurrentEvidence(currentEvidence));
        findings.AddRange(EvaluateSustainedMemoryPressure(history, evaluationTime));
        findings.AddRange(EvaluateSustainedCommitPressure(history, evaluationTime));
        findings.AddRange(EvaluateSustainedCpuPressure(history, evaluationTime));
        findings.AddRange(EvaluateProcessCpuPressure(history, evaluationTime));
        findings.AddRange(EvaluateProcessMemoryPressure(history, evaluationTime));
        findings.AddRange(EvaluateProcessMemoryGrowth(history, evaluationTime));
        findings.AddRange(EvaluateProcessCounterGrowth(history, "process.handles", "handle", 5000, 2500, evaluationTime));
        findings.AddRange(EvaluateProcessCounterGrowth(history, "process.threads", "thread", 500, 250, evaluationTime));
        findings.AddRange(EvaluateDiskBusyPressure(history, evaluationTime));
        findings.AddRange(EvaluateNetworkSaturation(history, evaluationTime));
        findings.AddRange(EvaluateCapacityTrend(history, evaluationTime));

        return findings
            .GroupBy(f => f.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(MergeGroup)
            .OrderByDescending(f => SeverityRank(f.Severity))
            .ThenByDescending(f => f.DetectedAtUtc)
            .ToList();
    }

    private static IEnumerable<Finding> FromCurrentEvidence(IReadOnlyList<EvidenceRecord> evidence)
    {
        foreach (var item in evidence.Where(e =>
                     e.Severity is EvidenceSeverity.Warning or EvidenceSeverity.Critical))
        {
            var severity = item.Severity == EvidenceSeverity.Critical
                ? FindingSeverity.Critical
                : FindingSeverity.Warning;
            var fingerprint = $"evidence:{item.CorrelationKey}";

            yield return new Finding(
                FindingId(fingerprint, severity, item.TimestampUtc),
                fingerprint,
                item.TimestampUtc,
                severity,
                item.Category,
                TitleFor(item),
                item.Message,
                RecommendFor(item),
                item.Confidence,
                item.Entity,
                [item.Id],
                item.TimestampUtc,
                item.TimestampUtc,
                1,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateSustainedMemoryPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-10);
        var samples = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "system.memory.percent", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.TimestampUtc)
            .Take(5)
            .OrderBy(m => m.TimestampUtc)
            .ToList();

        if (samples.Count < 3)
            yield break;

        var recent = samples.TakeLast(3).ToList();
        if (!IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(2)))
            yield break;
        if (recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromSeconds(30))
            yield break;

        var minimum = recent.Min(s => s.Value);
        if (minimum < 90)
            yield break;

        var severity = minimum >= 95 ? FindingSeverity.Critical : FindingSeverity.Warning;
        var fingerprint = "performance:sustained-memory-pressure";
        var detectedAt = recent[^1].TimestampUtc;

        yield return new Finding(
            FindingId(fingerprint, severity, detectedAt),
            fingerprint,
            detectedAt,
            severity,
            EvidenceCategory.Performance,
            "Presión sostenida de memoria",
            $"La memoria se mantuvo por encima de {minimum:F0}% en las últimas muestras disponibles.",
            "Revise los procesos con mayor consumo, paginación y crecimiento de memoria antes de reiniciar servicios o finalizar procesos.",
            DetectionConfidence.High,
            "Memoria del sistema",
            recent.Select(s => s.Id).ToList(),
            recent[0].TimestampUtc,
            detectedAt,
            recent.Count,
            FindingLifecycleState.Active);
    }

    private static IEnumerable<Finding> EvaluateSustainedCommitPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var recent = RecentSamples(history, "system.commit.percent", TimeSpan.FromMinutes(10), 5, 3, nowUtc);
        if (recent.Count < 3 ||
            recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromSeconds(30) ||
            !IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(2)))
            yield break;

        var minimum = recent.Min(s => s.Value);
        if (minimum < 90)
            yield break;

        var severity = minimum >= 97 ? FindingSeverity.Critical : FindingSeverity.Warning;
        var fingerprint = "performance:sustained-commit-pressure";
        var detectedAt = recent[^1].TimestampUtc;
        yield return new Finding(
            FindingId(fingerprint, severity, detectedAt),
            fingerprint,
            detectedAt,
            severity,
            EvidenceCategory.Performance,
            "Presión sostenida de memoria comprometida",
            $"El commit de memoria permaneció por encima de {minimum:F0}% del límite durante varias muestras.",
            "Identifique procesos con crecimiento de memoria y confirme paging/commit antes de reiniciar o finalizar procesos.",
            DetectionConfidence.High,
            "Commit de memoria",
            recent.Select(s => s.Id).ToList(),
            recent[0].TimestampUtc,
            detectedAt,
            recent.Count,
            FindingLifecycleState.Active);
    }

    private static IEnumerable<Finding> EvaluateProcessCpuPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-10);
        var systemCpu = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "system.cpu.percent", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.TimestampUtc)
            .Take(3)
            .OrderBy(m => m.TimestampUtc)
            .ToList();
        var systemSaturated = systemCpu.Count >= 3 &&
                              systemCpu[^1].TimestampUtc - systemCpu[0].TimestampUtc >= TimeSpan.FromSeconds(30) &&
                              systemCpu.Min(s => s.Value) >= 95 &&
                              IsFresh(systemCpu[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(2));

        var groups = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "process.cpu.percent", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var recent = group.OrderByDescending(m => m.TimestampUtc).Take(4).OrderBy(m => m.TimestampUtc).ToList();
            if (recent.Count < 3 ||
                recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromMinutes(1) ||
                !IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var minimum = recent.Min(s => s.Value);
            // Fuera de una saturación global exigimos un proceso claramente alto.
            // Durante saturación global también conservamos consumidores >=10% para
            // poder explicar escenarios distribuidos entre varios procesos.
            var threshold = systemSaturated ? 10d : 50d;
            if (minimum < threshold)
                continue;

            var severity = minimum >= 85 && systemSaturated
                ? FindingSeverity.Critical
                : FindingSeverity.Warning;
            var confidence = minimum >= 50
                ? DetectionConfidence.High
                : DetectionConfidence.Medium;
            var fingerprint = $"performance:process-cpu:{Normalize(group.Key)}";
            var detectedAt = recent[^1].TimestampUtc;
            yield return new Finding(
                FindingId(fingerprint, severity, detectedAt),
                fingerprint,
                detectedAt,
                severity,
                EvidenceCategory.Performance,
                $"CPU sostenida por proceso: {group.Key}",
                $"{group.Key} mantuvo al menos {minimum:F0}% de CPU normalizada durante varias muestras." +
                (systemSaturated ? " La CPU global también se encontraba saturada." : string.Empty),
                "Revise la función del proceso y correlacione su consumo con eventos/servicios. WSG no finalizará procesos automáticamente.",
                confidence,
                group.Key,
                recent.Select(s => s.Id).ToList(),
                recent[0].TimestampUtc,
                detectedAt,
                recent.Count,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateProcessMemoryPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var system = RecentSamples(history, "system.memory.percent", TimeSpan.FromMinutes(10), 5, 3, nowUtc);
        if (system.Count < 3 ||
            system.Min(s => s.Value) < 90 ||
            !IsFresh(system[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(2)))
            yield break;

        var latestSystem = system[^1].TimestampUtc;
        var totalMemory = history
            .Where(m => string.Equals(m.Name, "system.memory.total_bytes", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefault()?.Value ?? 0d;
        if (totalMemory <= 0)
            yield break;

        var latestByProcess = history
            .Where(m => m.TimestampUtc >= latestSystem.AddMinutes(-3))
            .Where(m => string.Equals(m.Name, "process.memory.working_set_bytes", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
            .Where(m => IsFresh(m.TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
            .OrderByDescending(m => m.Value)
            .Take(3)
            .ToList();

        foreach (var process in latestByProcess)
        {
            var share = process.Value / totalMemory * 100d;
            if (share < 15)
                continue;

            var fingerprint = $"performance:process-memory-share:{Normalize(process.Entity)}";
            yield return new Finding(
                FindingId(fingerprint, FindingSeverity.Warning, process.TimestampUtc),
                fingerprint,
                process.TimestampUtc,
                FindingSeverity.Warning,
                EvidenceCategory.Performance,
                $"Proceso concentra memoria: {process.Entity}",
                $"{process.Entity} utiliza aproximadamente {process.Value / GiB:F1} GB ({share:F0}% de la RAM física) mientras existe presión de memoria del sistema.",
                "Valide si el consumo corresponde a la carga esperada y revise su tendencia antes de reiniciar o finalizar el proceso.",
                DetectionConfidence.High,
                process.Entity,
                [process.Id],
                process.TimestampUtc,
                process.TimestampUtc,
                1,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateProcessMemoryGrowth(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddHours(-2);
        var groups = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "process.memory.private_bytes", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var samples = group.OrderBy(m => m.TimestampUtc).ToList();
            if (samples.Count < 4)
                continue;

            var first = samples.First();
            var last = samples.Last();
            var elapsed = last.TimestampUtc - first.TimestampUtc;
            if (elapsed < TimeSpan.FromMinutes(15) ||
                !IsFresh(last.TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var growth = last.Value - first.Value;
            if (growth < 512d * 1024d * 1024d)
                continue;

            var rateGiBHour = growth / GiB / Math.Max(0.01, elapsed.TotalHours);
            if (rateGiBHour < 0.25)
                continue;

            var fingerprint = $"performance:process-memory-growth:{Normalize(group.Key)}";
            var severity = rateGiBHour >= 1 && last.Value >= 2 * GiB
                ? FindingSeverity.Critical
                : FindingSeverity.Warning;
            yield return new Finding(
                FindingId(fingerprint, severity, last.TimestampUtc),
                fingerprint,
                last.TimestampUtc,
                severity,
                EvidenceCategory.Performance,
                $"Crecimiento de memoria: {group.Key}",
                $"La memoria privada de {group.Key} creció {growth / GiB:F1} GB en {elapsed.TotalHours:F1} h ({rateGiBHour:F2} GB/h). Esto es un patrón, no una confirmación de fuga.",
                "Revise la aplicación, su carga y contadores relacionados. Confirme el patrón durante más tiempo antes de reiniciar el servicio/proceso.",
                DetectionConfidence.Medium,
                group.Key,
                [first.Id, last.Id],
                first.TimestampUtc,
                last.TimestampUtc,
                samples.Count,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateProcessCounterGrowth(
        IReadOnlyList<MetricSample> history,
        string metricName,
        string counterName,
        double minimumGrowth,
        double minimumGrowthPerHour,
        DateTime nowUtc)
    {
        var cutoff = nowUtc.AddHours(-2);
        foreach (var group in history
                     .Where(m => m.TimestampUtc >= cutoff)
                     .Where(m => string.Equals(m.Name, metricName, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.OrderBy(m => m.TimestampUtc).ToList();
            if (samples.Count < 4)
                continue;

            var first = samples.First();
            var last = samples.Last();
            var elapsed = last.TimestampUtc - first.TimestampUtc;
            if (elapsed < TimeSpan.FromMinutes(15) ||
                !IsFresh(last.TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var growth = last.Value - first.Value;
            var rate = growth / Math.Max(0.01, elapsed.TotalHours);
            if (growth < minimumGrowth || rate < minimumGrowthPerHour)
                continue;

            var label = counterName == "handle" ? "handles" : "hilos";
            var fingerprint = $"performance:process-{counterName}-growth:{Normalize(group.Key)}";
            yield return new Finding(
                FindingId(fingerprint, FindingSeverity.Warning, last.TimestampUtc),
                fingerprint,
                last.TimestampUtc,
                FindingSeverity.Warning,
                EvidenceCategory.Performance,
                $"Crecimiento de {label}: {group.Key}",
                $"{group.Key} incrementó {label} en {growth:N0} durante {elapsed.TotalHours:F1} h ({rate:N0}/h). Es un patrón de crecimiento, no una confirmación de fuga.",
                "Confirme que el crecimiento continúa y revise la aplicación/servicio propietario antes de reiniciar o finalizar procesos.",
                DetectionConfidence.Medium,
                group.Key,
                [first.Id, last.Id],
                first.TimestampUtc,
                last.TimestampUtc,
                samples.Count,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateDiskBusyPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-10);
        foreach (var group in history
                     .Where(m => m.TimestampUtc >= cutoff)
                     .Where(m => string.Equals(m.Name, "storage.busy_percent", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase))
        {
            var recent = group.OrderByDescending(m => m.TimestampUtc).Take(4).OrderBy(m => m.TimestampUtc).ToList();
            if (recent.Count < 3 ||
                recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromMinutes(1) ||
                !IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var minimum = recent.Min(s => s.Value);
            if (minimum < 90)
                continue;

            var severity = minimum >= 98 ? FindingSeverity.Critical : FindingSeverity.Warning;
            var fingerprint = $"performance:disk-busy:{Normalize(group.Key)}";
            yield return new Finding(
                FindingId(fingerprint, severity, recent[^1].TimestampUtc),
                fingerprint,
                recent[^1].TimestampUtc,
                severity,
                EvidenceCategory.Performance,
                $"Saturación sostenida de disco: {group.Key}",
                $"El tiempo ocupado del volumen {group.Key} permaneció por encima de {minimum:F0}% durante varias muestras.",
                "Correlacione la saturación con crecimiento de archivos, procesos, actualizaciones y eventos de E/S. No optimice ni desfragmente automáticamente.",
                DetectionConfidence.High,
                group.Key,
                recent.Select(s => s.Id).ToList(),
                recent[0].TimestampUtc,
                recent[^1].TimestampUtc,
                recent.Count,
                FindingLifecycleState.Active);
        }
    }

    private static IEnumerable<Finding> EvaluateNetworkSaturation(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-10);
        foreach (var group in history
                     .Where(m => m.TimestampUtc >= cutoff)
                     .Where(m => string.Equals(m.Name, "network.utilization.percent", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase))
        {
            var recent = group.OrderByDescending(m => m.TimestampUtc).Take(4).OrderBy(m => m.TimestampUtc).ToList();
            if (recent.Count < 3 ||
                recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromMinutes(1) ||
                !IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var minimum = recent.Min(s => s.Value);
            if (minimum < 90)
                continue;

            var fingerprint = $"network:saturation:{Normalize(group.Key)}";
            yield return new Finding(
                FindingId(fingerprint, FindingSeverity.Warning, recent[^1].TimestampUtc),
                fingerprint,
                recent[^1].TimestampUtc,
                FindingSeverity.Warning,
                EvidenceCategory.Network,
                $"Utilización de red sostenida: {group.Key}",
                $"La interfaz {group.Key} permaneció por encima de {minimum:F0}% de su velocidad de enlace durante varias muestras.",
                "Correlacione el periodo con aplicaciones, copias, backups o tráfico esperado. La velocidad de enlace no implica por sí sola congestión externa.",
                DetectionConfidence.Medium,
                group.Key,
                recent.Select(s => s.Id).ToList(),
                recent[0].TimestampUtc,
                recent[^1].TimestampUtc,
                recent.Count,
                FindingLifecycleState.Active);
        }
    }

    private static List<MetricSample> RecentSamples(
        IReadOnlyList<MetricSample> history,
        string name,
        TimeSpan window,
        int take,
        int minimum,
        DateTime nowUtc)
    {
        var cutoff = nowUtc - window;
        var result = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.TimestampUtc)
            .Take(take)
            .OrderBy(m => m.TimestampUtc)
            .ToList();
        return result.Count >= minimum ? result : [];
    }

    private static IEnumerable<Finding> EvaluateSustainedCpuPressure(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-10);
        var samples = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "system.cpu.percent", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.TimestampUtc)
            .Take(5)
            .OrderBy(m => m.TimestampUtc)
            .ToList();

        if (samples.Count < 3)
            yield break;

        var recent = samples.TakeLast(3).ToList();
        if (recent[^1].TimestampUtc - recent[0].TimestampUtc < TimeSpan.FromSeconds(30) ||
            recent.Min(s => s.Value) < 95 ||
            !IsFresh(recent[^1].TimestampUtc, nowUtc, TimeSpan.FromMinutes(2)))
            yield break;

        var fingerprint = "performance:sustained-cpu-pressure";
        var detectedAt = recent[^1].TimestampUtc;
        yield return new Finding(
            FindingId(fingerprint, FindingSeverity.Warning, detectedAt),
            fingerprint,
            detectedAt,
            FindingSeverity.Warning,
            EvidenceCategory.Performance,
            "CPU sostenida por encima de 95%",
            "La utilización de CPU se mantuvo en un nivel muy alto durante varias muestras consecutivas.",
            "Revise los procesos responsables y correlacione el periodo con servicios, actualizaciones y eventos antes de aplicar cambios.",
            DetectionConfidence.High,
            "CPU del sistema",
            recent.Select(s => s.Id).ToList(),
            recent[0].TimestampUtc,
            detectedAt,
            recent.Count,
            FindingLifecycleState.Active);
    }

    private static IEnumerable<Finding> EvaluateCapacityTrend(IReadOnlyList<MetricSample> history, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddHours(-2);
        var byVolume = history
            .Where(m => m.TimestampUtc >= cutoff)
            .Where(m => string.Equals(m.Name, "storage.free_bytes", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Entity, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byVolume)
        {
            var samples = group.OrderBy(s => s.TimestampUtc).ToList();
            if (samples.Count < 3)
                continue;

            var first = samples.First();
            var last = samples.Last();
            var elapsed = last.TimestampUtc - first.TimestampUtc;
            if (elapsed < TimeSpan.FromMinutes(10) ||
                !IsFresh(last.TimestampUtc, nowUtc, TimeSpan.FromMinutes(3)))
                continue;

            var lostBytes = first.Value - last.Value;
            if (lostBytes < 2 * GiB)
                continue;

            var lossGiBPerHour = lostBytes / GiB / Math.Max(0.01, elapsed.TotalHours);
            if (lossGiBPerHour < 1)
                continue;

            var hoursRemaining = last.Value / GiB / lossGiBPerHour;
            var severity = hoursRemaining < 12
                ? FindingSeverity.Critical
                : FindingSeverity.Warning;
            var fingerprint = $"storage:rapid-growth:{group.Key}";
            var detectedAt = last.TimestampUtc;

            yield return new Finding(
                FindingId(fingerprint, severity, detectedAt),
                fingerprint,
                detectedAt,
                severity,
                EvidenceCategory.Storage,
                $"Crecimiento acelerado en {group.Key}",
                $"El volumen perdió aproximadamente {lostBytes / GiB:F1} GB en {elapsed.TotalHours:F1} h ({lossGiBPerHour:F1} GB/h).",
                "Identifique qué directorio o proceso está creciendo. WSG no eliminará datos de aplicaciones únicamente por su tamaño o velocidad de crecimiento.",
                DetectionConfidence.Medium,
                group.Key,
                [first.Id, last.Id],
                first.TimestampUtc,
                detectedAt,
                samples.Count,
                FindingLifecycleState.Active);
        }
    }

    private static Finding MergeGroup(IGrouping<string, Finding> group)
    {
        var items = group.ToList();
        var strongest = items
            .OrderByDescending(f => SeverityRank(f.Severity))
            .ThenByDescending(f => f.DetectedAtUtc)
            .First();

        var firstSeen = items
            .Select(f => f.FirstSeenUtc ?? f.DetectedAtUtc)
            .Min();
        var lastSeen = items
            .Select(f => f.LastSeenUtc ?? f.DetectedAtUtc)
            .Max();
        var evidenceIds = items
            .SelectMany(f => f.EvidenceIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entities = items
            .Select(f => f.AffectedEntity)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var explanation = strongest.Explanation;
        if (evidenceIds.Count > 1)
            explanation += $" Evidencias correlacionadas en esta captura: {evidenceIds.Count}.";

        return strongest with
        {
            Id = FindingId(group.Key, strongest.Severity, lastSeen),
            DetectedAtUtc = lastSeen,
            Explanation = explanation,
            AffectedEntity = entities.Count == 1 ? entities[0] : "Varios componentes",
            EvidenceIds = evidenceIds,
            FirstSeenUtc = firstSeen,
            LastSeenUtc = lastSeen,
            OccurrenceCount = Math.Max(items.Sum(f => Math.Max(1, f.OccurrenceCount)), evidenceIds.Count),
            State = FindingLifecycleState.Active
        };
    }

    private static string TitleFor(EvidenceRecord item) => item.Category switch
    {
        EvidenceCategory.Storage => $"Almacenamiento requiere atención: {item.Entity}",
        EvidenceCategory.Reliability => "Evento de estabilidad de Windows",
        EvidenceCategory.Services => $"Servicio crítico detenido: {item.Entity}",
        EvidenceCategory.Integrity => $"Integridad de Windows requiere atención: {item.Entity}",
        EvidenceCategory.Servicing => $"Mantenimiento de Windows requiere atención: {item.Entity}",
        EvidenceCategory.Recovery => $"Recuperación de Windows requiere atención: {item.Entity}",
        EvidenceCategory.Backup => $"Preparación VSS/respaldo requiere atención: {item.Entity}",
        _ => $"{item.Category}: {item.Entity}"
    };

    private static string RecommendFor(EvidenceRecord item)
    {
        if (item.Category == EvidenceCategory.Storage)
            return "Revise el volumen y use Liberar espacio únicamente para candidatos SAFE. No elimine datos de aplicaciones o roles sin identificar su propietario.";
        if (item.Category == EvidenceCategory.Services)
            return "Revise el servicio, sus dependencias y los eventos asociados. No fuerce el inicio ni cambie su configuración sin confirmar la causa.";
        if (item.Category == EvidenceCategory.Reliability &&
            item.CorrelationKey.Contains("storage", StringComparison.OrdinalIgnoreCase))
            return "Revise primero la salud del volumen, la latencia y los eventos de almacenamiento. Considere respaldo si existen errores físicos o de E/S.";
        if (item.Category == EvidenceCategory.Reliability)
            return "Correlacione este evento con almacenamiento, servicios y actualizaciones del mismo periodo antes de decidir una reparación.";
        if (item.Category == EvidenceCategory.Integrity)
            return "Confirme la integridad con las comprobaciones soportadas de solo lectura. Si Windows requiere una reparación, prográmela en una fase controlada; WSG 0.4.1 no la ejecuta.";
        if (item.Category == EvidenceCategory.Servicing)
            return "Revise Component Store, historial de Windows Update, espacio disponible y reinicio pendiente antes de modificar componentes de servicing.";
        if (item.Category == EvidenceCategory.Recovery)
            return "Valide WinRE y su configuración antes de modificar BCD, particiones o la imagen de recuperación. WSG no realiza esos cambios en esta fase.";
        if (item.Category == EvidenceCategory.Backup)
            return "Identifique los VSS writers afectados y el componente propietario antes de reiniciar servicios o manipular snapshots.";
        return "Revise la evidencia asociada y confirme la causa antes de aplicar cambios.";
    }

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "entity" : new string(chars).ToLowerInvariant();
    }

    private static bool IsFresh(DateTime timestampUtc, DateTime nowUtc, TimeSpan maximumAge) =>
        timestampUtc <= nowUtc + TimeSpan.FromSeconds(5) &&
        timestampUtc >= nowUtc - maximumAge;

    private static string FindingId(
        string fingerprint,
        FindingSeverity severity,
        DateTime timestampUtc)
    {
        // Buckets cortos limitan ruido, pero la severidad forma parte del ID para
        // que Warning → Critical nunca se pierda dentro del mismo intervalo.
        var bucketMinute = timestampUtc.Minute / 5 * 5;
        return $"finding:{fingerprint}:{severity}:{timestampUtc:yyyyMMddHH}{bucketMinute:00}";
    }

    private static int SeverityRank(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => 2,
        FindingSeverity.Warning => 1,
        _ => 0
    };
}
