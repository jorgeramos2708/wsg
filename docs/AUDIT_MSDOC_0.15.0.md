# Auditoría contra documentación oficial de Microsoft — WSG 0.15.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la limpieza de UI (retiro de tarjetas congeladas de Estado del sistema + botón de correlación redundante). Cada ciclo cierra con esta auditoría.
> **Método**: herencia de auditorías 0.5.0-0.14.0 + verificación del cambio puntual.
> **Base**: veredictos vigentes de AUDIT_MSDOC 0.5.0-0.14.0.

## 1. Cambios de 0.15.0

| Cambio | Fuente oficial | Veredicto |
|---|---|---|
| Retiro de las 8 tarjetas congeladas de ObservabilityView (Puntuación de salud, CPU, Memoria, Tiempo activo, Commit, Disco ocupado, Red, Principales consumidores) | Decisión de UI del operador — duplicaban información en vivo disponible en el Dashboard (0.7.0-0.9.0) | ✅ OK — la vista retiene Hallazgos y Colectores (su contenido único); las métricas en vivo están en la tarjeta del Dashboard que se actualiza cada 2 s |
| Retiro del botón "Correlacionar ahora" de IncidentsView | Correlación automática cada 30 s (DispatcherTimer 0.14.0, revalidado) | ✅ OK — el botón era redundante; la correlación corre sin intervención del operador |
| La correlación automática ejecuta `ObservabilityService.CaptureAsync(DiagnosticCost.Medium)` que solo ejecuta collectors Low/Medium/VeryLow | Diseño de scheduling de 0.4.1 | ✅ OK — los diagnósticos High (SFC verifyonly, DISM ScanHealth, CHKDSK scan) permanecen manuales |

## 2. Hallazgos

### OK — Sin pérdida de información única

Las tarjetas retiradas mostraban métricas del snapshot (congeladas entre capturas). Esa información vive en tiempo real en la tarjeta del Dashboard (0.7.0-0.9.0) y en la vista Diagnóstico. La vista Estado del sistema retiene Hallazgos (su contenido principal) y el estado de los Collectors.

### OK — Sin regresión

186/186 pruebas; 3 auditorías PASS. Todas las superficies previas sin cambios.

## 3. Fuentes

- Auditorías heredadas: 0.5.0-0.14.0 (docs/)
- [DispatcherTimer (WPF)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer) (revalidado de 0.7.0/0.14.0)

## 4. Conclusión

La limpieza de UI elimina duplicación sin pérdida de información única. Sin datos erróneos ni falsos positivos conocidos. Deudas documentales heredadas sin cambios.
