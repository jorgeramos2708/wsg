# Implementation Plan: WSG 0.5.0 — Controlled Remediation Foundation

> Spec aprobada: `docs/SPEC_0.5.0_REMEDIATION.md` · Intent: `docs/intent/wsg-0.5-remediation.md`
> Task list: `tasks/todo.md` (convención por defecto del proyecto; no hay tracker externo).

## Overview

Añade a WSG la primera rebanada de remediación controlada: ante un Incidente con causa raíz `ComponentStoreCorruption` o `SystemFileIntegrity`, el operador puede ejecutar — tras preflight y aprobación explícita — `DISM /RestoreHealth` o `SFC /scannow` en un proceso elevado aislado (`PrivilegedHost`), seguido de verificación con los diagnósticos read-only existentes que alimenta el ciclo de vida de incidentes. Fail-closed en cada frontera; gate de QA intacto.

## Architecture Decisions

1. **Lógica en Core, exe delgado**: política, preflight, parsing y verificación viven en `WindowsSystemGuardian.Core` y se prueban en `Core.Tests` sin elevar privilegios. `PrivilegedHost` es una consola delgada: parsea args de un enum fijo, ejecuta, emite JSON de resultado.
2. **Elevación por proceso, no servicio**: la UI lanza `WindowsSystemGuardian.PrivilegedHost.exe` con `Verb="runas"` (UAC) por cada remediación. Excepción única y documentada a la regla `UseShellExecute=false` de `ReadOnlyDiagnosticProcessRunner`; ese runner queda intacto.
3. **Contrato por args + JSON**: entrada = `--command <enum> --request <guid> --out <ruta>`; salida = `RemediationOutcome` JSON + exit code. Sin IPC, sin servicio, sin cola.
4. **Verificación reutiliza 0.4.1**: `DISM /CheckHealth` y `SFC /verifyonly` (ya implementados read-only) alimentan `RemediationVerificationService`; el ciclo `Active → Monitoring → Resolved` y su regla de dos verificaciones limpias no cambia.
5. **Auditoría append-only**: cada intento (preflight, aprobación, ejecución, verificación) se registra como `RemediationRecord` JSONL en el store local, reutilizando los patrones de `LocalObservabilityStore`.
6. **Fail-closed por diseño**: cualquier causa raíz, acción, estado o privilegio inesperado → bloqueado con motivo. Sin lista blanca implícita.

## Task List

### Phase 1: Foundation (Core sin elevación)

- [ ] Task 1: Verificación de baseline 0.4.1 (build + tests + auditorías en verde)
- [ ] Task 2: Contratos `Remediation/*` (enums, records, veredictos)
- [ ] Task 3: `RemediationPolicyEngine` + pruebas (causa raíz → acción, fail-closed)
- [ ] Task 4: `PreflightEngine` + probe de privilegios + pruebas
- [ ] Task 5: `RemediationOutcomeParser` + pruebas (fixtures DISM/SFC)
- [ ] Task 6: `RemediationVerificationService` + pruebas (veredicto → ciclo de incidentes)

### Checkpoint 1 — Foundation
- [ ] `dotnet build` Release sin errores ni advertencias; `dotnet test` en verde
- [ ] Política/preflight/parseo/verificación probados sin ejecutar procesos reales

### Phase 2: Ejecución elevada

- [ ] Task 7: `ApprovalGate` + `RemediationEngine` (orquestador) + pruebas con fakes
- [ ] Task 8: Proyecto `WindowsSystemGuardian.PrivilegedHost` (Program, runner, manifest, sln)
- [ ] Task 9: `ElevatedProcessLauncher` (runas) + pruebas de gramática de args
- [ ] Task 10: Log de auditoría `RemediationRecordStore` (JSONL append-only) + pruebas

### Checkpoint 2 — Ejecución elevada
- [ ] Build + tests en verde; `static-safety-audit.ps1` actualizado y PASS con la excepción de elevación declarada

### Phase 3: UI, documentación y cierre

- [ ] Task 11: `RemediationViewModel` + `RemediationView` + integración desde Incidentes
- [ ] Task 12: Docs (SAFETY.md, ARCHITECTURE.md, README 0.5.0) + bump de versión a 0.5.0
- [ ] Task 13: Gate de QA completo + checklist de smoke test manual (escenario real en VM)

### Checkpoint: Complete
- [ ] AC1–AC7 de la spec verificados; 3 auditorías PASS; sin regresiones de 0.4.1

## Risks and Mitigations

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Excepción `runas` debilita la superficie segura | Alto | `ElevatedProcessLauncher` único, exe fijo del propio directorio + validación de versión, args solo de enum; `static-safety-audit` extendido con regla específica |
| DISM/SFC reales requieren admin y minutos → pruebas lentas/omarislavas | Medio | Parsing y decisión en Core con fixtures; smoke test manual documentado en VM; timeouts generosos ya definidos |
| UAC rechazado por el usuario a mitad de flujo | Medio | Resultado "cancelado por el operador" registrado; sin reintento automático; el incidente queda `Active` con la recomendación intacta |
| PrivilegedHost queda huérfano si la UI muere | Bajo | Timeout propio del runner dentro de PrivilegedHost; resultado JSON queda en disco; el log de auditoría registra la sesión incompleta |
| Regresión de 0.4.1 | Medio | Baseline verde (Task 1) + checkpoint de regresión en cada checkpoint |

## Open Questions

- Ninguna abierta: las 3 de la spec se resolvieron con los defaults aprobados (progreso = cola acotada de líneas; DISM sin `/Source`; validación de exe por ruta+versión).
