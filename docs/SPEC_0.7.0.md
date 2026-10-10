# Spec: WSG 0.7.0 — Monitoreo de recursos en tiempo real

> Tercer ciclo. Requisito del operador: el monitoreo está congelado (solo refresco manual); se quiere lectura viva de recursos.

## Objective

El Dashboard (Inicio) muestra **CPU, RAM y commit en vivo**, actualizándose cada 2 segundos mediante `DispatcherTimer` (hilo de UI) + el collector de rendimiento existente (`GetSystemTimes` + `GlobalMemoryStatusEx`, APIs oficiales de Win32).

**Decisiones de diseño**:
1. **Solo lectura de almacenamiento**: las muestras vivas NO se escriben en el store JSONL (evitar inundar la evidencia append-only); la evidencia persistente mantiene su cadencia normal (>=30 s vía el pipeline existente).
2. **Busy-guard**: si la captura del tick anterior sigue en curso, el tick se salta.
3. **Ciclo de vida**: el timer arranca cuando el Dashboard es visible (`Loaded`) y se detiene al salir (`Unloaded`); botón de pausa/reanudar.
4. **Lógica pura en Core**: el mapeo `CollectorResult → LiveResourceSample` es testeable en `Core.Tests`.

**Acceptance criteria**: AC1 valores vivos cada 2 s en el Dashboard; AC2 sin escrituras al store; AC3 sin solapamiento de capturas; AC4 pausa/reanudar; AC5 bindings OneWay (audit); AC6 gate de QA intacto.

## Boundaries

- **Always**: captura en background, marshaling de vuelta al hilo UI vía await; busy-guard.
- **Never**: escribir muestras vivas al store; timers de sistema (System.Timers) que cruzan hilos de UI sin marshaling; bloquear el hilo de UI con la muestra (el delay de 300 ms de CPU es async).

## Success Criteria

- AC1-AC6 + auditoría MSDoc 0.7.0 (DispatcherTimer, GetSystemTimes, GlobalMemoryStatusEx citados) + gate PASS.
