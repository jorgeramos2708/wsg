# Auditoría contra documentación oficial de Microsoft — WSG 0.13.1

> **Alcance**: revalidar el flujo completo acumulado y auditar el revert quirúrgico del layout de ventana. Cada ciclo cierra con esta auditoría.
> **Método**: herencia de auditorías 0.5.0-0.13.0 + verificación del cambio puntual.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0-0.13.0.

## 1. Cambio de 0.13.1

| Cambio | Fuente oficial | Veredicto |
|---|---|---|
| Revert de MainWindow.xaml a la estructura de 0.12.0 (sin ScrollViewer de ventana, sidebar 270, 1420×860, Min 1080/690) | [Grid (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.grid) — columnas con `*` se expanden al ancho del contenedor; sin ScrollViewer exterior el contenido se ajusta al ancho de la ventana, no lo excede | ✅ OK — la distorsión de 0.13.0 (sparklines/barras/tarjetas estiradas al redimensionar) se elimina al quitar el ScrollViewer que permitía que el Grid interior excediera el viewport |
| Umbrales en filas de etiqueta conservados (x:Static sobre MonitoringThresholds) | Herencia 0.13.0 | ✅ OK — el operador confirmó que funcionan |
| Sparklines sin texto FormattedText encimado | Herencia 0.13.0 | ✅ OK |

## 2. Hallazgos

### OK — Causa raíz de la distorsión identificada y corregida

El ScrollViewer a nivel de ventana (0.13.0) con `MinWidth=850` en el Grid interior permitía que el contenido del Grid fuera más ancho que el viewport: al estirar la ventana, las columnas `*` expandían sparklines, progress bars y tarjetas desproporcionadamente. Al retirar el ScrollViewer, el Grid interior se ajusta exactamente al ancho de la ventana sin excederse.

### ⚠️ Lección aprendida — No añadir ScrollViewer a nivel de ventana cuando las vistas ya tienen ScrollViewers

Los ScrollViewers anidados (ventana + vista) crean conflictos de layout: el ScrollViewer exterior permite que el contenido crezca más allá del viewport, y las columnas `*` de las vistas se expanden para llenar ese espacio extra. La solución correcta para pantallas pequeñas es mantener MinWidth/MinHeight razonables y dejar que los ScrollViewers de las vistas individuales manejen el scroll.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Todas las superficies previas sin cambios.

## 3. Fuentes

- [Grid (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.grid) · [ScrollViewer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.scrollviewer)
- Auditorías heredadas: 0.5.0-0.13.0 (docs/)

## 4. Conclusión

El revert elimina la distorsión sin perder las mejoras de 0.13.0 que funcionaron (umbrales en filas de etiqueta, sparklines limpias). Sin datos erróneos ni falsos positivos conocidos.
