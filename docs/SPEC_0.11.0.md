# Spec: WSG 0.11.0 — Gráficas en vivo con umbrales + capacidad de discos

> Sexto ciclo. Peticiones del operador: (1) barras → gráficas con umbrales según recomendación oficial, (2) capacidad de discos (disponible/no disponible) en el monitoreo en vivo, (3) quitar los textos "Monitoreo en vivo cada 2 segundos." y "Solo lectura: WSG no altera procesos ni servicios.".

## Objective

1. Las 4 métricas del sistema (CPU, RAM, Memoria comprometida, Discos) se muestran como **sparklines** (histórico de 60 muestras ≈ 2 minutos) con **líneas de umbral** dibujadas (advertencia/crítico) en un control propio `WsgSparkline` (OnRender con DrawingContext — sin dependencias de charting).
2. **Capacidad de discos en vivo**: por volumen (unidad, total, usado, libre, % usado) vía `Win32_LogicalDisk` (`Size`/`FreeSpace`, clase WMI oficial).
3. Textos retirados; `StatusText` solo informa errores.

**Umbrales (constantes centralizadas en Core, `MonitoringThresholds`)**:
- RAM / Memoria comprometida: aviso ≥ 90 % — coincide con los umbrales de evidencia del collector desde 0.4.1 (`LoadPercent ≥ 90`, `commitPercent ≥ 90`) y con la semántica de presión de memoria de `GlobalMemoryStatusEx`.
- CPU: aviso ≥ 80 %, crítico ≥ 90 % (sostenido) — default alineado con la guía de rendimiento de Microsoft (marcado como default interno si la página exacta no está localizable).
- Discos (actividad): aviso ≥ 85 % sostenido — default del mismo origen.

**Boundaries**: sin dependencias de charting (control propio); muestras vivas siguen sin escribirse al store; bindings OneWay; busy-guard intacto.

## Success Criteria

AC1 sparklines con umbrales visibles para las 4 métricas; AC2 tabla de capacidad por unidad en vivo (usado/libre); AC3 textos retirados; AC4 gate PASS; AC5 auditoría MSDoc 0.11.0.
