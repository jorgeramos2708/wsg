# Auditoría contra documentación oficial de Microsoft — WSG 0.10.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva (discos en vivo, claridad del commit, retiro de la tarjeta duplicada "Salud del sistema"). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías 0.5.0-0.9.0.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0/0.6.0/0.7.0/0.8.0/0.9.0.

## 1. Superficie nueva de 0.10.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| Discos en vivo = `Win32_PerfFormattedData_PerfDisk_LogicalDisk` (`PercentDiskTime`, `DiskBytesPersec`, instancia `_Total`) | [Win32_PerfFormattedData_PerfDisk_LogicalDisk (WMI)](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisdk/win32-perfformatteddata-perfdisk-logicaldisk) | ✅ OK — clase formateada oficial: "the formatted data class that provides high-performance access to performance counter data"; los valores llegan pre-calculados por el proveedor (CorrectedData), no se requieren deltas entre muestras |
| `% Disk Time` puede superar 100 % en algunos escenarios → WSG lo acota con `Math.Clamp(0,100)` | Documentación del contador (PercentDiskTime refleja tiempo ocupado; puede exceder 100 % en discos con múltiples colas) | ✅ OK — el clamp evita barreras >100 % en la UI sin ocultar actividad (el throughput bytes/s se muestra aparte) |
| El collector programado de 0.4.1 usa la misma clase WMI (con exclusión de `_Total` para métricas por volumen) y el sampler en vivo usa `_Total` (agregado) | Misma clase WMI | ✅ OK — coherente: por volumen para diagnóstico, agregado para vista de sistema |
| "Commit" = memoria virtual comprometida (RAM + archivo de paginación), límite = RAM + pagefile | [GlobalMemoryStatusEx (sysinfoapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex): `TotalPageFile`/`AvailPageFile` — "the current committed memory limit... is the sum of the physical memory and the paging file" | ✅ OK — la explicación añadida como tooltip en la UI y el texto del VM coinciden con la doc; el collector ya leía TotalPageFile/AvailPageFile (0.4.1) |
| Retiro de la tarjeta "Salud del sistema" del Dashboard | Decisión de UI del operador | ✅ OK — eliminaba duplicación: CPU/RAM congeladas del snapshot vs. la tarjeta en vivo de 0.7.0; el "Tiempo activo" se conservó (ahora vive en la tarjeta en vivo desde la métrica `system.uptime.seconds` existente); "Almacenamiento" (score) permanece en la vista Almacenamiento y en Estado del sistema |
| Muestras vivas siguen sin escribirse al store | Herencia de 0.7.0 | ✅ OK |

## 2. Hallazgos del ciclo

### OK — Discos en vivo con la clase de rendimiento formateada oficial

`Win32_PerfFormattedData_PerfDisk_LogicalDisk` es la clase WMI de datos formateados de alto rendimiento (proveedor pre-calculado): a diferencia de las clases raw, no exige dos muestras para derivar tasas. El sampler lee solo lectura y agrega `_Total`; la exclusión de volúmenes individuales mantiene la vista de sistema simple. El collector programado de 0.4.1 (por volumen) no se toca.

### OK — El "commit" ahora está explicado en la UI y coincide con la doc

El tooltip declara: memoria virtual garantizada a los procesos, respaldada por RAM + archivo de paginación, límite = RAM + pagefile — semántica de `GlobalMemoryStatusEx` (`TotalPageFile`/`AvailPageFile`), que es la fuente que el collector usa desde 0.4.1. La etiqueta pasó de "Commit" a "Memoria comprometida" para lectores no técnicos.

### OK — Retiro de tarjeta congelada elimina la contradicción de la UI

La tarjeta retirada mostraba CPU/RAM del último snapshot (estático) junto a la tarjeta en vivo: dos valores de la misma métrica con distinta antigüedad = fuente de confusión. El score de almacenamiento y el uptime conservan visibilidad (vista Almacenamiento / tarjeta en vivo respectivamente).

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS; live monitoring, consumidores significativos, remediación (3 acciones) y verificación integrada sin cambios en su frontera de seguridad.

## 3. Fuentes

- [Win32_PerfFormattedData_PerfDisk_LogicalDisk (WMI)](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisdk/win32-perfformatteddata-perfdisk-logicaldisk)
- [GlobalMemoryStatusEx (sysinfoapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) — semántica de commit (TotalPageFile/AvailPageFile)
- Auditorías heredadas: 0.5.0-0.9.0 (docs/)

## 4. Conclusión

Los tres cambios del ciclo quedan validados: la clase WMI de discos es la oficial de datos formateados (sin deltas necesarios), la explicación del commit coincide con la semántica documentada de `GlobalMemoryStatusEx`, y el retiro de la tarjeta congelada elimina una contradicción de la UI sin pérdida de información (uptime y score conservan visibilidad). Sin datos erróneos ni falsos positivos conocidos.
