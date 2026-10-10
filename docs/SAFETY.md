# WSG 0.5.0 — Safety Model

## Regla central

**Detectar no concede permiso para modificar.**

0.4.x es observación/diagnóstico/recomendación. 0.5.0 añade la **primera rebanada de remediación controlada**: exactamente dos acciones, siempre con preflight + aprobación explícita del operador + verificación posterior. No existe reparación automática.

## CLI diagnósticas (solo lectura, sin cambios de 0.4.1)

Solo `WindowsSystemGuardian.Windows/Diagnostics/ReadOnlyDiagnosticProcessRunner.cs` puede crear procesos de diagnóstico externos.

El runner:

- usa rutas fijas de `%SystemRoot%\System32`;
- usa argumentos predefinidos;
- no recibe command line libre;
- valida drive letter para CHKDSK;
- no usa shell;
- redirige salida;
- impone timeout;
- limita salida capturada.

Permitido en 0.4.1 (sin cambios):

- DISM CheckHealth / ScanHealth.
- SFC verifyonly.
- CHKDSK /scan.
- REAgentC /info.
- vssadmin list writers.

## Remediación controlada (nuevo en 0.5.0)

**Acciones soportadas — exactamente dos:**

| Acción | Causa raíz que la habilita | Comando (fuente única: `RemediationCommandCatalog`) |
|---|---|---|
| `ComponentStoreRepair` | `ComponentStoreCorruption` (incidente `Active`) | `DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English` |
| `SystemFileRepair` | `SystemFileIntegrity` (incidente `Active`) | `sfc.exe /scannow` |

Cualquier otra causa raíz — incluida `ComponentStoreNonRepairable` — es rechazada por `RemediationPolicyEngine` con motivo explícito.

**Flujo obligatorio** (`RemediationEngine`): política → preflight → aprobación explícita → ejecución elevada → registro de auditoría. La verificación posterior (`DISM /CheckHealth`, `SFC /verifyonly`) alimenta el ciclo de vida existente: `Monitoring` con una captura limpia, `Resolved` con dos.

**Frontera de elevación — excepción única y acotada:**

- `WindowsSystemGuardian.Windows/Remediation/ElevatedProcessLauncher.cs` es el **único** archivo con `Verb = "runas"` (`UseShellExecute = true`); lo exige el mecanismo UAC del shell.
- El ejecutable es `WindowsSystemGuardian.PrivilegedHost.exe` del propio directorio de la aplicación, con validación de versión (si no coincide, no se ejecuta nada).
- Los argumentos salen de la gramática `ElevatedHostLaunchSpec` (enum de acción + GUID + ruta de resultado). Ningún texto de comando proviene de datos externos.
- `PrivilegedHost` (consola `requireAdministrator`) ejecuta **solo** los comandos del catálogo, resueltos desde System32, con timeout, kill del árbol de procesos, salida acotada y resultado JSON estructurado. Exit codes: 0 completado, 2 argumentos inválidos, 3 timeout, 4 fallo de ejecución.
- `static-safety-audit.ps1` verifica: archivos permitidos de proceso, unicidad de `runas`, guards de ambos runners, catálogo con exactamente los dos comandos y ausencia de switches destructivos.

**Fail-closed:**

- Sin privilegios de administrador el preflight bloquea (la UI no se auto-eleva; el operador relanza WSG como administrador).
- Sin aprobación vigente (misma acción + mismo requestId + mismo comando) no se ejecuta nada; las aprobaciones no se persisten ni se reutilizan.
- Una sola remediación por acción a la vez; UAC rechazado no se reintenta automáticamente.
- El resultado JSON se valida (versión de contrato, acción, requestId) antes de aceptarse.
- El Agent nunca dispara remediación; solo la UI, con humano presente.

Bloqueado (sigue sin implementarse):

- CHKDSK repair/offline fix, WinRE mutation, VSS mutation, reinicio automático, reset de SoftwareDistribution/Catroot2, BCD.

## Cleanup

Continúa analysis-only. La allowlist, validación de rutas y protección de reparse points de fases anteriores permanecen sin cambios.

## Persistencia

El delete productivo se limita a retención de archivos JSONL propios de WSG (observabilidad y ahora también el log de auditoría de remediación), con:

- carpetas administradas conocidas;
- `TopDirectoryOnly`;
- nombres administrados;
- validación reparse;
- sin recursividad genérica.

El log de auditoría de remediación (`RemediationRecordStore`, JSONL diario append-only bajo `%LOCALAPPDATA%\WindowsSystemGuardian\Data\Remediation`) registra preflight, aprobación, ejecución y verificación por requestId.
