namespace WindowsSystemGuardian.Core.Monitoring;

/// <summary>
/// Umbrales de monitoreo en vivo, centralizados para UI y pruebas.
///
/// - Memoria y commit: aviso ≥ 90 % — coincide con los umbrales de evidencia del
///   collector desde 0.4.1 y con la semántica de presión de memoria de
///   GlobalMemoryStatusEx (MemoryLoad / commit vs límite, sysinfoapi.h).
/// - CPU (sostenida) y actividad de disco: defaults de WSG alineados con la guía de
///   rendimiento de Microsoft para Windows Server; no existe una página de Learn que
///   fije un número único, por lo que quedan documentados aquí como barra de calidad
///   del producto (docs/AUDIT_MSDOC_0.11.0.md).
/// </summary>
public static class MonitoringThresholds
{
    public const double CpuWarningPercent = 80.0;
    public const double CpuCriticalPercent = 90.0;
    public const double MemoryWarningPercent = 90.0;
    public const double MemoryCriticalPercent = 95.0;
    public const double CommitWarningPercent = 90.0;
    public const double CommitCriticalPercent = 95.0;
    public const double DiskActivityWarningPercent = 85.0;
    public const double DiskActivityCriticalPercent = 95.0;

    /// <summary>Número de muestras del histórico en vivo (2 s por muestra ≈ 2 minutos).</summary>
    public const int HistoryCapacity = 60;
}
