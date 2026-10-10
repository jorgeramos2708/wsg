# Windows System Guardian 0.2.2 — Reauditoría profunda QA

**Fase:** Observability Foundation — QA Hardening  
**Base auditada:** 0.2.0  
**Versión resultante:** 0.2.2  
**Objetivo del producto:** observar → detectar → correlacionar → explicar → recomendar → reparar de forma controlada → verificar.

## Veredicto

**GO ESTÁTICO CON GATE DE RUNTIME.**

Después del hardening, la inspección estática deja **0 hallazgos críticos y 0 hallazgos altos abiertos** de la auditoría 0.2.0. La versión **todavía no queda autorizada para iniciar 0.3.0** hasta completar en Windows el build Release, xUnit, auditorías PowerShell, pruebas runtime de WMI/Event Log/VBS/mount points y un soak mínimo de 8 horas del Agent.

Esto es intencional: WSG no debe construir Incident/Root Cause sobre una base que solo “parece correcta” estáticamente.

## Resultado de reauditoría estática

| Control | Resultado |
|---|---:|
| Archivos C# inspeccionados | 88 |
| Archivos XAML | 9 |
| Bindings revisados | 83 |
| Bindings sin `Mode=OneWay` | 0 |
| Errores XML/XAML detectados | 0 |
| Referencias de proyecto faltantes | 0 |
| Recursos `Wsg*` faltantes | 0 |
| Handlers XAML sin implementación | 0 |
| `TODO` / `FIXME` / `HACK` / `NotImplementedException` | 0 |
| `SearchOption.AllDirectories` en código productivo | 0 |
| Operaciones destructivas de Windows | 0 |
| `File.Delete` productivo | 1, solo retención WSG validada |
| Tests `[Fact]` presentes | 37 |

> El entorno de auditoría no tiene .NET SDK, PowerShell ni runtime WPF de Windows. Por ello no se afirma que `dotnet build`, `dotnet test` o los tests runtime hayan pasado aquí.

---

# Cierre de hallazgos de 0.2.0

## C-01 — Retención podía atravesar reparse points

**Estado: RESUELTO.**

Cambios:
- eliminado `SearchOption.AllDirectories`;
- enumeración solo de `evidence`, `metrics`, `findings`;
- `SearchOption.TopDirectoryOnly`;
- solo se admiten archivos exactos `yyyy-MM-dd.jsonl`;
- directorio padre exacto debe ser uno de los tres folders administrados;
- raíz, directorios administrados y archivo se comprueban contra `FileAttributes.ReparsePoint`;
- `File.Delete` solo es accesible mediante `TryDeleteManagedFileUnsafe`;
- la auditoría estática falla si reaparece recursividad genérica;
- se añadió test de directorio anidado y test de managed-folder convertido en symbolic link cuando la plataforma lo permite.

**Residual:** existe la carrera TOCTOU teórica entre validación de atributos y delete. Se considera riesgo residual bajo porque la operación solo actúa sobre archivos JSONL de nombre estricto en directorios exactos y eliminar un symlink de archivo elimina el enlace, no concede a WSG un traversal recursivo. Antes de cualquier modelo de limpieza destructiva general se requerirá validación de identidad final/handle-based.

## H-01 — Agent/UI tenían dos fuentes de verdad

**Estado: RESUELTO PARA 0.2.x.**

- Agent marca `%ProgramData%\WindowsSystemGuardian\Data\Observability` como store autoritativo.
- UI detecta el marcador y usa `ReadOnlyObservabilityStore`.
- si no existe Agent store, UI usa `%LOCALAPPDATA%` como fallback.
- la UI no compite como segundo escritor.

## H-02 — Relectura completa de 24 h en cada captura

**Estado: RESUELTO.**

- añadido `MetricHistoryBuffer`;
- ventana acotada a 6 h / 25,000 muestras;
- el histórico se carga una sola vez por instancia;
- nuevas métricas se agregan incrementalmente;
- CPU/RAM usan ventanas recientes y tendencia de disco usa 2 h.

## H-03 — Fallo de persistencia invalidaba la captura

**Estado: RESUELTO.**

Captura, evaluación y persistencia están desacopladas. Un error al escribir o retener histórico:
- no descarta evidencia actual;
- devuelve `PersistenceDegraded=true`;
- conserva mensaje de error;
- reduce ligeramente Health Score;
- se muestra en UI como persistencia degradada.

## H-04 — Deduplicación podía perder Warning → Critical

**Estado: RESUELTO.**

- evidencia de observación usa timestamp completo y severidad cuando corresponde;
- Event Log usa `RecordId` natural;
- Finding ID incorpora severidad;
- cache de IDs es acotado y con TTL;
- test confirma que Warning y Critical del mismo fingerprint se persisten ambos.

## H-05 — Umbral absoluto de espacio estaba anulado

**Estado: RESUELTO.**

Se creó `StorageCapacityPolicy` como única fuente de decisión. Se combinan porcentaje y GB absolutos. Un volumen de 8 TB con 1.2 TB libres ya no se marca Warning solo por estar al 15% libre.

La UI dejó de reimplementar umbrales.

## H-06 — Mounted folders no participaban de Health/Observability

**Estado: RESUELTO ESTÁTICAMENTE; REQUIERE VALIDACIÓN RUNTIME.**

`WindowsVolumeProvider` consulta `Win32_MountPoint`, resuelve `Directory` ↔ `Volume` y añade `MountPaths`. Un volumen fijo sin letra pero con mount path puede clasificarse como `Data`.

Debe validarse en Windows Server con un volumen real montado en carpeta.

## H-07 — VBS y nested virtualization no estaban modelados

**Estado: RESUELTO EN MODELO/TESTS; REQUIERE VALIDACIÓN RUNTIME.**

Ahora se separan señales de:
- guest virtual;
- host Hyper-V;
- VBS de Windows;
- nested virtualization;
- plataforma padre.

Casos añadidos a pruebas:
- Windows 11 físico + VBS;
- Windows Server físico + VBS;
- VMware guest;
- Hyper-V nested guest;
- nested cluster guest.

## H-08 — Fallo del probe de servicios hacía fail-open

**Estado: RESUELTO.**

`ServiceProbeSucceeded=false` degrada confianza y fuerza `MAXIMUM_PROTECTION`. No se interpreta una consulta fallida como “el rol no existe”.

## H-09 — UI afirmaba `Protegido = Sí` sin auditoría de seguridad

**Estado: RESUELTO.**

La tarjeta ahora dice:

- `Modo protegido WSG`
- `Activo`
- política conservadora activa / sin remediación automática.

No afirma que Defender/Firewall/BitLocker estén sanos antes de implementar Security Posture.

## H-10 — Findings perdían evidencias al agruparse

**Estado: RESUELTO.**

`FindingEngine.MergeGroup` conserva:
- severidad máxima;
- unión de `EvidenceIds`;
- FirstSeen/LastSeen;
- OccurrenceCount;
- entidades afectadas;
- explicación de cantidad de evidencias correlacionadas.

---

# Hallazgos medios originales

| ID | Estado 0.2.2 | Acción |
|---|---|---|
| M-01 APIs CPU/RAM podían devolver 0 sano | RESUELTO | ahora lanzan excepción y collector queda Failed |
| M-02 caches `_seen*` ilimitadas | RESUELTO | `RecentIdCache` con TTL y límites |
| M-03 retention en cada captura | RESUELTO | cadencia de 6 h, retry 30 min si falla |
| M-04 Event Log reescaneaba 30 min / cap 300 | MITIGADO | RecordId incremental + cap 2,000 + alerta de cobertura |
| M-05 WHEA corregido aislado = Warning | RESUELTO | individual Informational; burst >=3 = Warning |
| M-06 SCM sin servicio concreto | RESUELTO | extracción best-effort de nombre + correlation key por servicio |
| M-07 sin ciclo Active/Resolved real | PENDIENTE MEDIO | contrato preparado; transición real se implementará antes/como parte de Incident Engine |
| M-08 trend de disco hasta 24 h | RESUELTO | ventana reciente de 2 h |
| M-09 cleanup materializaba arrays | RESUELTO | streaming acotado |
| M-10 USER_TEMP demasiado implícito | ENDURECIDO | root exacto `Path.GetTempPath`, no Windows, filesystem root prohibido |
| M-11 Dispatcher continuaba después de error desconocido | RESUELTO | registra y cierra de forma segura |
| M-12 health inicial aparecía Critical | RESUELTO | `— / Analizando` neutral |

### Residual M-04

El bookmark de Event Log es incremental en memoria y evita el rescaneo normal. Si se producen **más de 2,000 eventos nuevos relevantes en una sola ventana**, WSG genera una advertencia de cobertura incompleta. La estrategia de backfill durable/bookmark persistente queda pendiente; no es bloqueante para 0.2.2, pero debe resolverse antes de depender de Event Log como fuente exhaustiva para Root Cause en ambientes extremadamente ruidosos.

---

# Hallazgos bajos originales

| ID | Estado |
|---|---|
| L-01 versión 0.1.1 hardcodeada | RESUELTO |
| L-02 condición EventLog duplicada | RESUELTO |
| L-03 lógica de capacidad duplicada | RESUELTO |
| L-04 `async void ShowPage` | RESUELTO; solo quedan handlers WPF legítimos |
| L-05 versionado disperso | RESUELTO mediante `Directory.Build.props` / Assembly metadata |
| L-06 accesibilidad | PENDIENTE BAJO; requiere AutomationProperties/teclado/alto contraste/DPI runtime |

---

# Hallazgos residuales de la reauditoría

## R-M01 — Lifecycle de Findings todavía no es persistente

**Severidad: MEDIA.**

Existe `FindingLifecycleState.Active/Resolved`, pero 0.2.2 aún emite Findings actuales como `Active` y no persiste una transición formal a `Resolved`.

Esto debe cerrarse al construir Incident Engine para distinguir:
- activo;
- recuperado;
- recurrente;
- first/last seen.

## R-M02 — Event Log necesita backfill durable en tormentas extremas

**Severidad: MEDIA.**

Ya no existe el rescaneo completo normal y se detecta truncamiento, pero el bookmark está en memoria. Se requiere bookmark/backfill persistente antes de garantizar cobertura exhaustiva bajo >2,000 eventos nuevos por intervalo.

## R-M03 — JSONL todavía no tiene versionado/migración formal de esquema

**Severidad: MEDIA.**

Los records toleran extensiones por propiedades opcionales, pero no existe envelope con `schemaVersion` ni migrador. Antes de cambios incompatibles en 0.3.x debe añadirse.

## R-M04 — Cambios bruscos del reloj no están normalizados

**Severidad: MEDIA.**

Las métricas usan UTC, pero scheduler/ventanas se basan en `DateTime.UtcNow`. Un cambio significativo de reloj del sistema puede distorsionar temporalmente ventanas/intervalos. Para producción conviene usar monotonic timing para scheduling y UTC solo para timestamps.

## R-L01 — Store autoritativo marcado pero Agent detenido

**Severidad: BAJA.**

Si existe el marcador del Agent pero el servicio queda detenido, la UI mantiene el store compartido en read-only. Es una decisión segura que evita split-brain, pero el histórico no crecerá hasta restaurar el Agent. La UI debería mostrar en una fase posterior “Agent no activo”.

## R-L02 — Accesibilidad y DPI requieren prueba visual real

**Severidad: BAJA.**

XAML es válido, pero faltan pruebas de:
- 1366×768;
- 125/150/200% DPI;
- navegación teclado;
- alto contraste;
- lectores de pantalla/AutomationProperties.

---

# Seguridad del Cleanup Engine

Continúa **ANALYSIS ONLY**.

PASS estático:
- allowlist explícita;
- root exacto USER_TEMP/WINDOWS_TEMP;
- filesystem root bloqueado;
- reparse points omitidos;
- archivos `System` omitidos;
- locks no se fuerzan;
- scan streaming con presupuesto;
- no existe CleanupExecutor destructivo.

Antes de habilitar borrado real siguen siendo obligatorios:
- Restart Manager / lock check final;
- validación TOCTOU final;
- `ExclusionListManager`;
- dry-run formal;
- audit log de cada acción;
- pruebas adversariales de junction/symlink;
- autorización/elevación aislada.

---

# Gate runtime obligatorio en Windows

Ejecutar:

```powershell
dotnet clean .\WindowsSystemGuardian.sln
dotnet restore .\WindowsSystemGuardian.sln
dotnet build .\WindowsSystemGuardian.sln -c Release
dotnet test .\WindowsSystemGuardian.sln -c Release
.\tools\xaml-binding-audit.ps1
.\tools\static-safety-audit.ps1
.\tools\qa-static-audit.ps1
```

Luego:

```powershell
dotnet run --project .\src\WindowsSystemGuardian.UI\WindowsSystemGuardian.UI.csproj -c Release
```

## Matriz runtime mínima

1. Windows 11 físico, VBS OFF.
2. Windows 11 físico, VBS ON.
3. Windows 11 VM Hyper-V.
4. Windows Server 2019 físico/VM.
5. Windows Server 2022 Hyper-V/VMware/Proxmox.
6. Windows Server 2025 físico/VM.
7. Hyper-V Host.
8. Hyper-V Cluster Node.
9. Failover Cluster no-Hyper-V.
10. volumen NTFS/ReFS montado en carpeta.
11. Domain Controller virtual y físico (solo clasificación/observación en esta fase).

## Soak test obligatorio

Agent mínimo 8 h, idealmente 24 h, midiendo:
- CPU promedio/pico;
- working set;
- handles/threads;
- I/O bytes/sec;
- tamaño del store;
- duración de collectors;
- número de fallos;
- ejecución de retention;
- estabilidad del scheduler.

---

# Criterio final

**Estáticamente, 0.2.2 corrige todos los Critical/High de 0.2.0.**

Estado actual:

- Critical abiertos: **0**
- High abiertos: **0**
- Medium residuales: **4**
- Low residuales: **2**
- Operaciones de remediación destructiva: **0**

**Aprobación:** `STATIC GO / RUNTIME PENDING`.

No avanzar a 0.3.0 hasta obtener build/test/auditorías PASS y completar al menos el primer smoke + soak en Windows. Una vez cumplido ese gate, 0.3.0 puede comenzar sobre una Observability Foundation mucho más confiable.
