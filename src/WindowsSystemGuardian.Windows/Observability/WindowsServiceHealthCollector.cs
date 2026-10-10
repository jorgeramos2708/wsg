using System.Management;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsServiceHealthCollector : IObservabilityCollector
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "EventLog", "RpcSs", "DcomLaunch", "PlugPlay", "Power", "Schedule", "Winmgmt"
    };

    public string Name => "Servicios";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(5);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var evidence = new List<EvidenceRecord>();
        var total = 0;
        var running = 0;
        var automatic = 0;
        var automaticStopped = 0;

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
        using var results = searcher.Get();

        foreach (ManagementObject service in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;

            var name = service["Name"]?.ToString() ?? string.Empty;
            var displayName = service["DisplayName"]?.ToString() ?? name;
            var state = service["State"]?.ToString() ?? "Unknown";
            var startMode = service["StartMode"]?.ToString() ?? "Unknown";
            var isRunning = string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase);
            var isAuto = string.Equals(startMode, "Auto", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(startMode, "Automatic", StringComparison.OrdinalIgnoreCase);

            if (isRunning) running++;
            if (isAuto) automatic++;
            if (isAuto && !isRunning) automaticStopped++;

            if (CriticalServices.Contains(name) && isAuto && !isRunning)
            {
                var key = $"service:critical:{Normalize(name)}";
                evidence.Add(new EvidenceRecord(
                    ObservationId(key, timestamp),
                    timestamp,
                    Name,
                    EvidenceCategory.Services,
                    displayName,
                    "Estado",
                    state,
                    string.Empty,
                    EvidenceSeverity.Warning,
                    DetectionConfidence.High,
                    key,
                    $"El servicio esencial {displayName} ({name}) no está en ejecución. WSG no intentará iniciarlo automáticamente."));
            }
        }

        var metrics = new List<MetricSample>
        {
            Metric("services.total", total, timestamp),
            Metric("services.running", running, timestamp),
            Metric("services.automatic", automatic, timestamp),
            Metric("services.automatic_stopped", automaticStopped, timestamp)
        };

        return CollectorResult.Success(Name, Cost, timestamp, evidence, metrics);
    }

    private static MetricSample Metric(string name, double value, DateTime timestamp) =>
        new($"metric:{name}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Services,
            "Servicios Windows",
            name,
            value,
            "count");

    private static string ObservationId(string prefix, DateTime timestamp) =>
        $"evidence:{prefix}:{timestamp:yyyyMMddHHmmssfffffff}";

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "service" : new string(chars).ToLowerInvariant();
    }
}
