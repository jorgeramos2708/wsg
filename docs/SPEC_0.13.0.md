# Spec: WSG 0.13.0 — Sparklines legibles + ventana adaptable

> Séptimo ciclo. Peticiones del operador: (1) los indicadores de umbral dentro de la sparkline están encimados y muy pequeños; (2) la ventana abre muy grande y el contenido se esconde al redimensionar.

## Objective

1. **Sparklines legibles**: retirar el `FormattedText` de `DrawThreshold` (encimado/ilegible a 64 px de alto) y mover los valores de umbral a la **fila de etiqueta en XAML** ("CPU · aviso 80 % · crítico 90 %") usando `x:Static` sobre `MonitoringThresholds` — sin converters, legible a cualquier tamaño. Las líneas de umbral permanecen en la gráfica.
2. **Ventana adaptable**: tamaño inicial razonable (1200×740), mínimos reducidos (860×560) para que quepa en laptops; `ScrollViewer.HorizontalScrollBarVisibility="Auto"` en las vistas para que el contenido se desplace en lugar de esconderse cuando la ventana se reduce.

**Acceptance criteria**: AC1 umbrales legibles fuera de la sparkline; AC2 líneas de umbral visibles en la gráfica; AC3 ventana abre en 1200×740 (no más grande); AC4 contenido accesible por scroll al redimensionar; AC5 gate PASS; AC6 auditoría MSDoc 0.13.0.

## Boundaries

- **Never**: dibujar texto dentro de OnRender para labels de 64 px (ilegible); fijar MinWidth/MinHeight mayores que el viewport típico de una laptop.
