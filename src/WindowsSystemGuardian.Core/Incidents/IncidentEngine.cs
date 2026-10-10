using System.Security.Cryptography;
using System.Text;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Recommendations;

namespace WindowsSystemGuardian.Core.Incidents;

/// <summary>
/// Convierte hallazgos en incidentes por familias causales, alcance estable y
/// relación temporal explícita. Evita agrupar problemas únicamente porque están
/// presentes en la misma captura y evita mezclar volúmenes, interfaces o servicios
/// independientes.
/// </summary>
public sealed class IncidentEngine(
    RootCauseCorrelationEngine rootCauseEngine,
    RecommendationEngine recommendationEngine)
{
    private static readonly TimeSpan ProcessCorrelationTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HardwareCorrelationTolerance = TimeSpan.FromMinutes(10);

    public IReadOnlyList<Incident> Build(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<EvidenceRecord> evidence,
        IReadOnlyList<MetricSample> history)
    {
        var remaining = findings
            .Where(f => f.State == FindingLifecycleState.Active)
            .Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
            .Where(f => f.Category != EvidenceCategory.Observability)
            .OrderBy(f => f.DetectedAtUtc)
            .ToList();
        if (remaining.Count == 0)
            return [];

        var clusters = new List<IncidentCluster>();

        AddHardwareCluster(remaining, clusters);
        AddStorageClusters(remaining, clusters);
        AddMemoryClusters(remaining, clusters);
        AddCpuClusters(remaining, clusters);
        AddEntityClusters(
            remaining,
            clusters,
            "process-resource",
            f => f.Fingerprint.Contains("handle-growth", StringComparison.OrdinalIgnoreCase) ||
                 f.Fingerprint.Contains("thread-growth", StringComparison.OrdinalIgnoreCase));
        AddEntityClusters(
            remaining,
            clusters,
            "service",
            f => f.Category == EvidenceCategory.Services ||
                 f.Fingerprint.Contains("service-failure", StringComparison.OrdinalIgnoreCase));
        AddEntityClusters(
            remaining,
            clusters,
            "network",
            f => f.Category == EvidenceCategory.Network ||
                 f.Fingerprint.StartsWith("network:", StringComparison.OrdinalIgnoreCase));
        AddEntityClusters(
            remaining,
            clusters,
            "integrity",
            f => f.Category == EvidenceCategory.Integrity);
        AddEntityClusters(
            remaining,
            clusters,
            "servicing",
            f => f.Category == EvidenceCategory.Servicing);
        AddEntityClusters(
            remaining,
            clusters,
            "recovery",
            f => f.Category == EvidenceCategory.Recovery);
        AddEntityClusters(
            remaining,
            clusters,
            "backup",
            f => f.Category == EvidenceCategory.Backup);

        // Lo que no comparte una familia causal demostrable permanece separado.
        foreach (var finding in remaining)
            clusters.Add(new IncidentCluster("generic", finding.Fingerprint, [finding]));

        return clusters
            .Select(cluster => BuildIncident(cluster, evidence, history))
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.LastSeenUtc)
            .ToList();
    }

    private static void AddHardwareCluster(List<Finding> remaining, List<IncidentCluster> clusters)
    {
        var whea = remaining.Where(IsWhea).ToList();
        var crashes = remaining.Where(IsCrash).ToList();
        if (whea.Count == 0 || crashes.Count == 0)
            return;

        var relatedWhea = whea
            .Where(w => crashes.Any(c => AreTemporallyRelated(w, c, HardwareCorrelationTolerance)))
            .ToList();
        var relatedCrashes = crashes
            .Where(c => relatedWhea.Any(w => AreTemporallyRelated(w, c, HardwareCorrelationTolerance)))
            .ToList();

        if (relatedWhea.Count == 0 || relatedCrashes.Count == 0)
            return;

        var members = relatedWhea.Concat(relatedCrashes).Distinct().ToList();
        clusters.Add(new IncidentCluster("hardware", "system", members));
        RemoveAll(remaining, members);
    }

    private static void AddStorageClusters(List<Finding> remaining, List<IncidentCluster> clusters)
    {
        var pressure = remaining
            .Where(f => f.Category == EvidenceCategory.Storage || IsDiskBusy(f))
            .ToList();
        var reliability = remaining.Where(IsStorageReliability).ToList();

        foreach (var group in pressure.GroupBy(ScopeForEntity, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();

            // No asociar un evento genérico "Almacenamiento" a C:/D: solamente
            // porque existe un único volumen bajo presión. Para una correlación de
            // alta confianza exigimos alcance identificable y relación temporal.
            var relatedReliability = reliability
                .Where(r => EntityMatchesScope(r, group.Key))
                .Where(r => members.Any(p => AreTemporallyRelated(r, p, TimeSpan.FromMinutes(10))))
                .ToList();
            members.AddRange(relatedReliability);
            members = members.Distinct().ToList();

            clusters.Add(new IncidentCluster("storage", group.Key, members));
            RemoveAll(remaining, members);
        }

        // Eventos de confiabilidad cuyo volumen/dispositivo no puede demostrarse
        // permanecen independientes. Es preferible una causa menos específica a
        // atribuir de forma incorrecta un error físico a un volumen lógico.
        AddEntityClusters(remaining, clusters, "storage-reliability", IsStorageReliability);
    }

    private static void AddMemoryClusters(List<Finding> remaining, List<IncidentCluster> clusters)
    {
        var system = remaining.Where(IsSystemMemoryFinding).ToList();
        var process = remaining.Where(IsProcessMemoryFinding).ToList();

        if (system.Count > 0)
        {
            var relatedProcess = process
                .Where(p => system.Any(s => AreTemporallyRelated(p, s, ProcessCorrelationTolerance)))
                .ToList();
            var members = system.Concat(relatedProcess).Distinct().ToList();
            clusters.Add(new IncidentCluster("memory", "system", members));
            RemoveAll(remaining, members);
        }

        AddEntityClusters(remaining, clusters, "process-memory", IsProcessMemoryFinding);
    }

    private static void AddCpuClusters(List<Finding> remaining, List<IncidentCluster> clusters)
    {
        var system = remaining.Where(IsSystemCpuFinding).ToList();
        var process = remaining.Where(IsProcessCpuFinding).ToList();

        if (system.Count > 0)
        {
            var relatedProcess = process
                .Where(p => system.Any(s => AreTemporallyRelated(p, s, ProcessCorrelationTolerance)))
                .ToList();
            var members = system.Concat(relatedProcess).Distinct().ToList();
            clusters.Add(new IncidentCluster("cpu", "system", members));
            RemoveAll(remaining, members);
        }

        AddEntityClusters(remaining, clusters, "process-cpu", IsProcessCpuFinding);
    }

    private static void AddEntityClusters(
        List<Finding> remaining,
        List<IncidentCluster> clusters,
        string family,
        Func<Finding, bool> predicate)
    {
        var matches = remaining.Where(predicate).ToList();
        if (matches.Count == 0)
            return;

        foreach (var group in matches.GroupBy(ScopeForEntity, StringComparer.OrdinalIgnoreCase))
            clusters.Add(new IncidentCluster(family, group.Key, group.ToList()));

        RemoveAll(remaining, matches);
    }

    private Incident BuildIncident(
        IncidentCluster incidentCluster,
        IReadOnlyList<EvidenceRecord> allEvidence,
        IReadOnlyList<MetricSample> history)
    {
        var cluster = incidentCluster.Findings;
        var sourceIds = cluster.SelectMany(f => f.EvidenceIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var first = cluster.Min(FirstSeen);
        var last = cluster.Max(LastSeen);

        // Solo adjuntar EvidenceRecord expresamente referenciada por un Finding.
        // Proximidad temporal/categoría por sí solas no demuestran relación causal.
        var relatedEvidence = allEvidence
            .Where(e => sourceIds.Contains(e.Id))
            .ToList();

        var rootCause = rootCauseEngine.Correlate(cluster, relatedEvidence, history);
        var severity = cluster.Any(f => f.Severity == FindingSeverity.Critical)
            ? IncidentSeverity.Critical
            : IncidentSeverity.Warning;
        var entities = cluster.Select(f => f.AffectedEntity)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // La identidad del incidente depende de su familia/alcance, no del conjunto
        // exacto de Findings. Esto permite refinar la causa sin fragmentar el episodio.
        var fingerprint = StableFingerprint(incidentCluster.Family, incidentCluster.Scope);
        var recommendations = recommendationEngine.Build(rootCause.Kind);
        var verification = recommendations.Select(r => r.Verification)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var verificationCollectors = DetermineVerificationCollectors(incidentCluster, rootCause, cluster);

        var impact = BuildImpact(cluster);
        var summary = cluster.Count == 1
            ? cluster[0].Explanation
            : $"WSG correlacionó {cluster.Count} hallazgos compatibles en alcance y tiempo. {rootCause.Explanation}";

        return new Incident(
            IncidentId(fingerprint, severity, last),
            fingerprint,
            first,
            last,
            severity,
            IncidentState.Active,
            rootCause.Title,
            summary,
            impact,
            rootCause,
            entities,
            cluster.Select(f => f.Fingerprint).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            rootCause.EvidenceIds,
            recommendations,
            verification,
            OccurrenceCount: cluster.Sum(f => Math.Max(1, f.OccurrenceCount)),
            VerificationCollectors: verificationCollectors,
            StateChangedAtUtc: null);
    }

    private static IReadOnlyList<string> DetermineVerificationCollectors(
        IncidentCluster incidentCluster,
        RootCauseCandidate rootCause,
        IReadOnlyList<Finding> findings)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (incidentCluster.Family)
        {
            case "memory":
            case "cpu":
                required.Add("Rendimiento");
                required.Add("Procesos");
                break;
            case "process-memory":
            case "process-cpu":
            case "process-resource":
                required.Add("Procesos");
                break;
            case "storage":
                required.Add("Almacenamiento");
                required.Add("Rendimiento de disco");
                if (rootCause.Kind is "StorageIoReliability" or "StorageReliability")
                    required.Add("Eventos de Windows");
                break;
            case "storage-reliability":
            case "hardware":
                required.Add("Eventos de Windows");
                break;
            case "service":
                if (findings.Any(f => f.Category == EvidenceCategory.Services))
                    required.Add("Servicios");
                if (findings.Any(f => f.Category == EvidenceCategory.Reliability))
                    required.Add("Eventos de Windows");
                break;
            case "network":
                required.Add("Red");
                break;
            case "integrity":
                foreach (var collector in IntegrityVerificationCollectors(findings))
                    required.Add(collector);
                break;
            case "servicing":
                foreach (var collector in ServicingVerificationCollectors(findings))
                    required.Add(collector);
                break;
            case "recovery":
                required.Add("Preparación de recuperación WinRE");
                break;
            case "backup":
                required.Add("Preparación VSS / respaldo");
                break;
            default:
                foreach (var category in findings.Select(f => f.Category).Distinct())
                {
                    var collector = CollectorForCategory(category);
                    if (collector is not null)
                        required.Add(collector);
                }
                break;
        }

        return required.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? CollectorForCategory(EvidenceCategory category) => category switch
    {
        EvidenceCategory.Storage => "Almacenamiento",
        EvidenceCategory.Performance => "Rendimiento",
        EvidenceCategory.Reliability => "Eventos de Windows",
        EvidenceCategory.Services => "Servicios",
        EvidenceCategory.Network => "Red",
        EvidenceCategory.Integrity => "Integridad del sistema de archivos",
        EvidenceCategory.Servicing => "Salud de Windows Update",
        EvidenceCategory.Recovery => "Preparación de recuperación WinRE",
        EvidenceCategory.Backup => "Preparación VSS / respaldo",
        _ => null
    };

    private static IEnumerable<string> IntegrityVerificationCollectors(IReadOnlyList<Finding> findings)
    {
        // El collector que originó el hallazgo debe poder verificarlo por sí mismo.
        // No exigir simultáneamente un deep scan manual y el quick collector, porque
        // el scheduler los ejecuta con cadencias/costos diferentes.
        if (findings.Any(f => f.Fingerprint.Contains("system-files", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Verificación de archivos protegidos";
            yield break;
        }
        if (findings.Any(f => f.Fingerprint.Contains("filesystem-scan", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Escaneo profundo del sistema de archivos";
            yield break;
        }
        if (findings.Any(f => f.Fingerprint.Contains("filesystem", StringComparison.OrdinalIgnoreCase)))
            yield return "Integridad del sistema de archivos";
    }

    private static IEnumerable<string> ServicingVerificationCollectors(IReadOnlyList<Finding> findings)
    {
        if (findings.Any(f => f.Fingerprint.Contains("component-store-deep", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Escaneo profundo del almacén de componentes";
            yield break;
        }
        if (findings.Any(f => f.Fingerprint.Contains("component-store", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Salud del almacén de componentes";
            yield break;
        }
        if (findings.Any(f => f.Fingerprint.Contains("windows-update", StringComparison.OrdinalIgnoreCase) ||
                              f.Fingerprint.Contains("reboot", StringComparison.OrdinalIgnoreCase)))
            yield return "Salud de Windows Update";
    }

    private static string StableFingerprint(string family, string scope)
    {
        var normalizedFamily = Normalize(family);
        var normalizedScope = Normalize(scope);
        var seed = $"{family}|{scope}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant()[..12];
        return $"incident:{normalizedFamily}:{normalizedScope}:{hash}";
    }

    private static string ScopeForEntity(Finding finding) =>
        string.IsNullOrWhiteSpace(finding.AffectedEntity)
            ? finding.Fingerprint
            : finding.AffectedEntity;

    private static bool EntityMatchesScope(Finding finding, string scope) =>
        string.Equals(ScopeForEntity(finding), scope, StringComparison.OrdinalIgnoreCase);

    private static bool AreTemporallyRelated(Finding left, Finding right, TimeSpan tolerance)
    {
        var leftStart = FirstSeen(left);
        var leftEnd = LastSeen(left);
        var rightStart = FirstSeen(right);
        var rightEnd = LastSeen(right);

        if (leftStart <= rightEnd && rightStart <= leftEnd)
            return true;

        var gap = leftEnd < rightStart
            ? rightStart - leftEnd
            : leftStart - rightEnd;
        return gap <= tolerance;
    }

    private static DateTime FirstSeen(Finding finding) => finding.FirstSeenUtc ?? finding.DetectedAtUtc;
    private static DateTime LastSeen(Finding finding) => finding.LastSeenUtc ?? finding.DetectedAtUtc;

    private static void RemoveAll(List<Finding> remaining, IEnumerable<Finding> matches)
    {
        foreach (var item in matches.ToList())
            remaining.Remove(item);
    }

    private static bool IsSystemMemoryFinding(Finding f) =>
        f.Fingerprint is "performance:sustained-memory-pressure" or "performance:sustained-commit-pressure";

    private static bool IsProcessMemoryFinding(Finding f) =>
        f.Fingerprint.StartsWith("performance:process-memory", StringComparison.OrdinalIgnoreCase);

    private static bool IsSystemCpuFinding(Finding f) =>
        f.Fingerprint == "performance:sustained-cpu-pressure";

    private static bool IsProcessCpuFinding(Finding f) =>
        f.Fingerprint.StartsWith("performance:process-cpu:", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiskBusy(Finding f) =>
        f.Fingerprint.StartsWith("performance:disk-busy:", StringComparison.OrdinalIgnoreCase);

    private static bool IsStorageReliability(Finding f) =>
        f.Category == EvidenceCategory.Reliability &&
        (f.Fingerprint.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
         f.Fingerprint.Contains("filesystem", StringComparison.OrdinalIgnoreCase));

    private static bool IsWhea(Finding f) =>
        f.Fingerprint.Contains("whea", StringComparison.OrdinalIgnoreCase);

    private static bool IsCrash(Finding f) =>
        f.Fingerprint.Contains("unexpected-shutdown", StringComparison.OrdinalIgnoreCase) ||
        f.Fingerprint.Contains("bugcheck", StringComparison.OrdinalIgnoreCase);

    private static string BuildImpact(IReadOnlyList<Finding> findings)
    {
        var categories = findings.Select(f => f.Category).Distinct().ToList();
        var parts = new List<string>();
        if (categories.Contains(EvidenceCategory.Storage)) parts.Add("almacenamiento");
        if (categories.Contains(EvidenceCategory.Performance)) parts.Add("rendimiento");
        if (categories.Contains(EvidenceCategory.Services)) parts.Add("servicios");
        if (categories.Contains(EvidenceCategory.Reliability)) parts.Add("estabilidad de Windows");
        if (categories.Contains(EvidenceCategory.Network)) parts.Add("red");
        if (categories.Contains(EvidenceCategory.Integrity)) parts.Add("integridad del sistema");
        if (categories.Contains(EvidenceCategory.Servicing)) parts.Add("mantenimiento de Windows");
        if (categories.Contains(EvidenceCategory.Recovery)) parts.Add("recuperación");
        if (categories.Contains(EvidenceCategory.Backup)) parts.Add("preparación de respaldo/VSS");
        if (parts.Count == 0) parts.Add("sistema");
        return $"Puede afectar: {string.Join(", ", parts)}.";
    }

    private static string IncidentId(string fingerprint, IncidentSeverity severity, DateTime timestampUtc) =>
        $"{fingerprint}:{severity}:{timestampUtc:yyyyMMddHHmmss}";

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(64).ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars).ToLowerInvariant();
    }

    private sealed record IncidentCluster(
        string Family,
        string Scope,
        IReadOnlyList<Finding> Findings);
}
