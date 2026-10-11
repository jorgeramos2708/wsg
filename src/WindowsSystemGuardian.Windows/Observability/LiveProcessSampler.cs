using System.Diagnostics;
using System.Management;
using WindowsSystemGuardian.Core.Monitoring;

namespace WindowsSystemGuardian.Windows.Observability;

/// <summary>
/// Muestreador de solo lectura para el monitoreo en vivo de procesos. Usa la API
/// oficial System.Diagnostics.Process (TotalProcessorTime, WorkingSet64,
/// PrivateMemorySize64) y atribuye procesos que alojan servicios mediante la clase
/// WMI oficial Win32_Service (ProcessId), con caché de 30 segundos. Mantiene la
/// línea base de CPU en memoria para el cálculo por delta; no modifica ningún proceso.
/// </summary>
public sealed class LiveProcessSampler
{
    private const int ServiceMapCacheSeconds = 30;

    private readonly Dictionary<long, ProcessCpuBaseline> _previousCpu = [];
    private readonly object _sync = new();
    private IReadOnlyDictionary<int, IReadOnlyList<string>> _servicesByPid =
        new Dictionary<int, IReadOnlyList<string>>();
    private DateTime _servicesLoadedUtc = DateTime.MinValue;

    /// <summary>
    /// Toma una muestra y clasifica los consumidores. En el primer tick no hay línea
    /// base de CPU: los procesos se reportan con CPU 0 (sin delta inventado) y quedan
    /// listos para el siguiente tick.
    /// </summary>
    public IReadOnlyList<LiveProcessConsumption> Sample(int processorCount, int topN)
    {
        var now = DateTime.UtcNow;
        var current = new List<ProcessLiveSampleRecord>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    current.Add(new ProcessLiveSampleRecord(
                        process.Id,
                        string.IsNullOrWhiteSpace(process.ProcessName) ? $"PID {process.Id}" : process.ProcessName,
                        process.TotalProcessorTime.TotalSeconds,
                        process.WorkingSet64,
                        process.PrivateMemorySize64,
                        now));
                }
                catch
                {
                    // Procesos protegidos/efímeros pueden desaparecer o negar acceso.
                }
            }
        }

        IReadOnlyDictionary<long, ProcessCpuBaseline> baseline;
        lock (_sync)
        {
            baseline = new Dictionary<long, ProcessCpuBaseline>(_previousCpu);
            _previousCpu.Clear();
            foreach (var record in current)
                _previousCpu[record.ProcessId] = new ProcessCpuBaseline(record.TotalCpuSeconds, now);
        }

        var services = GetServicesByProcessIdCached(now);
        var classified = ProcessLiveClassifier.Classify(current, baseline, processorCount, services);
        return ProcessLiveClassifier.MergeTopConsumers(classified, topN);
    }

    private IReadOnlyDictionary<int, IReadOnlyList<string>> GetServicesByProcessIdCached(DateTime now)
    {
        lock (_sync)
        {
            if ((now - _servicesLoadedUtc).TotalSeconds < ServiceMapCacheSeconds &&
                _servicesByPid.Count > 0)
                return _servicesByPid;
        }

        try
        {
            var map = new Dictionary<int, List<string>>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, ProcessId, State FROM Win32_Service WHERE ProcessId != 0");
            using var results = searcher.Get();
            foreach (ManagementObject service in results)
            {
                var pid = Convert.ToInt32(service["ProcessId"]);
                var name = service["Name"]?.ToString();
                if (pid <= 0 || string.IsNullOrWhiteSpace(name))
                    continue;

                if (!map.TryGetValue(pid, out var list))
                    map[pid] = list = [];
                list.Add(name);
            }

            lock (_sync)
            {
                _servicesByPid = map.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value);
                _servicesLoadedUtc = now;
            }
        }
        catch
        {
            // Sin atribución de servicios esta ronda: los procesos se muestran igualmente.
        }

        lock (_sync)
            return _servicesByPid;
    }
}
