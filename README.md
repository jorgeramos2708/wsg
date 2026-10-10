# Windows System Guardian 0.5.0

**Windows System Guardian (WSG)** es una herramienta Windows orientada a:

> **observar → detectar → correlacionar → explicar → recomendar → reparar de forma controlada → verificar**

La versión **0.5.0 — Controlled Remediation Foundation** mantiene todo el motor de 0.4.1 y habilita la primera rebanada de remediación controlada: `DISM /RestoreHealth` y `SFC /scannow` contra `ComponentStoreCorruption` y `SystemFileIntegrity`, siempre con preflight + aprobación explícita del operador + ejecución elevada aislada (`PrivilegedHost` + UAC) + verificación. **Ninguna otra acción se ejecuta.**


## 0.5.0 — Controlled Remediation Foundation

- **Frontera de elevación**: `PrivilegedHost` (consola `requireAdministrator`) ejecuta exclusivamente los comandos del catálogo (`RemediationCommandCatalog`); la UI lo lanza con UAC (`ElevatedProcessLauncher`, única excepción `runas` con validación de versión) y valida el resultado JSON por contrato (schema, acción, requestId).
- **Gates**: `RemediationPolicyEngine` (solo 2 causas raíz soportadas), `PreflightEngine` (privilegios, reinicio de servicing pendiente, remediación en curso, espacio en disco), `ApprovalGate` (aprobación explícita por ejecución, comando exacto visible, sin persistencia).
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
