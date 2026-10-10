# Auditoría contra documentación oficial de Microsoft — WSG 0.7.0

> **Alcance**: revalidar el flujo acumulado (diagnóstico → remediación 0.5.0/0.6.0 → verificación) y auditar la superficie nueva de este ciclo (monitoreo en tiempo real del Dashboard). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + razonamiento sobre el código. La superficie nueva es de **solo lectura** (sin mutaciones del sistema), por lo que la validación es principalmente de concurrencia/hilo de UI y de semántica de APIs.
> **Base**: veredictos de 0.5.0 y 0.6.0 siguen vigentes (`docs/AUDIT_MSDOC_0.5.0.md`, `docs/AUDIT_MSDOC_0.6.0.md`).

## 1. Superficie nueva de 0.7.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| `DispatcherTimer` corre en el hilo de la UI → las actualizaciones de propiedades bindables no requieren marshaling | [DispatcherTimer Class (System.Windows.Threading)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer) | ✅ OK — cita textual: "the DispatcherTimer runs on the same thread as the Dispatcher" |
| Elección de `DispatcherTimer` frente a `System.Timers.Timer` | Misma página | ✅ OK — cita textual: "If a System.Timers.Timer is used in a WPF application... it runs on a different thread than the user interface (UI) thread. In order to access objects on the UI thread, it is necessary to post the operation onto the Dispatcher" — exactamente el riesgo que se evita |
| El intervalo de 2 s es aproximado, no exacto | Misma página: "Timers are not guaranteed to execute exactly when the time interval occurs, but they are guaranteed to not execute before" | ✅ OK — el diseño no promete precisión; el busy-guard compensa ticks retrasados |
| Busy-guard (saltar tick si la captura anterior sigue en curso) | Misma página (executions are queued en el Dispatcher) | ✅ OK — evita acumulación de capturas pendientes cuando el sistema está cargado |
| CPU % por delta de dos muestras de `GetSystemTimes` (300 ms aparte) | [GetSystemTimes (sysinfoapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemtimes) — **página de referencia no localizada durante la auditoría (404)** | ⚠️ Parcialmente verificado — uso preexistente desde 0.4.1, validado empíricamente (valores coherentes en esta máquina); el muestreo es async (no bloquea el hilo de UI) |
| RAM vía `GlobalMemoryStatusEx` (`MemoryLoad`, `TotalPhys`, `AvailPhys`) | [GlobalMemoryStatusEx (sysinfoapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) — igualmente no localizada (404) | ⚠️ Parcialmente verificado — uso preexistente desde 0.4.1, empíricamente coherente |
| Muestras vivas **no se escriben** en el store de evidencia | Decisión de diseño interna (spec 0.7.0): el store es JSONL append-only con retención; evidencia viva a 2 s lo inundaría | ✅ OK — la cadencia de evidencia permanece en el pipeline normal (≥30 s) |
| Mapeo `CollectorResult → LiveResourceSample` fail-closed (métrica faltante → null, la UI conserva la última muestra) | Pruebas unitarias en Core | ✅ OK (4 pruebas nuevas) |
| Ciclo de vida del timer: corre solo con el Dashboard visible (`Loaded`/`Unloaded`), pausa/reanudar | Comportamiento WPF de `UserControl.Loaded`/`Unloaded` en un `ContentControl` que intercambia páginas | ✅ OK — sin capturas en segundo plano cuando la vista no está visible |

## 2. Hallazgos del ciclo

### OK — Diseño de concurrencia correcto según la documentación de WPF

La combinación `DispatcherTimer` (tick en hilo de UI) + `await collector.CollectAsync()` (continuación de vuelta al contexto de UI) + busy-guard evita: (a) actualizaciones de bindings desde hilos ajenos a la UI (excepción `InvalidOperationException` clásica de WPF), y (b) solapamiento de capturas. La doc oficial recomienda explícitamente `DispatcherTimer` sobre `System.Timers.Timer` para este escenario.

### ⚠️ Parcialmente verificado — APIs Win32 preexistentes (0.4.1)

Las páginas de referencia de `GetSystemTimes` y `GlobalMemoryStatusEx` no fueron localizadas en Learn durante esta auditoría (404). Ambas APIs son anteriores a este ciclo (collector de 0.4.1) y están validadas empíricamente: el collector produce muestras coherentes en esta máquina. Marcado como deuda de documentación, no como defecto.

### OK — Sin impacto en evidencia, incidentes ni remediación

El monitoreo en vivo es una **capa de lectura adicional**: no escribe evidencia, no altera el scheduler de diagnósticos, no participa en la correlación (por diseño: muestras de 2 s no son evidencia) y no interactúa con la remediación. Los veredictos de 0.5.0 y 0.6.0 permanecen intactos.

## 3. Fuentes

- [DispatcherTimer Class (System.Windows.Threading) — .NET/WPF API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer)
- `GetSystemTimes` / `GlobalMemoryStatusEx` (sysinfoapi.h): referencia no localizada (404) — marcado parcialmente verificado
- Referencias revalidadas por herencia: [AUDIT_MSDOC_0.5.0.md](AUDIT_MSDOC_0.5.0.md) y [AUDIT_MSDOC_0.6.0.md](AUDIT_MSDOC_0.6.0.md)

## 4. Conclusión

El monitoreo en tiempo real queda **validado contra la documentación oficial de WPF** (hilo de UI, elección de timer, semántica del intervalo) y **sin datos erróneos ni falsos positivos conocidos**: no toca evidencia, incidentes ni remediación, y falla hacia la conservación de la última muestra válida. Deuda documental menor: páginas Win32 de `GetSystemTimes`/`GlobalMemoryStatusEx` no localizadas (uso preexistente, empíricamente coherente).
