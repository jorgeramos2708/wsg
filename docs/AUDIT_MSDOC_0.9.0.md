# Auditoría contra documentación oficial de Microsoft — WSG 0.9.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva de este ciclo (consumidores significativos en tiempo real: procesos y servicios). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + herencia de auditorías previas (0.5.0/0.6.0/0.7.0/0.8.0).
> **Base**: veredictos vigentes de las auditorías 0.5.0 (WUA, DISM, SFC, Win32_Volume, fixes WinRE/VSS), 0.6.0 (ServiceController/SCM), 0.7.0 (DispatcherTimer) y 0.8.0 (integración de verificación).

## 1. Superficie nueva de 0.9.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| CPU % por proceso = delta de `TotalProcessorTime` / tiempo transcurrido / núcleos lógicos × 100 | [Process.TotalProcessorTime (System.Diagnostics)](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime): "the amount of time the associated process has spent utilizing the CPU... sum of UserProcessorTime and PrivilegedProcessorTime" | ✅ OK — el delta de tiempo de CPU total entre dos muestras, normalizado por tiempo de pared y núcleos, reproduce la semántica de "% Processor Time" |
| Memoria por proceso = `WorkingSet64` | [Process.WorkingSet64 (System.Diagnostics)](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64): "the amount of physical memory, in bytes, allocated for the associated process" | ✅ OK (misma familia de API, misma página de clase) |
| Procesos protegidos/efímeros pueden fallar al leerse → se ignoran silenciosamente con contador de cobertura | Misma página (excepciones documentadas: `InvalidOperationException`, `Win32Exception`) | ✅ OK — mismo manejo que el collector de 0.4.1 |
| Atribución proceso → servicios: `Win32_Service` con propiedad `ProcessId` ("Process identifier of the service") | [Win32_Service class (WMI)](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-service) | ✅ OK — la relación proceso↔servicio es la propiedad oficial `ProcessId`; un proceso que aloja servicios (Share Process, p. ej. svchost) se resuelve consultando `Win32_Service` por PID |
| Consulta WMI solo lectura `SELECT Name, ProcessId, State FROM Win32_Service WHERE ProcessId != 0` | Misma página (clase WMI de solo lectura en este uso; los métodos de mutación de la clase no se invocan) | ✅ OK — sin llamada a `StopService`/`Change`/`Delete` ni métodos de mutación |
| `Environment.ProcessorCount` = núcleos lógicos para normalizar | [.NET Environment.ProcessorCount](https://learn.microsoft.com/en-us/dotnet/api/system.environment.processorcount): número de procesadores lógicos | ✅ OK |
| Muestras vivas **no se escriben** en el store; el sampling corre en background (Task.Run) con busy-guard del tick | Herencia de 0.7.0 (DispatcherTimer validado) | ✅ OK — sin cambios de diseño de concurrencia |
| El monitoreo **no mata, suspende ni modifica** procesos/servicios (solo lectura; nota visible en la UI) | Herencia 0.6.0 (ServiceController confinado a PrivilegedHost; la regla static-safety sigue PASS) | ✅ OK — el sampler no usa ninguna API de mutación |

## 2. Hallazgos del ciclo

### OK — Cálculo de CPU por proceso coherente con los contadores oficiales

La fórmula (delta de `TotalProcessorTime` normalizado por tiempo de pared y núcleos lógicos) reproduce la semántica del contador `% Processor Time` de Windows. La clasificación vive en Core (`ProcessLiveClassifier`) y está probada: delta, clamp 0-100, sin línea base → CPU 0 (sin delta inventado), umbrales de significancia (10 % / 200 MB), fusión de rankings sin duplicados.

### OK — Atribución de servicios con la clase WMI oficial y con caché

`Win32_Service.ProcessId` es la propiedad oficial que relaciona servicio con proceso. El mapa PID→servicios se consulta cada 30 s (caché), no en cada tick, y un fallo de la consulta deja la lista de procesos funcionando sin atribución (sin datos fabricados). Los procesos no alojados (p. ej. una app normal) se muestran sin servicios.

### ⚠️ Parcialmente verificado — Primer tick sin delta

En el primer tick tras abrir/pausar/reanudar, no existe línea base de CPU → los procesos se reportan con CPU 0 (y memoria real). Es la decisión fail-closed correcta (no inventar delta) y se autcorrige al tick siguiente; marcado como comportamiento esperado, no defecto.

### OK — Sin regresión

186/186 pruebas (+7 del clasificador); las 3 auditorías PASS. El resto del flujo (diagnósticos, incidentes, remediación con 3 acciones, verificación integrada, live del sistema) sin cambios.

## 3. Fuentes

- [Process.TotalProcessorTime (System.Diagnostics)](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime) · [Process class (misma familia: WorkingSet64, PrivateMemorySize64)](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process)
- [Win32_Service class (WMI)](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-service)
- Auditorías heredadas: 0.5.0, 0.6.0, 0.7.0, 0.8.0 (docs/)

## 4. Conclusión

La superficie nueva es **solo lectura, usa APIs oficiales documentadas (Process + Win32_Service) y su lógica de interpretación está probada en Core**. Sin datos erróneos ni falsos positivos conocidos: los estados transitorios de CPU (primer tick) se reportan como 0 en lugar de inventarse, y la significancia tiene umbrales explícitos documentados en la UI. Deudas documentales heredadas sin cambios.
