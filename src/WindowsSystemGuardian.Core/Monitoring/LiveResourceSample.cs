namespace WindowsSystemGuardian.Core.Monitoring;

/// <summary>
/// Muestra de recursos del sistema para el monitoreo en vivo de la UI. Es una vista
/// de lectura de las métricas que ya produce el collector de rendimiento; las muestras
/// vivas no se persisten en el store de evidencia (la cadencia de evidencia es la del
/// pipeline normal).
/// </summary>
public sealed record LiveResourceSample(
    DateTime CapturedAtUtc,
    double CpuPercent,
    double MemoryLoadPercent,
    double MemoryTotalBytes,
    double MemoryAvailableBytes,
    double CommitPercent,
    double UptimeSeconds);

/// <summary>
/// Mapea el resultado del collector de rendimiento a una muestra viva. Fail-closed:
/// si el collector falló o falta cualquiera de las métricas requeridas devuelve null
/// (la UI conserva la última muestra válida).
/// </summary>
public static class LiveResourceSampleReader
{
    public static LiveResourceSample? FromCollectorResult(Contracts.Observability.CollectorResult? result)
    {
        if (result is null || !result.Succeeded)
            return null;

        double? Read(string name) => result.Metrics
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal)) is { } metric
            ? metric.Value
            : null;

        var cpu = Read("system.cpu.percent");
        var memoryLoad = Read("system.memory.percent");
        var memoryTotal = Read("system.memory.total_bytes");
        var memoryAvailable = Read("system.memory.available_bytes");
        var commit = Read("system.commit.percent");
        var uptime = Read("system.uptime.seconds");

        if (cpu is null || memoryLoad is null || memoryTotal is null || memoryAvailable is null || commit is null || uptime is null)
            return null;

        return new LiveResourceSample(
            result.CapturedAtUtc,
            cpu.Value,
            memoryLoad.Value,
            memoryTotal.Value,
            memoryAvailable.Value,
            commit.Value,
            uptime.Value);
    }
}
