namespace WindowsSystemGuardian.Core.Monitoring;

/// <summary>
/// Muestra cruda por proceso para el monitoreo en vivo. La recolección (Process,
/// capa Windows) es solo lectura; la interpretación vive en <see cref="ProcessLiveClassifier"/>.
/// </summary>
public sealed record ProcessLiveSampleRecord(
    int ProcessId,
    string Name,
    double TotalCpuSeconds,
    long WorkingSetBytes,
    long PrivateBytes,
    DateTime TimestampUtc);

/// <summary>Estado previo de CPU por identidad de proceso (pid + inicio) para el delta.</summary>
public sealed record ProcessCpuBaseline(
    double TotalCpuSeconds,
    DateTime TimestampUtc);

/// <summary>Consumo calculado de un proceso para la vista en vivo.</summary>
public sealed record LiveProcessConsumption(
    int ProcessId,
    string Name,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    IReadOnlyList<string> ServiceNames,
    bool CpuSignificant,
    bool MemorySignificant);

/// <summary>
/// Clasificador puro de consumidores significativos en tiempo real. La CPU por proceso
/// se calcula como delta de <c>TotalProcessorTime</c> entre muestras dividido por el
/// tiempo transcurrido y por el número de procesadores lógicos (×100, acotado 0-100) —
/// la misma semántica de % Processor Time de los contadores de rendimiento de Windows.
/// Un proceso es "significativo" cuando supera los umbrales de CPU o memoria; los
/// top-N se devuelven igualmente para contexto. Fail-closed: sin muestra previa el
/// proceso se reporta con CPU 0 (no hay delta inventado).
/// </summary>
public static class ProcessLiveClassifier
{
    /// <summary>Umbral de CPU (por núcleo lógico) que marca un proceso como significativo.</summary>
    public const double SignificantCpuPercent = 10.0;

    /// <summary>Umbral de memoria (working set) que marca un proceso como significativo.</summary>
    public const long SignificantMemoryBytes = 200L * 1024L * 1024L;

    public static double ComputeCpuPercent(
        double previousCpuSeconds,
        DateTime previousTimestampUtc,
        double currentCpuSeconds,
        DateTime currentTimestampUtc,
        int processorCount)
    {
        if (processorCount <= 0)
            return 0;

        var cpuDelta = currentCpuSeconds - previousCpuSeconds;
        var elapsed = currentTimestampUtc - previousTimestampUtc;
        if (cpuDelta <= 0 || elapsed <= TimeSpan.Zero)
            return 0;

        var percent = cpuDelta / elapsed.TotalSeconds / processorCount * 100.0;
        return Math.Clamp(percent, 0, 100);
    }

    public static IReadOnlyList<LiveProcessConsumption> Classify(
        IReadOnlyList<ProcessLiveSampleRecord> current,
        IReadOnlyDictionary<long, ProcessCpuBaseline> previousCpuByProcess,
        int processorCount,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? servicesByProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previousCpuByProcess);

        var result = new List<LiveProcessConsumption>(current.Count);
        foreach (var record in current)
        {
            // Línea base por pid: el sampler garantiza una sola fila por pid por muestra.
            var hasBaseline = previousCpuByProcess.TryGetValue(record.ProcessId, out var baseline);
            var cpuPercent = hasBaseline
                ? ComputeCpuPercent(
                    baseline!.TotalCpuSeconds,
                    baseline.TimestampUtc,
                    record.TotalCpuSeconds,
                    record.TimestampUtc,
                    processorCount)
                : 0;

            var services = servicesByProcessId is not null &&
                           servicesByProcessId.TryGetValue(record.ProcessId, out var names)
                ? names
                : Array.Empty<string>();

            result.Add(new LiveProcessConsumption(
                record.ProcessId,
                record.Name,
                cpuPercent,
                record.WorkingSetBytes,
                record.PrivateBytes,
                services,
                CpuSignificant: cpuPercent >= SignificantCpuPercent,
                MemorySignificant: record.WorkingSetBytes >= SignificantMemoryBytes));
        }

        return result;
    }

    /// <summary>Ordena por CPU y memoria y toma los top-N sin duplicados (fusión de rankings).</summary>
    public static IReadOnlyList<LiveProcessConsumption> MergeTopConsumers(
        IReadOnlyList<LiveProcessConsumption> consumption,
        int topN)
    {
        var limit = Math.Clamp(topN, 1, 50);
        return consumption
            .OrderByDescending(c => c.CpuPercent)
            .Take(limit)
            .Concat(consumption
                .OrderByDescending(c => c.WorkingSetBytes)
                .Take(limit))
            .Distinct()
            .OrderByDescending(c => c.CpuPercent)
            .ThenByDescending(c => c.WorkingSetBytes)
            .Take(limit * 2)
            .ToList();
    }
}
