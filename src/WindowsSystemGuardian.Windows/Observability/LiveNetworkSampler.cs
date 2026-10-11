namespace WindowsSystemGuardian.Windows.Observability;

/// <summary>Muestra agregada de red para la vista en vivo. Valores null = no disponible.</summary>
public sealed record LiveNetworkSample(
    DateTime TimestampUtc,
    double? BytesReceivedPerSec,
    double? BytesSentPerSec);

/// <summary>
/// Muestreador de solo lectura para el monitoreo en vivo de red. Consulta la clase
/// de rendimiento formateada Win32_PerfFormattedData_Tcpip_NetworkInterface y agrega
/// la instancia _Total (bytes recibidos/enviados por segundo). No modifica la red.
/// </summary>
public sealed class LiveNetworkSampler
{
    public LiveNetworkSample Sample()
    {
        var timestamp = DateTime.UtcNow;
        double? received = null;
        double? sent = null;

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface WHERE Name='_Total'");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject item in results)
            {
                received = ReadDouble(item, "BytesReceivedPersec");
                sent = ReadDouble(item, "BytesSentPersec");
            }
        }
        catch
        {
            // Sin muestra de red esta ronda: la UI conserva la anterior (fail-closed).
        }

        return new LiveNetworkSample(timestamp, received, sent);
    }

    private static double? ReadDouble(System.Management.ManagementObject item, string property)
    {
        try
        {
            var value = item[property];
            return value is null ? null : Convert.ToDouble(value);
        }
        catch
        {
            return null;
        }
    }
}
