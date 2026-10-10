# Task List — WSG 0.5.0 Controlled Remediation Foundation

> Plan: `tasks/plan.md` · Spec: `docs/SPEC_0.5.0_REMEDIATION.md` · Intent: `docs/intent/wsg-0.5-remediation.md`
> Comandos de verificación: `dotnet build .\WindowsSystemGuardian.sln -c Release` · `dotnet test .\WindowsSystemGuardian.sln -c Release` · `.\tools\{xaml-binding,static-safety,qa-static}-audit.ps1`

## Task 1: Verificación de baseline 0.4.1 — ✅ COMPLETADA (build 0/0, 89/89 pruebas, 3 auditorías PASS)

**Description:** Confirmar que 0.4.1 compila, pasa pruebas y auditorías antes de tocar nada; es la línea base contra la que medir regresiones.

**Acceptance criteria:**
- [ ] `dotnet build -c Release` sin errores ni advertencias controlables
- [ ] `dotnet test` en verde (89+ pruebas)
- [ ] 3 auditorías PASS

**Verification:** build + test + auditorías (comandos arriba).
**Dependencies:** None. **Files:** ninguno (solo ejecución). **Scope:** XS

## Task 2: Contratos `Remediation/*` — ✅ COMPLETADA (7 archivos, build limpio)

**Description:** Tipos inmutables del dominio de remediación en `WindowsSystemGuardian.Contracts`: `RemediationActionId`, `RemediationParsedVerdict`, `RemediationPlan`, `PreflightResult` (+checks), `RemediationApproval`, `RemediationOutcome`, `RemediationRecord`.

**Acceptance criteria:**
- [ ] Compila sin advertencias; `RemediationActionId.None = 0` y fail-closed por defecto
- [ ] Ningún tipo referencia capas superiores (Contracts puro)

**Verification:** `dotnet build` limpio. **Dependencies:** Task 1. **Files:** 6-7 en `src/WindowsSystemGuardian.Contracts/Remediation/`. **Scope:** M

## Task 3: `RemediationPolicyEngine` + pruebas — ✅ COMPLETADA (17 pruebas)

**Description:** Motor que decide si una acción de remediación está permitida para (causa raíz, estado de incidente): mapea `ComponentStoreCorruption → ComponentStoreRepair`, `SystemFileIntegrity → SystemFileRepair`; rechaza todo lo demás con motivo.

**Acceptance criteria:**
- [ ] Mapeo exacto de las 2 causas soportadas; rechazo documentado para las otras 6 causas de 0.4.1 y para causas desconocidas
- [ ] Rechaza incidentes que no estén en `Active` y acciones no soportadas
- [ ] Pruebas unitarias sin dependencias de Windows

**Verification:** `dotnet test` (suite nueva en verde). **Dependencies:** Task 2. **Files:** `Core/Remediation/RemediationPolicyEngine.cs` + `Core.Tests/RemediationPolicyEngineTests.cs`. **Scope:** S

## Task 4: `PreflightEngine` + probe de privilegios — ✅ COMPLETADA (9 pruebas)

**Description:** Verificaciones previas a ejecutar: elevación disponible, reboot pendiente, remediación ya en curso (por requestId/acción), advertencia informativa de fuente de reparación. Core define `IRemediationPreflightProbe`; Windows lo implementa con primitivas existentes.

**Acceptance criteria:**
- [ ] Sin admin → veredicto `Blocked` con motivo "requiere privilegios" (sin lanzar UAC)
- [ ] Reboot pendiente → `Blocked` con motivo; remediation en curso → `Blocked`
- [ ] Fuente offline → `Allowed` con advertencia informativa (default aprobado en spec)
- [ ] Pruebas del motor con probes falsos

**Verification:** `dotnet test`. **Dependencies:** Task 2. **Files:** `Core/Remediation/PreflightEngine.cs`, `Core/Abstractions/IRemediationPreflightProbe.cs`, `Windows/Remediation/WindowsRemediationPreflightProbe.cs`, `Core.Tests/PreflightEngineTests.cs`. **Scope:** M

## Task 5: `RemediationOutcomeParser` + pruebas — ✅ COMPLETADA (15 pruebas, EN/ES)

**Description:** Parseo determinista de salida/exit code de DISM `/RestoreHealth` y SFC `/scannow` hacia `RemediationParsedVerdict` (Repaired, NoDamageFound, NotRepairable, Indeterminate, Cancelled). Fixtures de texto reales acotados.

**Acceptance criteria:**
- [ ] Exit code 0 + texto "no component store corruption"/similar → `NoDamageFound|Repaired` según texto
- [ ] Exit code distinto de 0 / timeout / salida vacía → `Indeterminate|NotRepairable` según reglas documentadas
- [ ] Salida truncada nunca produce veredicto positivo sin evidencia (fail-closed)
- [ ] Pruebas con fixtures

**Verification:** `dotnet test`. **Dependencies:** Task 2. **Files:** `Core/Remediation/RemediationOutcomeParser.cs` + `Core.Tests/RemediationOutcomeParserTests.cs`. **Scope:** M

## Task 6: `RemediationVerificationService` + pruebas — ✅ COMPLETADA (8 pruebas, no duplica el ciclo)

**Description:** Mapea resultados de los diagnósticos read-only existentes (DISM CheckHealth, SFC verifyonly) a un veredicto de verificación y decide el avance del incidente (`Active → Monitoring` con 1 verificación limpia; `Resolved` con 2), reutilizando `IncidentLifecycleTracker`.

**Acceptance criteria:**
- [ ] Verificación limpia ×1 → `Monitoring`; ×2 → `Resolved`; verificación sucia → vuelve a `Active` con el incidente documentado
- [ ] No duplica lógica de `IncidentLifecycleTracker` (lo consume)
- [ ] Pruebas del mapeo y del conteo de verificaciones

**Verification:** `dotnet test`. **Dependencies:** Tasks 2, 5. **Files:** `Core/Remediation/RemediationVerificationService.cs` + `Core.Tests/RemediationVerificationTests.cs`. **Scope:** M

## Checkpoint 1 — Foundation ✅ (build 0/0, 138/138 pruebas, sin elevación)

- [ ] Build Release limpio (0 errores, 0 advertencias) y todas las pruebas en verde
- [ ] Sin procesos reales ejecutados en pruebas; lógica de decisión 100 % cubierta

## Task 7: `ApprovalGate` + `RemediationEngine` — ✅ COMPLETADA (8 pruebas con fakes)

**Description:** `ApprovalGate` exige una `RemediationApproval` vigente (misma acción + mismo requestId, no revocada) antes de ejecutar; `RemediationEngine` orquesta plan → preflight → aprobación → ejecución (vía abstracción `IElevatedRemediationLauncher`) → verificación → auditoría, con fakes en pruebas.

**Acceptance criteria:**
- [ ] Sin aprobación vigente → no se ejecuta nada (motivo registrado)
- [ ] La ejecución usa exclusivamente `IElevatedRemediationLauncher`; ningún acceso a Process en Core
- [ ] Cada paso escribe `RemediationRecord` (vía abstracción de store)
- [ ] Pruebas del orquestador con fakes: camino feliz, bloqueo en cada frontera, cancelación

**Verification:** `dotnet test`. **Dependencies:** Tasks 3, 4, 5, 6. **Files:** `Core/Remediation/ApprovalGate.cs`, `Core/Remediation/RemediationEngine.cs`, `Core/Abstractions/IElevatedRemediationLauncher.cs` (+abstracción store), `Core.Tests/RemediationEngineTests.cs`. **Scope:** M

## Task 8: Proyecto `WindowsSystemGuardian.PrivilegedHost` — ✅ COMPLETADA (csproj + manifest + Program + runner + sln)

**Description:** Nueva consola elevada en la solución: `app.manifest` (requireAdministrator), `Program.cs` delgado (args estrictos: `--command <enum> --request <guid> --out <ruta>`), `ElevatedRemediationRunner` con los 2 comandos fijados (mismo patrón que `ReadOnlyDiagnosticProcessRunner`: timeout, kill del árbol, salida acotada), escribe `RemediationOutcome` JSON y exit code.

**Acceptance criteria:**
- [ ] Rechaza args desconocidos/incompletos con exit code documentado (no lanza procesos)
- [ ] Solo 2 comandos posibles, fijados en código; sin strings de comando externos
- [ ] Escribe JSON válido de `RemediationOutcome` y exit codes: 0 ok, 2 cancelado/inválido, 3 timeout, 4 fallo de ejecución
- [ ] Referencias solo Contracts (+ Core para el parser); sln actualizada

**Verification:** `dotnet build` limpio + smoke manual documentado (`--help` y arg inválido sin elevar no son posibles — se valida en Task 13). **Dependencies:** Tasks 2, 5. **Files:** nuevo proyecto (csproj, manifest, Program.cs, ElevatedRemediationRunner.cs) + `WindowsSystemGuardian.sln`. **Scope:** M

## Task 9: `ElevatedProcessLauncher` + pruebas de gramática — ✅ COMPLETADA (9 pruebas) + reglas nuevas en static-safety-audit

**Description:** Único punto de la solución con `Verb="runas"`: valida que el exe sea `PrivilegedHost.exe` del propio directorio de la aplicación con versión coincidente, construye args solo desde enum+guid+ruta segura, lanza y espera resultado JSON.

**Acceptance criteria:**
- [ ] Gramática de args probada como función pura (sin lanzar procesos en pruebas)
- [ ] Rechaza exe fuera del directorio de la app o versión dispareja
- [ ] `UseShellExecute=true` solo aquí, con comentario de justificación y regla añadida a `static-safety-audit.ps1`
- [ ] Ruta de salida en directorio de datos de WSG (no temp compartido)

**Verification:** `dotnet test` + auditoría estática PASS. **Dependencies:** Task 8. **Files:** `Windows/Remediation/ElevatedProcessLauncher.cs`, `Core.Tests/ElevatedLaunchGrammarTests.cs`, `tools/static-safety-audit.ps1`. **Scope:** M

## Task 10: Log de auditoría `RemediationRecordStore` — ✅ COMPLETADA (JSONL append-only)

**Description:** Store append-only JSONL de `RemediationRecord` en el directorio de datos local, siguiendo los patrones de retención/acotado de `LocalObservabilityStore` (TopDirectoryOnly, sin seguir reparse points).

**Acceptance criteria:**
- [ ] Append-only; cada registro con requestId, acción, fase (preflight/approval/run/verify), veredicto y marca UTC
- [ ] Lectura para la vista de Historial; sin borrado salvo retención existente
- [ ] Pruebas de escritura/lectura con directorio temporal

**Verification:** `dotnet test`. **Dependencies:** Task 2. **Files:** `Persistence/RemediationRecordStore.cs` (+resolver de ruta) + `Core.Tests/LocalObservabilityStoreTests`-style suite nueva. **Scope:** M

## Checkpoint 2 — Ejecución elevada ✅ (build 0/0, 154/154 pruebas, static-safety-audit PASS con reglas de elevación)

- [ ] Build + tests en verde; `static-safety-audit.ps1` PASS con la regla de elevación
- [ ] Flujo orquestador probado extremo a extremo con fakes de launcher/verificación

## Task 11: UI de remediación — ✅ COMPLETADA (RemediationView/ViewModel + entrada en Incidentes; XAML audit PASS)

**Description:** `RemediationViewModel` + `RemediationView` (ModernWpf): muestra preflight, comando exacto, riesgo, exige aprobación explícita (casilla + botón por acción), cola acotada de líneas de progreso y panel de verificación. Entrada desde la vista de Incidentes solo cuando la política lo permite (AC1).

**Acceptance criteria:**
- [ ] Botón "Reparar" visible solo para causas/estado soportados y remediation no en curso
- [ ] Sin aprobación marcada el botón de ejecución permanece deshabilitado; sin persistencia de la decisión
- [ ] UAC rechazado → mensaje claro, incidente intacto, registro de auditoría
- [ ] Tras ejecución: verificación automática y estado del incidente visible en la vista
- [ ] `xaml-binding-audit.ps1` PASS (bindings correctos)

**Verification:** build + tests + `xaml-binding-audit.ps1`. **Dependencies:** Tasks 7, 9, 10. **Files:** `UI/Views/RemediationView.xaml(.cs)`, `UI/ViewModels/RemediationViewModel.cs`, `UI/Views/IncidentsView.xaml(.cs)` (entrada), `MainWindow`/navegación. **Scope:** M

## Task 12: Documentación y versión — ✅ COMPLETADA (SAFETY/ARCHITECTURE/README 0.5.0 + bump de versión)

**Description:** Actualizar SAFETY.md (frontera de elevación, formato del log de auditoría, exit codes), ARCHITECTURE.md (capa Remediation + diagrama del flujo), README (sección 0.5.0: qué se implementa y qué sigue sin implementarse) y bump `Directory.Build.props` a 0.5.0.

**Acceptance criteria:**
- [ ] SAFETY.md declara la excepción `runas` con su justificación y límites
- [ ] README distingue acciones soportadas (2) de las aún bloqueadas (lista 0.4.1 reducida)
- [ ] AssemblyVersion/FileVersion = 0.5.0; build etiqueta 0.5.0

**Verification:** build limpio tras el bump; revisión manual de docs. **Dependencies:** Tasks 8, 9, 11. **Files:** 3 docs + `Directory.Build.props`. **Scope:** S

## Task 13: Gate de QA completo + smoke test manual — ✅ COMPLETADA (gate PASS; smoke manual pendiente en VM: docs/SMOKE_0.5.0.md)

**Description:** Ejecutar el gate completo y documentar el smoke test manual del escenario real (VM con corrupción provocada de component store: preflight → aprobación → reparación → verificación → incidente resuelto).

**Acceptance criteria:**
- [ ] 0 errores, 0 advertencias, todas las pruebas en verde
- [ ] 3 auditorías PASS (`xaml-binding`, `static-safety`, `qa-static`)
- [ ] Checklist de smoke test documentado en `docs/SMOKE_0.5.0.md` con resultados

**Verification:** comandos del gate + checklist firmado. **Dependencies:** Tasks 11, 12. **Files:** `docs/SMOKE_0.5.0.md`. **Scope:** S

## Checkpoint: Complete — ✅ Gate automatizado PASS (0 errores, 0 advertencias, 154/154, 3 auditorías); smoke manual en VM pendiente del operador

- [ ] AC1–AC7 de la spec verificados (pruebas + smoke)
- [ ] Sin regresiones de 0.4.1; documentación actualizada; versión 0.5.0
