# Spec: WSG 0.6.0 — Remediación de Windows Update (servicios)

> Segundo ciclo sobre la foundation 0.5.0. Misma filosofía fail-closed; se añade UNA acción.

## Objective

Ante un Incidente con causa raíz `WindowsUpdateFailure` (estado `Active`), el operador puede ejecutar un **reinicio controlado de los servicios Windows Update** (`bits` → `wuauserv`: stop ordenado, arranque ordenado) mediante `PrivilegedHost` con los mismos gates: preflight + aprobación explícita + resultado JSON + verificación posterior por los collectors declarados.

**Acceptance criteria**:
1. AC1 — Solo incidentes `WindowsUpdateFailure` + `Active` ofrecen la acción; el resto de causas sigue rechazado por la política.
2. AC2 — Flujo idéntico al de 0.5.0 (plan → preflight → aprobación → ejecución elevada → verificación); la aprobación muestra el alcance exacto: reinicio de servicios, **sin** borrar caché (`SoftwareDistribution`/`Catroot2` explícitamente fuera).
3. AC3 — El chequeo de espacio en disco del preflight no aplica a esta acción (no consume disco); elevación/reboot/en-curso sí aplican.
4. AC4 — `PrivilegedHost` usa la API oficial `System.ServiceProcess.ServiceController` (stop con `WaitForStatus` y timeout por servicio; arranque ordenado `wuauserv` → `bits`); fallos por servicio se reportan por paso en el OutputTail.
5. AC5 — El parser interpreta la ejecución: marcador determinista de éxito → `Repaired`; cualquier fallo/timeout → `Indeterminate` (fail-closed).
6. AC6 — El botón "Reparar…" de la UI se rige por la política (ya no por una lista duplicada en el ViewModel).
7. AC7 — Gate de QA intacto: 0 errores, 0 advertencias, pruebas, 3 auditorías PASS (static-safety ampliada: control de servicios confinado a PrivilegedHost).

## Commands

Sin cambios (mismos comandos de build/test/auditorías). Nueva operación interna en PrivilegedHost (no es CLI externa): `ServiceController` sobre `bits` y `wuauserv`.

## Project Structure

- `Contracts/Remediation`: `RemediationActionId.WindowsUpdateServiceRestart` (extensión de enum), sin tipos nuevos.
- `Core/Remediation`: catálogo con `RemediationCommandKind` (`ProcessCommand` | `ServiceRestart`) y nueva entrada; política con el nuevo mapeo; preflight condiciona el chequeo de espacio por `Kind`; parser extiende el veredicto para la nueva acción; helper estático de causas soportadas para la UI.
- `PrivilegedHost`: `ElevatedRemediationRunner` añade la rama de servicio (ServiceController con timeouts por paso).
- `UI`: `IncidentItemViewModel.CanRemediate` delega en la política.
- `tools/static-safety-audit.ps1`: control de servicios confinado a PrivilegedHost.
- Tests: política (nuevo mapeo + causas bloqueadas), catálogo (Kind), preflight (espacio condicional), parser (marcador de éxito).

## Boundaries

- **Always**: mismos gates 0.5.0; orden de arranque `wuauserv` antes que `bits`; `WaitForStatus` con timeout; fallo por servicio registrado en el OutputTail.
- **Ask first**: añadir más servicios al reinicio; cambiar el orden; nuevos tipos de acción.
- **Never**: borrar `SoftwareDistribution`/`Catroot2` (explícitamente fuera del intent); detener servicios distintos de `bits`/`wuauserv`; remediación automática; uso de `ServiceController` fuera de PrivilegedHost.

## Success Criteria

- AC1–AC7 verificados con pruebas + gate completo.
- Auditoría MSDoc 0.6.0: `ServiceController`/`Stop`/`WaitForStatus` contrastados con la doc oficial de .NET; nombres de servicio `bits`/`wuauserv` citados; sondeo empírico de solo lectura (`sc query`) sobre esta máquina.

## Open Questions

- Ninguna abierta (alcance fijado por el roadmap del README; lo demás fuera de alcance).
