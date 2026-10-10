using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Correlation;

/// <summary>
/// Correlación determinística y explicable. No ejecuta acciones ni utiliza IA.
/// </summary>
public sealed class RootCauseCorrelationEngine
{
    public RootCauseCandidate Correlate(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<EvidenceRecord> evidence,
        IReadOnlyList<MetricSample> history)
    {
        var fingerprints = findings.Select(f => f.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evidenceIds = findings.SelectMany(f => f.EvidenceIds)
            .Concat(evidence.Select(e => e.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var componentStore = findings.FirstOrDefault(f =>
            f.Fingerprint.Contains("component-store", StringComparison.OrdinalIgnoreCase));
        if (componentStore is not null)
        {
            var nonRepairable = componentStore.Severity == FindingSeverity.Critical ||
                                componentStore.Explanation.Contains("no reparable", StringComparison.OrdinalIgnoreCase);
            return Candidate(
                nonRepairable ? "ComponentStoreNonRepairable" : "ComponentStoreCorruption",
                nonRepairable ? "Corrupción no reparable del Component Store" : "Corrupción del Component Store",
                nonRepairable
                    ? "DISM reportó que el almacén de componentes no puede repararse con el flujo normal de servicing. WSG conserva el incidente y no intenta reparación automática."
                    : "DISM detectó corrupción o un estado no saludable del almacén de componentes. Esta capa puede afectar Windows Update, SFC y mantenimiento de características.",
                DetectionConfidence.High,
                nonRepairable ? 98 : 94,
                evidenceIds,
                fingerprints);
        }

        var systemFiles = findings.FirstOrDefault(f =>
            f.Fingerprint.Contains("system-files", StringComparison.OrdinalIgnoreCase));
        if (systemFiles is not null)
        {
            return Candidate(
                "SystemFileIntegrity",
                "Integridad de archivos protegidos comprometida",
                "SFC /verifyonly detectó o no pudo descartar violaciones de integridad en recursos protegidos de Windows. WSG no ejecutó /scannow ni reemplazó archivos.",
                DetectionConfidence.High,
                92,
                evidenceIds,
                fingerprints);
        }

        var fileSystem = findings.FirstOrDefault(f =>
            f.Category == EvidenceCategory.Integrity &&
            f.Fingerprint.Contains("filesystem", StringComparison.OrdinalIgnoreCase));
        if (fileSystem is not null)
        {
            return Candidate(
                "FileSystemIntegrity",
                $"Inconsistencia del sistema de archivos: {fileSystem.AffectedEntity}",
                "Windows reporta dirty bit, estado anómalo o un escaneo de filesystem no saludable. Debe confirmarse antes de cualquier reparación offline.",
                DetectionConfidence.High,
                92,
                evidenceIds,
                fingerprints);
        }

        var updateFailure = findings.FirstOrDefault(f =>
            f.Fingerprint.Contains("windows-update-failure", StringComparison.OrdinalIgnoreCase) ||
            f.Fingerprint.Contains("windows-update-partial", StringComparison.OrdinalIgnoreCase));
        if (updateFailure is not null)
        {
            return Candidate(
                "WindowsUpdateFailure",
                "Fallo recurrente de Windows Update",
                "Windows Update Agent registra operaciones fallidas, canceladas o completadas con errores. La causa todavía puede estar en servicing, conectividad, políticas o en el propio paquete; WSG no atribuye más sin evidencia adicional.",
                DetectionConfidence.High,
                88,
                evidenceIds,
                fingerprints);
        }

        var pendingReboot = findings.FirstOrDefault(f =>
            f.Fingerprint.Contains("update-reboot-required", StringComparison.OrdinalIgnoreCase));
        if (pendingReboot is not null)
        {
            return Candidate(
                "PendingServicingReboot",
                "Reinicio pendiente de Windows Update",
                "Windows Update Agent informa que un reinicio es necesario para completar operaciones de actualización. WSG no reinicia el equipo automáticamente.",
                DetectionConfidence.High,
                98,
                evidenceIds,
                fingerprints);
        }

        var winRe = findings.FirstOrDefault(f => f.Category == EvidenceCategory.Recovery);
        if (winRe is not null)
        {
            return Candidate(
                "RecoveryEnvironmentUnavailable",
                "Windows Recovery Environment no está preparado",
                "REAgentC no confirmó un WinRE habilitado y operativo. Esto no implica un fallo actual de Windows, pero reduce la capacidad de recuperación si el sistema deja de iniciar.",
                DetectionConfidence.High,
                90,
                evidenceIds,
                fingerprints);
        }

        var vss = findings.FirstOrDefault(f => f.Category == EvidenceCategory.Backup);
        if (vss is not null)
        {
            return Candidate(
                "VssWriterFailure",
                "Preparación VSS degradada",
                "Uno o más VSS writers no están en estado estable o no pudieron enumerarse. Esto puede afectar respaldos consistentes con aplicaciones, pero WSG no reinicia writers ni servicios automáticamente.",
                DetectionConfidence.High,
                90,
                evidenceIds,
                fingerprints);
        }

        var processMemoryFindings = findings.Where(f =>
            f.Fingerprint.StartsWith("performance:process-memory", StringComparison.OrdinalIgnoreCase)).ToList();
        var processMemory = processMemoryFindings.FirstOrDefault();
        var memoryProcessEntities = processMemoryFindings
            .Select(f => f.AffectedEntity)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var systemMemory = findings.FirstOrDefault(f =>
            f.Fingerprint is "performance:sustained-memory-pressure" or "performance:sustained-commit-pressure");
        if (systemMemory is not null && memoryProcessEntities.Count == 1)
        {
            var processEntity = memoryProcessEntities[0];
            return Candidate(
                "MemoryPressureByProcess",
                $"Presión de memoria asociada a {processEntity}",
                $"WSG observó presión sostenida del sistema y, en el mismo periodo, {processEntity} concentró o incrementó memoria de forma relevante. El patrón es consistente con presión originada o amplificada por ese proceso, pero no confirma por sí solo una fuga.",
                DetectionConfidence.High,
                90,
                evidenceIds,
                fingerprints);
        }

        if (systemMemory is not null && memoryProcessEntities.Count > 1)
        {
            return Candidate(
                "MemoryPressureWithMultipleProcesses",
                "Presión de memoria con varios consumidores relevantes",
                $"WSG detectó presión sostenida y {memoryProcessEntities.Count} procesos con señales relevantes de memoria. La evidencia no permite atribuir de forma responsable la causa a uno solo.",
                DetectionConfidence.Medium,
                82,
                evidenceIds,
                fingerprints);
        }

        if (processMemory is not null && systemMemory is null)
        {
            return Candidate(
                "ProcessMemoryGrowth",
                $"Crecimiento de memoria observado en {processMemory.AffectedEntity}",
                $"{processMemory.AffectedEntity} presenta un patrón de crecimiento o concentración de memoria. Aún no existe presión global suficiente para afirmar impacto sistémico ni para confirmar una fuga.",
                DetectionConfidence.Medium,
                72,
                evidenceIds,
                fingerprints);
        }

        var processCpuFindings = findings.Where(f =>
            f.Fingerprint.StartsWith("performance:process-cpu:", StringComparison.OrdinalIgnoreCase)).ToList();
        var processCpu = processCpuFindings.FirstOrDefault();
        var cpuProcessEntities = processCpuFindings
            .Select(f => f.AffectedEntity)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var systemCpu = findings.FirstOrDefault(f =>
            f.Fingerprint == "performance:sustained-cpu-pressure");
        if (systemCpu is not null && cpuProcessEntities.Count == 1)
        {
            var processEntity = cpuProcessEntities[0];
            return Candidate(
                "CpuSaturationByProcess",
                $"Saturación de CPU asociada a {processEntity}",
                $"La CPU global permaneció saturada y {processEntity} mostró consumo sostenido elevado en el mismo intervalo.",
                DetectionConfidence.High,
                92,
                evidenceIds,
                fingerprints);
        }

        if (systemCpu is not null && cpuProcessEntities.Count > 1)
        {
            return Candidate(
                "CpuSaturationWithMultipleProcesses",
                "Saturación de CPU con varios consumidores relevantes",
                $"La CPU global permaneció saturada y {cpuProcessEntities.Count} procesos mostraron carga sostenida. La evidencia no justifica atribuir toda la saturación a uno solo.",
                DetectionConfidence.Medium,
                84,
                evidenceIds,
                fingerprints);
        }

        if (processCpu is not null && systemCpu is null)
        {
            return Candidate(
                "ProcessCpuLoad",
                $"Carga sostenida de CPU en {processCpu.AffectedEntity}",
                $"{processCpu.AffectedEntity} mostró consumo sostenido elevado. La CPU global no alcanzó el umbral de saturación sistémica, por lo que debe validarse si la carga es esperada.",
                DetectionConfidence.Medium,
                74,
                evidenceIds,
                fingerprints);
        }

        var storageReliability = findings.Any(f =>
            f.Category == EvidenceCategory.Reliability &&
            (f.Fingerprint.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
             f.Fingerprint.Contains("filesystem", StringComparison.OrdinalIgnoreCase)));
        var storagePressure = findings.Any(f =>
            f.Category == EvidenceCategory.Storage ||
            f.Fingerprint.StartsWith("performance:disk-busy:", StringComparison.OrdinalIgnoreCase));
        if (storageReliability && storagePressure)
        {
            return Candidate(
                "StorageIoReliability",
                "Degradación de almacenamiento con impacto operativo",
                "Coinciden presión/saturación del almacenamiento y eventos de E/S o sistema de archivos. Debe tratarse primero como problema de integridad/rendimiento del subsistema de almacenamiento y no como simple falta de espacio.",
                DetectionConfidence.High,
                94,
                evidenceIds,
                fingerprints);
        }

        if (storageReliability && !storagePressure)
        {
            return Candidate(
                "StorageReliability",
                "Evento de confiabilidad de almacenamiento",
                "Windows registró un error de E/S o del sistema de archivos sin una señal simultánea de presión de capacidad/rendimiento. Debe diagnosticarse el volumen antes de cualquier optimización o reparación.",
                DetectionConfidence.High,
                88,
                evidenceIds,
                fingerprints);
        }

        var hardware = findings.Any(f =>
            f.Fingerprint.Contains("whea", StringComparison.OrdinalIgnoreCase));
        var crash = findings.Any(f =>
            f.Fingerprint.Contains("unexpected-shutdown", StringComparison.OrdinalIgnoreCase) ||
            f.Fingerprint.Contains("bugcheck", StringComparison.OrdinalIgnoreCase));
        if (hardware && crash)
        {
            return Candidate(
                "HardwareInstability",
                "Inestabilidad posiblemente originada en hardware",
                "Errores WHEA coinciden con reinicio inesperado o BugCheck. Esta combinación eleva la probabilidad de una causa de hardware, firmware, bus o dispositivo por encima de un fallo puramente lógico de Windows.",
                DetectionConfidence.High,
                93,
                evidenceIds,
                fingerprints);
        }

        if (hardware)
        {
            return Candidate(
                "HardwareError",
                "Error de hardware registrado por Windows",
                "WHEA registró una condición de hardware relevante, pero la evidencia actual no demuestra que haya provocado un reinicio o BugCheck. Debe conservarse la evidencia y validarse hardware, firmware, bus o dispositivo antes de atribuir una causa más específica.",
                DetectionConfidence.High,
                86,
                evidenceIds,
                fingerprints);
        }

        var processResourceGrowth = findings.FirstOrDefault(f =>
            f.Fingerprint.Contains("handle-growth", StringComparison.OrdinalIgnoreCase) ||
            f.Fingerprint.Contains("thread-growth", StringComparison.OrdinalIgnoreCase));
        if (processResourceGrowth is not null)
        {
            return Candidate(
                "ProcessResourceGrowth",
                $"Crecimiento de recursos en {processResourceGrowth.AffectedEntity}",
                $"{processResourceGrowth.AffectedEntity} presenta crecimiento sostenido de handles o hilos. El patrón puede preceder agotamiento de recursos, pero requiere confirmación durante más tiempo y contexto de la aplicación.",
                DetectionConfidence.Medium,
                70,
                evidenceIds,
                fingerprints);
        }

        var serviceFailure = findings.Any(f =>
            f.Category == EvidenceCategory.Services ||
            f.Fingerprint.Contains("service-failure", StringComparison.OrdinalIgnoreCase));
        if (serviceFailure)
        {
            return Candidate(
                "ServiceFailure",
                "Fallo o detención de servicio de Windows",
                "WSG detectó un servicio crítico detenido o fallos registrados por Service Control Manager. Deben revisarse dependencias y eventos adyacentes antes de forzar su inicio.",
                DetectionConfidence.High,
                85,
                evidenceIds,
                fingerprints);
        }

        if (findings.Any(f => f.Category == EvidenceCategory.Network))
        {
            return Candidate(
                "NetworkPressure",
                "Presión o errores de red",
                "La evidencia apunta a saturación, errores o descartes en una interfaz de red. La velocidad de enlace local no confirma por sí sola congestión fuera del equipo.",
                DetectionConfidence.Medium,
                75,
                evidenceIds,
                fingerprints);
        }

        if (storagePressure)
        {
            return Candidate(
                "StoragePressure",
                "Presión de almacenamiento",
                "La evidencia principal corresponde a capacidad, crecimiento o saturación del almacenamiento. WSG debe identificar el consumidor antes de recomendar eliminación de datos.",
                DetectionConfidence.High,
                84,
                evidenceIds,
                fingerprints);
        }

        if (systemMemory is not null)
        {
            return Candidate(
                "MemoryPressure",
                "Presión de memoria del sistema",
                "La memoria o el commit permanecieron elevados, pero todavía no existe evidencia suficiente para atribuir la causa a un proceso específico.",
                DetectionConfidence.Medium,
                72,
                evidenceIds,
                fingerprints);
        }

        if (systemCpu is not null)
        {
            return Candidate(
                "CpuPressure",
                "Presión de CPU del sistema",
                "La CPU permaneció elevada, pero todavía no existe una atribución suficientemente fuerte a un proceso específico.",
                DetectionConfidence.Medium,
                72,
                evidenceIds,
                fingerprints);
        }

        if (crash)
        {
            return Candidate(
                "WindowsInstability",
                "Inestabilidad de Windows",
                "Windows registró un reinicio inesperado o BugCheck. No hay evidencia suficiente en la captura actual para afirmar una causa más específica.",
                DetectionConfidence.Medium,
                68,
                evidenceIds,
                fingerprints);
        }

        var strongest = findings
            .OrderByDescending(f => f.Severity == FindingSeverity.Critical ? 2 : 1)
            .First();
        return Candidate(
            "Generic",
            strongest.Title,
            "La evidencia permite confirmar el problema observado, pero no una causa raíz única. WSG mantiene el diagnóstico como hipótesis hasta recopilar más señales.",
            DetectionConfidence.Medium,
            60,
            evidenceIds,
            fingerprints);
    }

    private static RootCauseCandidate Candidate(
        string kind,
        string title,
        string explanation,
        DetectionConfidence confidence,
        int percent,
        IReadOnlyList<string> evidenceIds,
        IEnumerable<string> findingFingerprints) =>
        new(kind, title, explanation, confidence, percent,
            evidenceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            findingFingerprints.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
}
