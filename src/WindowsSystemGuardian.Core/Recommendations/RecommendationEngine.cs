using WindowsSystemGuardian.Contracts.Incidents;

namespace WindowsSystemGuardian.Core.Recommendations;

/// <summary>
/// Produce planes de resolución. En 0.4.1 todas las acciones son recomendadas;
/// ninguna se ejecuta desde este motor.
/// </summary>
public sealed class RecommendationEngine
{
    public IReadOnlyList<RecommendationStep> Build(string causeKind) => causeKind switch
    {
        "MemoryPressureByProcess" or "MemoryPressureWithMultipleProcesses" or "MemoryPressure" or "ProcessMemoryGrowth" =>
        [
            Step(1, "Confirmar consumidor de memoria", "Revisar memoria privada/working set, crecimiento y función del proceso antes de actuar.", RecommendationRisk.Informational, false, false, false, "La presión debe poder atribuirse o descartarse con nuevas muestras."),
            Step(2, "Revisar commit y paginación", "Confirmar que el problema no sea únicamente cache legítima y evaluar presión del límite de commit.", RecommendationRisk.Informational, false, false, false, "Commit y memoria vuelven a un rango estable."),
            Step(3, "Aplicar remediación del propietario", "Si se confirma una aplicación/servicio defectuoso, usar su procedimiento soportado de reinicio, configuración o actualización.", RecommendationRisk.Medium, true, false, false, "El consumo deja de crecer y los servicios dependientes permanecen estables.")
        ],
        "CpuSaturationByProcess" or "CpuSaturationWithMultipleProcesses" or "CpuPressure" or "ProcessCpuLoad" =>
        [
            Step(1, "Confirmar proceso responsable", "Correlacionar CPU por proceso con servicios, tareas, Windows Update y la carga esperada.", RecommendationRisk.Informational, false, false, false, "Se identifica una carga concreta o la saturación desaparece."),
            Step(2, "Revisar eventos del mismo intervalo", "Buscar fallos, timeouts o servicing que expliquen el pico sostenido.", RecommendationRisk.Informational, false, false, false, "No aparecen nuevos errores relacionados."),
            Step(3, "Remediar el componente identificado", "Usar el procedimiento soportado de la aplicación/servicio; no finalizar procesos críticos de forma automática.", RecommendationRisk.Medium, true, false, false, "CPU global y del proceso vuelven a la línea base.")
        ],
        "StorageIoReliability" or "StorageReliability" =>
        [
            Step(1, "Proteger datos y confirmar salud", "Priorizar respaldo/estado de almacenamiento cuando existen errores de E/S o filesystem.", RecommendationRisk.Low, false, false, false, "No aparecen nuevos eventos de E/S y el volumen permanece accesible."),
            Step(2, "Ejecutar diagnóstico de filesystem soportado", "Iniciar primero una comprobación de solo lectura. Reparaciones offline requieren mantenimiento explícito.", RecommendationRisk.Medium, true, true, false, "La comprobación reporta filesystem saludable o identifica una reparación concreta."),
            Step(3, "Revalidar carga y capacidad", "Comprobar espacio, tiempo ocupado y eventos después del diagnóstico.", RecommendationRisk.Informational, false, false, false, "Capacidad y E/S permanecen estables sin nuevos eventos críticos.")
        ],
        "StoragePressure" =>
        [
            Step(1, "Identificar el crecimiento", "Determinar directorios/procesos responsables antes de eliminar datos.", RecommendationRisk.Informational, false, false, false, "Se identifica la fuente principal del crecimiento."),
            Step(2, "Recuperar únicamente espacio SAFE", "Usar el análisis de limpieza WSG y mantener protegidos datos de aplicaciones/roles.", RecommendationRisk.Low, true, false, false, "Aumenta el espacio libre sin errores de aplicaciones o Windows."),
            Step(3, "Verificar tendencia", "Confirmar que el volumen ya no pierde espacio a ritmo anormal.", RecommendationRisk.Informational, false, false, false, "La pendiente de crecimiento vuelve a normalidad.")
        ],
        "HardwareInstability" or "HardwareError" =>
        [
            Step(1, "Preservar evidencia", "Conservar eventos WHEA/BugCheck y no ejecutar optimizaciones destructivas.", RecommendationRisk.Informational, false, false, false, "La evidencia queda disponible para diagnóstico."),
            Step(2, "Validar hardware/firmware", "Revisar firmware, controladores y diagnóstico del fabricante/hipervisor según corresponda.", RecommendationRisk.Medium, true, true, false, "No se reproducen WHEA/BugCheck tras la corrección soportada."),
            Step(3, "Revisar estabilidad", "Monitorizar durante una ventana suficiente antes de cerrar el incidente.", RecommendationRisk.Informational, false, false, false, "No existen nuevos errores de hardware durante la ventana de verificación.")
        ],
        "ProcessResourceGrowth" =>
        [
            Step(1, "Confirmar crecimiento sostenido", "Verificar que handles/hilos continúan creciendo durante una ventana suficiente y que no se trata de una carga temporal.", RecommendationRisk.Informational, false, false, false, "El contador se estabiliza o continúa creciendo de forma reproducible."),
            Step(2, "Identificar componente propietario", "Relacionar el proceso con servicio/aplicación, logs y versión antes de aplicar cambios.", RecommendationRisk.Informational, false, false, false, "Se identifica el componente y su comportamiento esperado."),
            Step(3, "Aplicar corrección soportada", "Actualizar, reconfigurar o reiniciar solo mediante el procedimiento soportado del componente.", RecommendationRisk.Medium, true, false, false, "Handles/hilos vuelven a una línea base estable y no reaparece el incidente.")
        ],
        "ServiceFailure" =>
        [
            Step(1, "Analizar dependencias y eventos", "Confirmar por qué el servicio se detuvo antes de iniciarlo.", RecommendationRisk.Informational, false, false, false, "Se identifica el fallo o se descartan dependencias."),
            Step(2, "Aplicar corrección soportada", "Corregir configuración, dependencia o componente propietario antes de reiniciar el servicio.", RecommendationRisk.Medium, true, false, false, "El servicio inicia y permanece estable."),
            Step(3, "Verificar recurrencia", "Comprobar Service Control Manager y estado del servicio en capturas posteriores.", RecommendationRisk.Informational, false, false, false, "No aparecen nuevos fallos del servicio.")
        ],
        "NetworkPressure" =>
        [
            Step(1, "Validar interfaz y errores", "Revisar velocidad, errores, descartes y si el tráfico es esperado.", RecommendationRisk.Informational, false, false, false, "Errores/descartes se mantienen en cero o nivel normal."),
            Step(2, "Identificar origen del tráfico", "Correlacionar con backups, transferencias, aplicaciones o mantenimiento.", RecommendationRisk.Informational, false, false, false, "La carga se atribuye a un origen conocido."),
            Step(3, "Revalidar después de la corrección", "Confirmar utilización y conectividad tras el ajuste soportado.", RecommendationRisk.Informational, false, false, false, "La interfaz ya no permanece saturada." )
        ],
        "FileSystemIntegrity" =>
        [
            Step(1, "Confirmar el volumen afectado", "Correlacionar dirty bit/estado WMI con eventos NTFS/Disk y, si procede, ejecutar un escaneo online de solo lectura.", RecommendationRisk.Informational, true, false, false, "El volumen puede comprobarse sin nuevos errores de filesystem."),
            Step(2, "Preparar mantenimiento si se requiere reparación", "Si el escaneo confirma corrupción, planear Repair-Volume/CHKDSK soportado durante una ventana de mantenimiento. WSG 0.4.1 no ejecuta la reparación.", RecommendationRisk.Medium, true, true, false, "Existe una acción de reparación concreta y aprobada."),
            Step(3, "Revalidar filesystem y eventos", "Después de la futura corrección, repetir dirty bit/scan y revisar NTFS/Disk.", RecommendationRisk.Informational, false, false, false, "Filesystem saludable y sin nuevos eventos de integridad.")
        ],
        "ComponentStoreCorruption" or "ComponentStoreNonRepairable" =>
        [
            Step(1, "Confirmar Component Store", "Ejecutar DISM /ScanHealth si solo existe CheckHealth o evidencia preliminar.", RecommendationRisk.Low, true, false, false, "DISM clasifica la imagen como saludable, reparable o no reparable."),
            Step(2, "Definir fuente de reparación", "Si la imagen es reparable, validar Windows Update o una fuente compatible antes de cualquier RestoreHealth.", RecommendationRisk.Medium, true, false, false, "La fuente de reparación y versión coinciden con el sistema."),
            Step(3, "Reparar en fase controlada", "DISM /RestoreHealth seguirá bloqueado hasta el Remediation Planner con aprobación explícita.", RecommendationRisk.Medium, true, false, false, "Component Store vuelve a Healthy y SFC puede verificarse.")
        ],
        "SystemFileIntegrity" =>
        [
            Step(1, "Confirmar archivos protegidos", "Repetir SFC /verifyonly y correlacionar con Component Store/CBS antes de reparar.", RecommendationRisk.Informational, true, false, false, "SFC confirma o descarta violaciones de integridad."),
            Step(2, "Validar Component Store", "Si existen violaciones, comprobar DISM antes de usar SFC /scannow.", RecommendationRisk.Low, true, false, false, "El Component Store está saludable o tiene un plan de reparación."),
            Step(3, "Reparación posterior controlada", "SFC /scannow no se ejecuta en 0.4.1; requerirá aprobación y verificación posterior.", RecommendationRisk.Medium, true, false, false, "SFC /verifyonly termina sin violaciones después de la futura reparación.")
        ],
        "WindowsUpdateFailure" =>
        [
            Step(1, "Revisar historial y HRESULT", "Identificar updates fallidos, repetición y códigos antes de resetear componentes o borrar caches.", RecommendationRisk.Informational, false, false, false, "Se identifica el update/código que falla."),
            Step(2, "Correlacionar servicing", "Comprobar Component Store, reinicio pendiente, espacio y eventos antes de modificar Windows Update.", RecommendationRisk.Informational, false, false, false, "Existe una causa probable respaldada por evidencia."),
            Step(3, "Aplicar solo procedimiento soportado", "No borrar SoftwareDistribution/Catroot2 de forma automática. La remediación futura debe ser específica a la causa.", RecommendationRisk.Medium, true, true, false, "Windows Update completa una comprobación/instalación sin nuevos fallos.")
        ],
        "PendingServicingReboot" =>
        [
            Step(1, "Confirmar ventana de mantenimiento", "Validar que el reinicio pendiente proviene de Windows Update y que no existen cargas críticas activas.", RecommendationRisk.Informational, false, false, false, "Se confirma que el reinicio es apropiado."),
            Step(2, "Programar reinicio administrado", "WSG no reinicia automáticamente; debe respetarse la ventana de mantenimiento y roles del servidor.", RecommendationRisk.Medium, true, true, false, "Después del reinicio WUA ya no indica reboot required."),
            Step(3, "Revalidar servicing", "Comprobar Windows Update, servicios e incidentes después del arranque.", RecommendationRisk.Informational, false, false, false, "No quedan operaciones de servicing pendientes.")
        ],
        "RecoveryEnvironmentUnavailable" =>
        [
            Step(1, "Validar estado WinRE", "Confirmar con REAgentC /info la configuración y ubicación de recuperación.", RecommendationRisk.Informational, true, false, false, "WinRE muestra estado y ubicación coherentes."),
            Step(2, "No modificar BCD automáticamente", "Cualquier habilitación/reconfiguración de WinRE se realizará solo con procedimiento explícito y respaldado.", RecommendationRisk.Medium, true, true, false, "WinRE queda habilitado y accesible después de la futura corrección.")
        ],
        "VssWriterFailure" =>
        [
            Step(1, "Identificar writers afectados", "Revisar writers fuera de Stable y eventos VSS de la misma ventana.", RecommendationRisk.Informational, true, false, false, "Se identifica el writer y su componente propietario."),
            Step(2, "Corregir el componente propietario", "No reiniciar todos los servicios VSS genéricamente; aplicar el procedimiento soportado de la aplicación/writer.", RecommendationRisk.Medium, true, false, false, "Todos los writers necesarios vuelven a Stable."),
            Step(3, "Verificar preparación de respaldo", "Repetir vssadmin list writers y validar el sistema de backup real.", RecommendationRisk.Informational, false, false, false, "Writers estables y sin nuevos eventos VSS.")
        ],
        _ =>
        [
            Step(1, "Revisar evidencia correlacionada", "Analizar los hallazgos y eventos del mismo periodo antes de modificar Windows.", RecommendationRisk.Informational, false, false, false, "La causa queda confirmada o se recopila evidencia adicional."),
            Step(2, "Aplicar solo una corrección soportada", "No usar cambios destructivos ni optimizaciones genéricas sin una causa identificada.", RecommendationRisk.Medium, true, false, false, "El síntoma desaparece sin introducir nuevos hallazgos."),
            Step(3, "Verificar resultado", "Repetir las comprobaciones que originaron el incidente.", RecommendationRisk.Informational, false, false, false, "Las condiciones del incidente dejan de estar presentes.")
        ]
    };

    private static RecommendationStep Step(
        int order,
        string title,
        string detail,
        RecommendationRisk risk,
        bool elevation,
        bool restart,
        bool automation,
        string verification) =>
        new(order, title, detail, risk, elevation, restart, automation, verification);
}
