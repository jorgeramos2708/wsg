# Auditoría contra documentación oficial de Microsoft — WSG 0.14.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva (red en vivo, capacidad de discos con %, correlación automática). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías 0.5.0-0.13.1.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0-0.13.1.

## 1. Superficie nueva de 0.14.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| Red en vivo = `Win32_PerfFormattedData_Tcpip_NetworkInterface` (`BytesReceivedPersec`, `BytesSentPersec`, instancia `_Total`) | [Win32_PerfFormattedData_Tcpip_NetworkInterface (WMI)](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisdk/win32-perfformatteddata-tcpip-networkinterface) | ✅ OK — clase formateada oficial de rendimiento de red; los nombres de contadores corresponden a los del proveedor Tcpip |
| `BytesReceivedPersec` / `BytesSentPersec` | [Win32_PerfFormattedData_Tcpip_NetworkInterface — propiedades](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisdk/win32-perfformatteddata-tcpip-networkinterface) | ✅ OK — contadores oficiales de bytes por segundo en cada dirección |
| `AutoMaximum` en sparkline: escala Y al mayor valor del histórico para métricas sin techo (throughput de red) | Decisión de diseño (throughput no tiene un máximo fijo como CPU/Memoria que se acotan a 100 %) | ✅ OK — sin falsos techos que hagan la gráfica plana |
| `FreePercent` = `FreeSpace / Size × 100` | [Win32_LogicalDisk — FreeSpace, Size](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisk) (revalidado por herencia 0.10.0) | ✅ OK |
| Correlación automática: `DispatcherTimer` 30 s → `CorrelateAsync` (Dashboard + Incidents + Integrity.Quick) | [DispatcherTimer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer) (revalidado de 0.7.0) | ✅ OK — cadencia de correlación razonable entre respuesta y costo; los collectors de costo High/manual NO se ejecutan automáticamente (solo Low/Medium/VeryLow) |
| `IntegrityViewModel.RefreshQuickAsync()` | Método existente desde 0.4.1 | ✅ OK — no ejecuta scans profundos (High), solo collectors de bajo costo |

## 2. Hallazgos del ciclo

### OK — Red en vivo con clase formateada oficial

El patrón es idéntico al del sampler de discos (0.10.0): clase formateada WMI, instancia `_Total`, valores pre-calculados por el proveedor. La sparkline usa `AutoMaximum` para escalar la Y al máximo del histórico (throughput no tiene techo fijo).

### OK — Correlación automática sin ejecutar diagnósticos High

`CorrelateAsync` usa `ObservabilityService.CaptureAsync(DiagnosticCost.Medium)` (heredado de 0.4.1), que solo ejecuta collectors Low/Medium/VeryLow. Los High (SFC verifyonly, DISM ScanHealth, CHKDSK scan) permanecen manuales. La cadencia de 30 s es la misma del scheduler recomendado de la mayoría de collectors Medium.

### OK — Capacidad de discos con % disponible

Se añadió `FreePercent` junto a `UsedPercent`; el texto ahora muestra ambos porcentajes ("X GB usados (Y %)" · "Z GB libres (W %)"), lo que hace la medición más legible sin hover.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Todas las superficies previas sin cambios.

## 3. Fuentes

- [Win32_PerfFormattedData_Tcpip_NetworkInterface (WMI)](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisdk/win32-perfformatteddata-tcpip-networkinterface)
- [DispatcherTimer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer) (revalidado)
- [Win32_LogicalDisk (WMI)](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisk) (revalidado)
- Auditorías heredadas: 0.5.0-0.13.1 (docs/)

## 4. Conclusión

La superficie nueva es **solo lectura, usa la misma clase de datos formateados oficial que los discos, y la correlación automática respeta la frontera de costos del scheduler existente**. Sin datos erróneos ni falsos positivos conocidos. Deudas documentales heredadas sin cambios.
