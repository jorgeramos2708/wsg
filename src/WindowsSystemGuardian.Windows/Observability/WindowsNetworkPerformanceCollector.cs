using System.Net.NetworkInformation;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Observability;

public sealed class WindowsNetworkPerformanceCollector : IObservabilityCollector
{
    private readonly Dictionary<string, AdapterState> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public string Name => "Red";
    public DiagnosticCost Cost => DiagnosticCost.Low;
    public TimeSpan RecommendedInterval => TimeSpan.FromMinutes(1);

    public Task<CollectorResult> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private CollectorResult Collect(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var metrics = new List<MetricSample>();
        var evidence = new List<EvidenceRecord>();
        var current = new Dictionary<string, AdapterState>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            try
            {
                var stats = nic.GetIPv4Statistics();
                var id = nic.Id;
                var state = new AdapterState(
                    stats.BytesReceived + stats.BytesSent,
                    stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors,
                    stats.IncomingPacketsDiscarded + stats.OutgoingPacketsDiscarded,
                    timestamp);
                current[id] = state;

                var bytesPerSecond = 0d;
                var errorsPerSecond = 0d;
                var discardsPerSecond = 0d;
                if (TryGetPrevious(id, out var previous))
                {
                    var elapsed = Math.Max(0.001, (timestamp - previous.TimestampUtc).TotalSeconds);
                    bytesPerSecond = Math.Max(0d, (state.TotalBytes - previous.TotalBytes) / elapsed);
                    errorsPerSecond = Math.Max(0d, (state.Errors - previous.Errors) / elapsed);
                    discardsPerSecond = Math.Max(0d, (state.Discards - previous.Discards) / elapsed);
                }

                var utilization = nic.Speed <= 0
                    ? 0d
                    : Math.Clamp(bytesPerSecond * 8d / nic.Speed * 100d, 0d, 100d);
                var entity = string.IsNullOrWhiteSpace(nic.Name) ? nic.Description : nic.Name;

                metrics.Add(Metric(entity, "network.utilization.percent", utilization, "%", timestamp));
                metrics.Add(Metric(entity, "network.bytes_per_sec", bytesPerSecond, "bytes/s", timestamp));
                metrics.Add(Metric(entity, "network.errors_per_sec", errorsPerSecond, "errors/s", timestamp));
                metrics.Add(Metric(entity, "network.discards_per_sec", discardsPerSecond, "packets/s", timestamp));
                metrics.Add(Metric(entity, "network.link_speed_bps", nic.Speed, "bits/s", timestamp));

                if (errorsPerSecond >= 1 || discardsPerSecond >= 10)
                {
                    evidence.Add(new EvidenceRecord(
                        $"evidence:network-errors:{Normalize(id)}:{timestamp:yyyyMMddHHmmssfffffff}",
                        timestamp,
                        Name,
                        EvidenceCategory.Network,
                        entity,
                        "Errores/descartes",
                        $"{errorsPerSecond:F1}/{discardsPerSecond:F1}",
                        "por segundo",
                        EvidenceSeverity.Warning,
                        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.High,
                        $"network:errors:{Normalize(id)}",
                        $"La interfaz {entity} registró {errorsPerSecond:F1} errores/s y {discardsPerSecond:F1} descartes/s en el intervalo reciente."));
                }
            }
            catch
            {
                // Una NIC específica puede no exponer estadísticas IPv4.
            }
        }

        lock (_sync)
        {
            _previous.Clear();
            foreach (var pair in current)
                _previous[pair.Key] = pair.Value;
        }

        return CollectorResult.Success(Name, Cost, timestamp, evidence, metrics);
    }

    private bool TryGetPrevious(string id, out AdapterState state)
    {
        lock (_sync)
        {
            if (_previous.TryGetValue(id, out var found))
            {
                state = found;
                return true;
            }

            state = null!;
            return false;
        }
    }

    private static MetricSample Metric(string entity, string name, double value, string unit, DateTime timestamp) =>
        new($"metric:{name}:{Normalize(entity)}:{timestamp:yyyyMMddHHmmssfff}",
            timestamp,
            EvidenceCategory.Network,
            entity,
            name,
            value,
            unit);

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "adapter" : new string(chars).ToLowerInvariant();
    }

    private sealed record AdapterState(long TotalBytes, long Errors, long Discards, DateTime TimestampUtc);
}
