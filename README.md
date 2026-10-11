# Windows System Guardian 0.11.0

**Windows System Guardian (WSG)** es una herramienta Windows orientada a:

> **observar → detectar → correlacionar → explicar → recomendar → reparar de forma controlada → verificar**

La versión **0.9.0 — Significant Consumers** amplía el monitoreo en vivo: además de CPU/RAM/commit del sistema, el Dashboard muestra en tiempo real los **procesos y servicios que consumen significativamente CPU y memoria** (top por CPU y por memoria, con atribución de servicios alojados vía WMI `Win32_Service`). Solo lectura; sin escrituras al store. Remediación y verificación integradas de 0.5.0-0.8.0 sin cambios.

## 0.9.0 — Significant Consumers

- Lista de **principales consumidores** (máx. 10 filas) en la tarjeta "Monitoreo en vivo": proceso (PID), % CPU, memoria, y los servicios de Windows que aloja cuando aplica (p. ej. `svchost.exe · wuauserv, bits`).
- CPU por proceso = delta de `TotalProcessorTime` / tiempo transcurrido / núcleos lógicos (`System.Diagnostics.Process`, API oficial); memoria = `WorkingSet64`.
- **Significancia**: CPU ≥ 10 % por núcleo o memoria ≥ 200 MB (marcador ▲); la clasificación vive en Core (`ProcessLiveClassifier`) y está probada.
- Atribución de servicios vía WMI `Win32_Service` (ProcessId), con caché de 30 s.
- Gate de QA: **0 errores, 0 advertencias, pruebas en verde y 3 auditorías PASS**.

## 0.8.0 — Integrated Verification

## 0.8.0 — Integrated Verification

- Tras una remediación exitosa, el diálogo ejecuta **exactamente los collectors de verificación declarados por el incidente** vía `ObservabilityService.CaptureCollectorsAsync` (pipeline completo: evidencia → hallazgos → incidentes → ciclo de vida → persistencia).
- `RemediationVerificationService.ResolveUpdatedIncident` resuelve el estado actualizado por fingerprint sin inventar transiciones.
- Botón "Verificar de nuevo" para repetir la verificación sin relanzar la remediación.
- Sin duplicación: el conteo de capturas limpias sigue en `IncidentLifecycleTracker`.
- Gate de QA: **0 errores, 0 advertencias, 179/179 pruebas y 3 auditorías PASS**.

## 0.7.0 — Live Monitoring

## 0.7.0 — Live Monitoring

- Tarjeta **"Monitoreo en vivo"** en el Dashboard: CPU %, RAM usada/total y commit % con barras de progreso, indicador de estado y botón pausar/reanudar.
- `DispatcherTimer` (hilo de UI, documentación oficial de WPF) con busy-guard: si la captura anterior sigue en curso, el tick se salta.
- El timer corre solo mientras el Dashboard es visible (`Loaded`/`Unloaded`).
- **Las muestras vivas no se persisten**: la evidencia del store mantiene su cadencia normal (evita inundar el JSONL append-only).
- El mapeo `CollectorResult → LiveResourceSample` vive en Core y está probado (fail-closed ante métricas faltantes).


## 0.6.0 — Remediación de Windows Update

- Nueva acción `WindowsUpdateServiceRestart`: stop(bits) → stop(wuauserv) → start(wuauserv) → start(bits), con `WaitForStatus` y timeout por paso (60 s); restauración best-effort si un paso falla; marcador determinista `WU_RESTART_OK` para el veredicto.
- El chequeo de espacio del preflight no aplica a esta acción (no consume disco, spec 0.6.0 AC3).
- El botón "Reparar…" de la UI lo decide la política (`RemediationPolicyEngine.IsRemediableRootCause`) — sin listas duplicadas en la UI.
- Control de servicios confinado a `PrivilegedHost` (`static-safety-audit` verifica que `ServiceController` no aparezca fuera de él).
- Gate de QA: **0 errores, 0 advertencias, pruebas en verde y 3 auditorías PASS**.

## 0.5.0 — Controlled Remediation Foundation

- **Frontera de elevación**: `PrivilegedHost` (consola `requireAdministrator`) ejecuta exclusivamente los comandos del catálogo (`RemediationCommandCatalog`); la UI lo lanza con UAC (`ElevatedProcessLauncher`, única excepción `runas` con validación de versión) y valida el resultado JSON por contrato (schema, acción, requestId).
- **Gates**: `RemediationPolicyEngine` (causas raíz soportadas por política), `PreflightEngine` (privilegios, reinicio de servicing pendiente, remediación en curso, espacio en disco cuando aplica), `ApprovalGate` (aprobación explícita por ejecución, comando exacto visible, sin persistencia).
- **Verificación**: los collectors read-only declarados por el incidente alimentan el ciclo `Active → Monitoring → Resolved` (dos capturas limpias).
- **Auditoría**: `RemediationRecordStore` (JSONL append-only) registra preflight, aprobación, ejecución y verificación por requestId.
- **Exit codes de PrivilegedHost**: 0 completado, 2 argumentos inválidos, 3 timeout, 4 fallo de ejecución.
- Gate de QA: **0 errores, 0 advertencias, 154/154 pruebas y 3 auditorías PASS**.

## 0.4.1 — Build/QA hotfix sobre 0.4.0

0.4.1 no amplía el alcance funcional. Corrige defectos encontrados durante el primer build real de 0.4.0 en Windows:

- colisión del namespace `Environment` en `ReadOnlyDiagnosticProcessRunner`;
- compatibilidad de `ObservabilityService.CaptureAsync(CancellationToken)` preservada mediante overload;
- 8 advertencias nullable en pruebas de incidentes de integridad;
- 5 advertencias `xUnit2031` sustituyendo `Where(...)+Assert.Single` por el overload con predicado.

Objetivo del gate 0.4.1: **0 errores, 0 advertencias controlables, 89/89 pruebas y 3 auditorías PASS**.

## Plataformas objetivo

- Windows 10/11.
- Windows Server 2019/2022/2025.
- Equipo físico o VM.
- Windows guest en Hyper-V, VMware y KVM/QEMU/Proxmox.
- Hyper-V host, nested Hyper-V y Failover Cluster con políticas más restrictivas.

## Pipeline actual

```text
Windows / almacenamiento / rendimiento / eventos / servicios
                       ↓
                    Evidence
                       ↓
                    Findings
                       ↓
                  Incident Engine
                       ↓
             Root Cause Correlation
                       ↓
             Explicación + confianza
                       ↓
                Recommendation Plan
                       ↓
             Verification Criteria
```

La remediación 0.5.0 queda limitada a las dos acciones del catálogo con su flujo de gates; todo lo demás permanece bloqueado.

## 0.4.1 — Integridad y servicing

### Comprobaciones automáticas de bajo/medio impacto

- `Win32_Volume`: dirty bit y estado de volúmenes System/Data.
- `DISM /Online /Cleanup-Image /CheckHealth /NoRestart /English`.
- Windows Update Agent:
  - historial reciente;
  - fallos/cancelaciones;
  - operaciones completadas con errores;
  - `RebootRequired`.
- `REAgentC /info`: preparación de Windows Recovery Environment.
- `vssadmin list writers`: preparación de VSS writers.

### Diagnósticos profundos manuales

Marcados `DiagnosticCost.High`, por lo que el scheduler normal no los ejecuta automáticamente:

- `DISM /ScanHealth`.
- `SFC /verifyonly`.
- `CHKDSK <unidad> /scan` únicamente en volúmenes NTFS System/Data con letra validada.

Los diagnósticos que Windows exige ejecutar elevados devuelven **No disponible / requiere privilegios** cuando la UI no tiene esos privilegios. La UI no intenta autoelevarse.

## Acciones expresamente NO implementadas en 0.5.0

Sigue bloqueado (salvo las dos acciones de la remediación controlada 0.5.0, que pasan por el flujo completo de gates):

- `CHKDSK /f`, `/r`, `/spotfix`, reparaciones offline.
- habilitar/deshabilitar/reconfigurar WinRE.
- crear/eliminar shadow copies.
- reiniciar servicios VSS de forma genérica.
- reiniciar Windows automáticamente.
- resetear/borrar `SoftwareDistribution` o `Catroot2` automáticamente.
- modificar BCD.
- cualquier remediación sin preflight + aprobación explícita + verificación.

## Causas raíz nuevas

El motor determinístico reconoce, entre otras:

- `FileSystemIntegrity`.
- `ComponentStoreCorruption`.
- `ComponentStoreNonRepairable`.
- `SystemFileIntegrity`.
- `WindowsUpdateFailure`.
- `PendingServicingReboot`.
- `RecoveryEnvironmentUnavailable`.
- `VssWriterFailure`.

Todas generan recomendaciones con `AutomationAllowed = false` y criterios explícitos de verificación.

## Funciones heredadas de 0.3.0

- CPU, RAM y commit.
- CPU/RAM/private bytes/handles/threads por proceso.
- crecimiento de memoria/handles/threads.
- uso/queue/throughput de disco.
- capacidad y crecimiento de volúmenes, incluidos mount points.
- utilización/errores/descartes de red.
- Event Log con Kernel-Power, BugCheck, NTFS/Disk, WHEA y SCM.
- Service Health.
- Findings, Incidents, Root Cause y Recommendation Engine.
- ciclo `Active → Monitoring → Resolved` con dos verificaciones limpias por dominio.
- Agent como writer autoritativo y UI read-only cuando existe el store del Agent.

## Seguridad

WSG mantiene una política **fail-closed** (detalle completo en `docs/SAFETY.md`):

- desconocido no equivale a seguro;
- los comandos de diagnóstico solo pueden salir de `ReadOnlyDiagnosticProcessRunner`;
- la remediación solo puede salir del catálogo `RemediationCommandCatalog` y ejecutarse en `PrivilegedHost`;
- el launcher de elevación (`ElevatedProcessLauncher`) es el único punto con `runas`, con exe fijo y validación de versión;
- ningún runner/launcher acepta executable/arguments arbitrarios;
- CHKDSK solo acepta una letra validada mediante regex;
- stdout/stderr redirigidos y limitados;
- timeout para toda CLI;
- el `File.Delete` permitido queda confinado a retención de JSONL propios de WSG (observabilidad y auditoría de remediación);
- retención `TopDirectoryOnly`, sin seguir reparse points;
- sin aprobación explícita vigente no se ejecuta ninguna remediación;
- el Agent nunca remedia.

## UI

Navegación 0.5.0:

```text
Inicio
Estado del sistema
Incidentes
Integridad de Windows
Almacenamiento
Liberar espacio
Diagnóstico
Historial
```

`Integridad de Windows` muestra módulos estándar, diagnósticos profundos manuales y Findings de Integrity/Servicing/Recovery/Backup.

## Build / QA gate

En Windows:

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

Después:

```powershell
dotnet run --project .\src\WindowsSystemGuardian.UI\WindowsSystemGuardian.UI.csproj -c Release
```

## Siguiente fase prevista

**Ampliación de la remediación controlada** sobre la foundation de 0.5.0:

- más acciones acotadas (p. ej. reinicio controlado de servicios Windows Update);
- política de ventana de mantenimiento y riesgo por acción;
- resultado de DISM/SFC en streaming estructurado;
- integración directa de la verificación en el diálogo de remediación;
- smoke test automatizado en VM.

La ampliación no se habilitará hasta que 0.5.0 pase build, tests, auditorías, smoke test y QA runtime en Windows.


