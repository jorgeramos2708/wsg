# Spec: WSG 0.9.0 — Consumidores significativos en tiempo real (procesos y servicios)

> Quinto ciclo. Extiende el monitoreo en vivo de 0.7.0: además de CPU/RAM/commit del sistema, muestra los **procesos y servicios que consumen significativamente CPU y memoria**, en tiempo real.

## Objective

La tarjeta "Monitoreo en vivo" del Dashboard añade una lista de **principales consumidores** (top por CPU y por memoria, máx. 8 filas): nombre del proceso, PID, % CPU, memoria en uso y — cuando el proceso aloja servicios de Windows (p. ej. `svchost.exe`) — los nombres de esos servicios.

**Decisiones de diseño**:
1. **APIs oficiales**: `System.Diagnostics.Process` (`TotalProcessorTime` delta, `WorkingSet64`, `PrivateMemorySize64`) y `Win32_Service` (WMI) para atribuir procesos a servicios. Nada de CLIs externas.
2. **Solo lectura + sin store**: igual que 0.7.0 — las muestras vivas no se persisten.
3. **Lógica pura en Core**: clasificación (CPU % por delta, significancia, top-N) en `ProcessLiveClassifier`, testeable; el sampler (Windows) solo recolecta y mantiene la muestra previa de CPU.
4. **CPU % por núcleo**: `ΔTotalProcessorTime / elapsed / ProcessorCount × 100`, acotado 0-100.
5. **Atribución de servicios con caché**: el mapa PID→servicios (WMI) se refresca cada 30 s, no en cada tick.
6. **Significancia**: CPU ≥ 10 % (por núcleo) o memoria ≥ 200 MB marca la fila; se muestran siempre los top-N para contexto.

**Acceptance criteria**: AC1 lista viva de consumidores en el Dashboard; AC2 atribución de servicios para procesos que alojan servicios; AC3 clasificación testeada en Core; AC4 sin escrituras al store; AC5 gate de QA intacto; AC6 auditoría MSDoc 0.9.0.

## Boundaries

- **Always**: sampling en background (Task.Run), busy-guard del tick existente, procesos protegidos/efímeros ignorados silenciosamente (como el collector 0.4.1).
- **Never**: matar/suspender procesos desde el monitoreo (solo lectura); escribir muestras al store; atribuir servicios por heurísticas no documentadas.

## Success Criteria

AC1–AC6 + gate PASS.
