# Spec: WSG 0.8.0 — Verificación integrada en el diálogo de remediación

> Cuarto ciclo. Cierra el último hueco manual del ciclo reparar→verificar: tras una remediación exitosa, el diálogo ejecuta él mismo la verificación.

## Objective

Tras una ejecución de remediación exitosa, `RemediationViewModel`:
1. Obtiene los collectors de verificación declarados por el incidente (`VerificationCollectors`).
2. Ejecuta **exactamente esos collectors** vía `ObservabilityService.CaptureCollectorsAsync` (pipeline completo existente: evidence → findings → incidentes → ciclo de vida → persistencia).
3. Muestra el resultado de `RemediationVerificationService.Evaluate` + el **estado actualizado del incidente** (Active/Monitoring/Resolved) según el ciclo de vida.
4. Permite repetir la verificación ("Verificar de nuevo") sin relanzar la remediación.

**Decisiones de diseño**:
1. La verificación usa el **pipeline normal** (`CaptureCollectorsAsync`): el conteo de capturas limpias lo sigue haciendo `IncidentLifecycleTracker` — sin duplicación.
2. La selección de collectors es por nombre, desde los declarados por el incidente; los diagnósticos High/manual quedan habilitados solo aquí, por decisión explícita del operador (coherente con 0.4.1).
3. Fail-closed: collector ausente por nombre → mensaje claro; errores de captura → mensajes, sin estados inventados.

**Acceptance criteria**: AC1 tras ejecución exitosa corre automáticamente una verificación; AC2 "Verificar de nuevo" disponible; AC3 el estado del incidente se muestra actualizado según el ciclo de vida; AC4 los collectors ejecutados son exactamente los declarados por el incidente; AC5 gate de QA intacto.

## Success Criteria

AC1–AC5 + auditoría MSDoc 0.8.0 + gate PASS.
