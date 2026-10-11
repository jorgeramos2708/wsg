using System.Management;

namespace WindowsSystemGuardian.Windows.Observability;

/// <summary>
/// Muestreador de solo lectura para el monitoreo en vivo de discos. Consulta la clase
/// de rendimiento formateada Win32_PerfFormattedData_PerfDisk_LogicalDisk (los valores
/// vienen ya calculados por el proveedor de contadores: no se requiere delta entre
/// muestras) y agrega la instancia _Total de todos los discos lógicos. No modifica
/// ningún volumen.
/// </summary>
public sealed class LiveDiskSampler
{
    public LiveDiskSample Sample()
    {
        var timestamp = DateTime.UtcNow;
        double busyPercent = 0;
        double bytesPerSec = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PercentDiskTime, DiskBytesPersec FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name='_Total'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                busyPercent = Math.Clamp(ReadDouble(item, "PercentDiskTime"), 0d, 100d);
                bytesPerSec = Math.Max(0d, ReadDouble(item, "DiskBytesPersec"));
            }
        }
        catch
        {
            // Sin muestra de discos esta ronda: la UI conserva la anterior (fail-closed).
            return new LiveDiskSample(timestamp, null, null);
        }

        return new LiveDiskSample(timestamp, busyPercent, bytesPerSec);
    }

    private static double ReadDouble(ManagementObject item, string property)
    {
        try
        {
            var value = item[property];
            return value is null ? 0d : Convert.ToDouble(value);
        }
        catch
        {
            return 0d;
        }
    }
}

/// <summary>Muestra agregada de discos para la vista en vivo. Valores null = no disponible.</summary>
public sealed record LiveDiskSample(
    DateTime TimestampUtc,
    double? BusyPercent,
    double? BytesPerSec);
