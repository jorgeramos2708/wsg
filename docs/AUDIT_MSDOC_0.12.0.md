# Auditoría contra documentación oficial de Microsoft — WSG 0.12.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva (etiquetas de umbral con unidades en las sparklines + retiro del texto de no-mutación en la tarjeta de hallazgos). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías 0.5.0-0.11.0.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0-0.11.0.

## 1. Superficie nueva de 0.12.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| Etiqueta de umbral dibujada con `FormattedText` en `DrawingContext.DrawText` sobre cada línea de umbral, con unidad ("80 %", "90 %"…) | [DrawingContext.DrawText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.drawingcontext.drawtext) · [FormattedText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.formattedtext) — "draws a formatted text object at the specified location"; constructor con pixelsPerDip documentado | ✅ OK — la etiqueta se posiciona sobre la línea (o debajo si el espacio es insuficiente), sin converters |
| `UnitText` como `DependencyProperty` con `AffectsRender` (unidad configurable por gráfica: %, MB/s…) | [FrameworkPropertyMetadataOptions.AffectsRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkpropertymetadataoptions) | ✅ OK — mismo patrón que el resto de las DPs del control |
| Retiro de "un hallazgo no autoriza una reparación" de la tarjeta Hallazgos recientes | Decisión de UI del operador | ✅ OK — el texto era un recordatorio de política que ya está documentado en README/SAFETY y en cada recomendación (`AutomationAllowed = false` visible en el detalle del incidente); la tarjeta ahora describe el contenido, no la política |
| Sin cambios en umbrales, collectors, remediación ni verificación | Herencia 0.5.0-0.11.0 | ✅ OK — 186/186 pruebas, 3 auditorías PASS |

## 2. Hallazgos del ciclo

### OK — Etiquetas de umbral medibles

Cada línea de umbral ahora muestra su valor con unidad ("80 %", "90 %"), lo que permite **medir visualmente** la distancia entre la serie y el umbral sin hover. La implementación usa `FormattedText` + `DrawText` (primitivas documentadas de WPF) dentro del mismo `OnRender` — sin elementos visuales extra ni converters.

### OK — Texto de política retirado de la tarjeta de hallazgos

El mensaje movía contexto de política a un lugar donde el operador ya lo ve en cada recomendación del incidente (`AutomationAllowed = false` → "No se ejecuta automáticamente"). La política permanece documentada en README/SAFETY/auditorías y visible en el flujo de remediación.

### ⚠️ Incidencia operacional documentada — Build bloqueado por app en ejecución

Durante el gate, el build falló porque la aplicación WSG 0.11.0 estaba en ejecución (DLLs bloqueadas). Se cerró el proceso, se reconstruyó y el gate pasó limpio. Lección operacional documentada: cerrar la app antes de reconstruir. No afecta al producto ni al repositorio.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Todas las superficies previas sin cambios.

## 3. Fuentes

- [DrawingContext.DrawText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.drawingcontext.drawtext) · [FormattedText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.formattedtext) · [FrameworkPropertyMetadataOptions.AffectsRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkpropertymetadataoptions)
- Auditorías heredadas: 0.5.0-0.11.0 (docs/)

## 4. Conclusión

Las etiquetas de umbral usan las primitivas de texto documentadas de WPF y hacen las gráficas medibles sin hover ni converters. El texto de política retirado no reduce la trazabilidad de la política (permanece en docs y en cada recomendación). Sin datos erróneos ni falsos positivos conocidos. Deudas documentales heredadas sin cambios.
