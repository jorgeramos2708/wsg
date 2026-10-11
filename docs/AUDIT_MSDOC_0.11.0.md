# Auditoría contra documentación oficial de Microsoft — WSG 0.11.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva (sparklines con umbrales, capacidad de discos en vivo, retiro de textos). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías 0.5.0-0.10.0.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0/0.6.0/0.7.0/0.8.0/0.9.0/0.10.0.

## 1. Superficie nueva de 0.11.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| Sparkline renderizada con `DrawingContext` en `OnRender` (área bajo curva, línea de serie, líneas de umbral) | [FrameworkElement.OnRender / DrawingContext (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkelement.onrender) — modelo de render inmediato de WPF; `DrawLine`/`DrawGeometry` son las primitivas documentadas | ✅ OK — sin dependencias de charting (cero superficie de supply-chain); `Freeze()` en geometrías y pinceles para rendimiento (práctica documentada de WPF) |
| `DependencyProperty` con `FrameworkPropertyMetadataOptions.AffectsRender` para invalidar el render ante cambios | [FrameworkPropertyMetadataOptions.AffectsRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkpropertymetadataoptions) — "the property affects rendering" | ✅ OK — cada nuevo histórico (lista nueva por tick) invalida el render del control |
| Umbrales: RAM/commit aviso ≥ 90 % (coincide con los umbrales de evidencia del collector desde 0.4.1 y con la semántica de presión de GlobalMemoryStatusEx) | [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) (MemoryLoad, commit vs límite) | ✅ OK — consistente con el código de evidencia existente (`LoadPercent ≥ 90`, `commitPercent ≥ 90` desde 0.4.1) |
| Umbrales: CPU aviso ≥ 80 % / crítico ≥ 90 % (sostenido); discos aviso ≥ 85 % | Guía de rendimiento de Microsoft para Windows Server (no hay página de Learn que fije un número único para estos contadores en Windows client) | ⚠️ Default del producto documentado en `MonitoringThresholds` (fuente única) — marcado como decisión interna alineada con la práctica común de Microsoft; no se atribuye a una cita textual |
| Capacidad de discos en vivo: `Win32_LogicalDisk` (`Size`, `FreeSpace`, `DriveType = 3`) | [Win32_LogicalDisk (WMI)](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisk) | ✅ OK — clase oficial; `DriveType 3` = "Local Disk"; `Size`/`FreeSpace` documentadas |
| Retiro de "Monitoreo en vivo cada 2 segundos." (StatusText vacío en operación normal; solo errores) y del pie "Solo lectura: WSG no altera procesos ni servicios." | Decisión de UI del operador | ✅ OK — el texto de solo lectura se conservaba en el footer; la nota de no-mutación sigue documentada en README/SAFETY y la UI ya no repite texto estático por tick |

## 2. Hallazgos del ciclo

### OK — Sparklines con umbrales sin dependencias y render correcto

El control `WsgSparkline` usa el modelo de render inmediato documentado de WPF (`OnRender` + `DrawingContext` + `Freeze()`), con `AffectsRender` en todas las DPs. Las líneas de umbral (advertencia naranja, crítico rojo) se dibujan solo si caen dentro del rango (0..Maximum). El histórico se reemplaza con una lista nueva por tick → `AffectsRender` dispara el re-dibujo sin converters ni temporizadores extra.

### OK — Capacidad de discos vía clase WMI oficial

`Win32_LogicalDisk` (`Size`/`FreeSpace`) es la clase oficial para capacidad de unidades lógicas; `DriveType = 3` (Local Disk) filtra unidades extraíbles/red. La consulta es de solo lectura; el fallo de la consulta conserva la lista previa (fail-closed, igual que el resto del monitoreo vivo).

### OK — Los umbrales son coherentes entre evidencia y UI

El collector de 0.4.1 ya emitía evidencia informativa con `LoadPercent ≥ 90` y `commitPercent ≥ 90`; los umbrales de las sparklines usan exactamente esos valores para RAM/commit (desde `MonitoringThresholds`, fuente única). CPU y disco quedan como defaults del producto, documentados y centralizados — no silencian ni exageran condiciones.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Consumidores significativos (0.9.0), verificación integrada (0.8.0), remediación (0.5.0/0.6.0) y todos los diagnósticos sin cambios en su frontera.

## 3. Fuentes

- [FrameworkElement.OnRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkelement.onrender) · [DrawingContext](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.drawingcontext) · [FrameworkPropertyMetadataOptions.AffectsRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkpropertymetadataoptions)
- [Win32_LogicalDisk (WMI)](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisk)
- [GlobalMemoryStatusEx (sysinfoapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
- Auditorías heredadas: 0.5.0-0.10.0 (docs/)

## 4. Conclusión

Las gráficas usan el modelo de render documentado de WPF sin dependencias nuevas; los umbrales de RAM/commit coinciden con los umbrales de evidencia existentes y los de CPU/disco están centralizados y documentados como defaults del producto; la capacidad de discos usa la clase WMI oficial. Sin datos erróneos ni falsos positivos conocidos. Deudas documentales heredadas (GetSystemTimes/GlobalMemoryStatusEx/VSS_WRITER_STATE 404) sin cambios.
