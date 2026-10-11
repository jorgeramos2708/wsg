# Auditoría contra documentación oficial de Microsoft — WSG 0.13.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva (sparklines legibles, ventana adaptable). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías 0.5.0-0.12.0.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0-0.12.0.

## 1. Superficie nueva de 0.13.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| Valores de umbral mostrados en la fila de etiqueta (fuera del render de la sparkline) como texto Run con `x:Static` | [X:Static Markup Extension (WPF XAML)](https://learn.microsoft.com/en-us/dotnet/desktop/xaml-services/xstatic-markup-extension) | ✅ OK — resuelve el valor en tiempo de compilación desde la clase estática; legible a cualquier tamaño de fuente sin converters |
| Líneas de umbral permanecen dibujadas en OnRender (sin texto encimado) | [FrameworkElement.OnRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkelement.onrender) — heredado de 0.11.0 | ✅ OK — la sparkline muestra la línea de referencia; el valor numérico está en la etiqueta legible |
| Ventana 1200×740 (antes 1420×860) con MinWidth 860 / MinHeight 560 (antes 1080/690) | [Window.Width / MinWidth / MinHeight (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.width) | ✅ OK — tamaños apropiados para laptops con pantalla 1366×768; el usuario puede redimensionar hacia abajo |
| ScrollViewer a nivel de ventana con HorizontalScrollBarVisibility="Auto" + VerticalScrollBarVisibility="Auto" | [ScrollViewer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.scrollviewer) — "Represents a scrollable area that can contain visible elements" | ✅ OK — el contenido que no quepa en la ventana se desplaza horizontal y verticalmente en lugar de esconderse |
| Sidebar de 250 px (antes 270) + MinWidth 850 del Grid interior | Proporción sidebar/contenido ajustada para pantallas pequeñas | ✅ OK — el sidebar es fijo (convención de navegación WPF) pero proporcionalmente menor |
| Sparklines sin texto FormattedText encimado (retiro de 0.12.0, confirmado) | Herencia de 0.11.0 | ✅ OK — las líneas de umbral siguen visibles; el valor numérico está en la etiqueta legible |

## 2. Hallazgos del ciclo

### OK — Umbrales legibles fuera del render

El retiro del FormattedText de DrawThreshold y la adición de la información en la fila de etiqueta (con `x:Static` sobre `MonitoringThresholds`) resuelve el encimamiento sin tocar el modelo de render de la sparkline. La gráfica muestra las líneas de referencia; la etiqueta muestra los valores exactos.

### OK — Ventana adaptable con ScrollViewer a nivel de ventana

El ScrollViewer a nivel de ventana es la solución estándar de WPF para contenido que excede el viewport: permite scroll horizontal y vertical sin tocar las vistas individuales (que ya tienen sus propios ScrollViewers verticales). El tamaño inicial de 1200×740 y los mínimos de 860×560 son razonables para laptops con pantalla 1366×768.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Todas las superficies previas sin cambios.

## 3. Fuentes

- [X:Static Markup Extension](https://learn.microsoft.com/en-us/dotnet/desktop/xaml-services/xstatic-markup-extension) · [FrameworkElement.OnRender](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkelement.onrender) · [ScrollViewer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.scrollviewer) · [Window.Width](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.width)
- Auditorías heredadas: 0.5.0-0.12.0 (docs/)

## 4. Conclusión

Los umbrales son legibles fuera del render (en la etiqueta) y las líneas de referencia permanecen en la gráfica. La ventana abre a un tamaño razonable y el contenido es accesible por scroll al redimensionar. Sin datos erróneos ni falsos positivos conocidos. Deudas documentales heredadas sin cambios.
