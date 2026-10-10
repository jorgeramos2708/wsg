# Windows System Guardian 0.4.1 — Auditoría profunda de QA

**Fase:** Windows Integrity & Servicing Health  
**Veredicto:** **GO ESTÁTICO / BUILD-RUNTIME PENDIENTE EN WINDOWS**

## Resumen

0.4.1 añade diagnóstico de integridad de filesystem, Component Store, archivos protegidos, Windows Update, WinRE y VSS sobre la base Incident/Root Cause 0.3.0. La fase continúa siendo **diagnóstico + recomendación**; no ejecuta reparaciones.

Durante la auditoría se detectaron defectos de diseño antes del empaquetado y fueron corregidos. Después del hardening quedan **0 Critical y 0 High abiertos en la revisión estática**.


### Addendum de build 0.4.1

El primer build real de 0.4.0 en Windows reportó **8 errores y 13 advertencias**. La revisión 0.4.1 corrige específicamente:

1. cuatro `CS0234` por resolución de `Environment` hacia `WindowsSystemGuardian.Windows.Environment`; se usa `global::System.Environment`;
2. cuatro `CS1503` por cambio de firma de `CaptureAsync`; se restaura compatibilidad con un overload `CaptureAsync(CancellationToken)`;
3. ocho `CS8604` de tests usando colecciones opcionales de verificación;
4. cinco `xUnit2031` sustituyendo filtrado LINQ previo a `Assert.Single`.

Estos cambios son de compilación/QA y **no habilitan reparación ni cambian la lógica funcional de diagnóstico**. El estado de build continúa pendiente hasta que el paquete 0.4.1 sea compilado en Windows.

## Inventario estático final

- C# productivo: 110 archivos.
- XAML: 11 archivos.
- Bindings: 131; todos explícitos `Mode=OneWay`.
- Tests fuente: 81 `[Fact]` + 1 `[Theory]` con 8 casos; **89 casos runtime esperados**.
- Errores XML/XAML: 0.
- ProjectReference faltantes: 0.
- TODO/FIXME/HACK/NotImplementedException: 0.
- `SearchOption.AllDirectories`: 0.
- operaciones destructivas de Windows: 0.
- `File.Delete`: 1, exclusivamente retención JSONL WSG ya protegida.

## Correcciones realizadas durante QA

### H-01 — Compatibilidad histórica de EvidenceCategory

**Hallazgo:** Recovery/Backup se habían insertado antes de `Observability`. Como los enums forman parte de JSONL histórico, un valor numérico antiguo de Observability podía reinterpretarse como otra categoría.

**Corrección:** valores explícitos y estables:

- Security = 9
- Observability = 10
- Recovery = 11
- Backup = 12

Se añadieron tests de compatibilidad.

**Estado:** RESUELTO.

### H-02 — Resolución imposible de incidentes deep/quick

**Hallazgo:** un Finding de `filesystem-scan` podía exigir simultáneamente el collector deep y el quick; lo mismo para `component-store-deep`. Al ejecutarse con costos/cadencias distintas, el incidente podía no alcanzar nunca dos verificaciones limpias.

**Corrección:** cada hallazgo deep se verifica por el collector deep que lo originó; el quick se usa para hallazgos quick. Se añadieron tests.

**Estado:** RESUELTO.

### H-03 — Timeout de CLI podía bloquear el drenaje de stdout/stderr

**Hallazgo:** `WaitForExitAsync` tenía timeout, pero `ReadToEndAsync` utilizaba únicamente el token del usuario. Si el proceso no terminaba y `Kill` fallaba, el runner podía seguir esperando salida.

**Corrección:** stdout, stderr y WaitForExit comparten el mismo token de timeout enlazado. El kill sigue siendo best-effort exclusivamente sobre el proceso diagnóstico iniciado por WSG.

**Estado:** RESUELTO.

### H-04 — Ejecución privilegiada no estaba diferenciada

**Hallazgo:** DISM/SFC/CHKDSK/VSS pueden requerir privilegios. Una UI estándar podía ejecutar la CLI, recibir un código de error y convertirlo en un Finding del sistema.

**Corrección:** `WindowsPrivilegeContext` consulta el token actual sin elevarlo. Los collectors que requieren administrador retornan `CollectorResult.Failure`/No disponible cuando no hay privilegios. La UI no solicita UAC automáticamente.

**Estado:** RESUELTO.

### H-05 — Fallos de herramienta podían confundirse con corrupción

**Hallazgo:** un exit code no reconocido de DISM, SFC, VSS o REAgentC podía producir Warning de integridad/servicing aunque WSG no hubiese demostrado corrupción.

**Corrección:**

- DISM solo crea Finding cuando su salida declara corrupción reparable/no reparable.
- SFC solo crea Finding cuando reconoce violaciones; idioma/formato no interpretable = collector no disponible.
- VSS solo crea Finding cuando existen writers y su estado es distinto de Stable; error/parse vacío = collector no disponible.
- WinRE solo crea Finding cuando se reconoce explícitamente Disabled/Deshabilitado; salida no interpretable = collector no disponible.

**Estado:** RESUELTO.

### M-01 — Operaciones WUA canceladas se contaban como fallos

Microsoft define `orcFailed=4` y `orcAborted=5`. Cancelado no demuestra un fallo de Windows.

**Corrección:** ResultCode 4 genera fallo; 5 se conserva como evidencia Informational y métrica independiente.

**Estado:** RESUELTO.

### M-02 — Historial WUA podía quedar truncado sin avisar

**Corrección:** si las 100 entradas consultadas siguen dentro de la ventana de 14 días y existen más entradas, WSG genera un Finding de **cobertura de observabilidad**, no atribuye falsamente una causa y declara que los conteos pueden estar incompletos.

**Estado:** RESUELTO.

### M-03 — Status WMI nulo podía parecer fallo de volumen

**Corrección:** `Win32_Volume.Status` nulo ya no se interpreta como estado anómalo. Solo un Status explícito distinto de OK o un LastErrorCode distinto de cero genera evidencia.

**Estado:** RESUELTO.

## Safety boundary de procesos externos

0.4.1 introduce por primera vez procesos CLI. La auditoría se modificó para que exista **un único boundary**:

`ReadOnlyDiagnosticProcessRunner`

Propiedades verificadas:

- ejecutables bajo `Environment.SystemDirectory`;
- enum de comandos cerrado;
- ningún executable/arguments arbitrario;
- `UseShellExecute=false`;
- stdout/stderr redirigidos;
- timeout;
- output limitado;
- CHKDSK recibe solo drive letter validada;
- un solo `process.Start()` en todo src;
- `process.Kill` únicamente para terminar el proceso diagnóstico que excedió timeout.

Comandos permitidos:

- DISM CheckHealth.
- DISM ScanHealth.
- SFC verifyonly.
- CHKDSK /scan.
- REAgentC /info.
- vssadmin list writers.

El auditor bloquea argumentos ejecutables de reparación como RestoreHealth, scannow, CHKDSK /f-/r-/spotfix/offline fix, mutación WinRE y eliminación VSS.

## Auditoría funcional por módulo

### Filesystem

PASS estático:

- quick check solo consulta WMI.
- solo System/Data afectan la detección.
- dirty system volume = Critical; data = Warning.
- deep scan solo NTFS, local, explícito y High/manual.
- no `/f`, `/r`, `/spotfix`, `/offlinescanandfix`.

### Component Store

PASS estático:

- CheckHealth Medium.
- ScanHealth High/manual.
- `/English` para clasificación estable.
- no RestoreHealth.
- no repair source ni cleanup mutation.

### SFC

PASS estático:

- solo `/verifyonly`.
- High/manual.
- administrador requerido.
- salida desconocida no se trata como salud ni corrupción demostrada.

### Windows Update

PASS estático:

- usa WUA COM para `QueryHistory`.
- no Search/Download/Install/Uninstall.
- ResultCode 4 = failure; 5 = aborted/informational; 3 = succeeded with errors.
- `Microsoft.Update.SystemInfo.RebootRequired` solo consulta.
- exposición de truncación de historial.

### WinRE

PASS estático:

- solo `REAgentC /info`.
- no enable/disable/setreimage/BCD mutation.
- parse no concluyente falla cerrado.

### VSS

PASS estático:

- solo `vssadmin list writers`.
- ningún create/delete/resize shadow storage.
- State [1] Stable se considera saludable.
- no writer parsable ≠ fallo VSS demostrado.

## Root Cause / recomendaciones

Causas nuevas verificadas:

- FileSystemIntegrity.
- ComponentStoreCorruption.
- ComponentStoreNonRepairable.
- SystemFileIntegrity.
- WindowsUpdateFailure.
- PendingServicingReboot.
- RecoveryEnvironmentUnavailable.
- VssWriterFailure.

Todos los `RecommendationStep` continúan con `AutomationAllowed=false`.

## Riesgos residuales — MEDIUM

1. **Deep diagnostics desde UI sin PrivilegedHost.** La UI normal no se autoeleva. Si el diagnóstico requiere administrador y no existe un mecanismo IPC hacia Agent, el botón informa que no está disponible. Se resolverá con PrivilegedHost/UAC en la fase de remediación.
2. **ReFS:** 0.4.1 monitoriza estado WMI/eventos, pero no ejecuta un deep scan equivalente a CHKDSK /scan. ReFS tiene mecanismos nativos de resiliencia/scrubbing y requiere tratamiento específico.
3. **Pending reboot general:** se consulta el indicador oficial de Windows Update; todavía no se consolidan todos los posibles orígenes de reboot (por ejemplo instaladores externos/rename operations).
4. **WinRE locale:** se reconocen formatos inglés/español. Otro formato se presenta como No disponible, nunca como falso fallo.
5. **VSS readiness ≠ backup válido:** writers Stable no demuestran que un producto de respaldo concreto pueda completar/restaurar un backup.
6. Se mantienen riesgos 0.3.x todavía no resueltos: bookmark Event Log durable, heartbeat independiente del Agent, schemaVersion JSONL y scheduler monotónico.

## Límites deliberados

No se consideran defectos de 0.4.1:

- RestoreHealth no implementado.
- SFC scannow no implementado.
- Repair-Volume/CHKDSK repair no implementado.
- WinRE/BCD mutation no implementada.
- reparación VSS no implementada.
- reboot automático no implementado.
- reparación AD/Hyper-V/Cluster no implementada.

Estas capacidades requieren primero el motor de remediación controlada.

## Gate Windows requerido

```powershell
dotnet clean .\WindowsSystemGuardian.sln
dotnet restore .\WindowsSystemGuardian.sln
dotnet build .\WindowsSystemGuardian.sln -c Release
dotnet test .\WindowsSystemGuardian.sln -c Release

Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\tools\xaml-binding-audit.ps1
.\tools\static-safety-audit.ps1
.\tools\qa-static-audit.ps1
```

**89 casos de test runtime esperados** (81 Facts + Theory con 8 InlineData).

Después debe realizarse smoke test como usuario estándar y como Agent/service, incluyendo:

- módulos no privilegiados;
- comportamiento No disponible sin elevación;
- DISM/SFC/CHKDSK manuales en entorno de prueba;
- WUA history;
- WinRE;
- VSS;
- Incident/Root Cause generado por evidencia simulada/real;
- ausencia de cambios de sistema.

## Veredicto

**GO ESTÁTICO.**

- Critical abiertos: 0.
- High abiertos: 0.
- Medium residuales: cobertura/usabilidad conocida, fail-closed.
- Reparaciones: 0.

No avanzar a ejecución de remediación hasta que build/tests/3 auditorías y smoke test de 0.4.1 pasen en Windows.
