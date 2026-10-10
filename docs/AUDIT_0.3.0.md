# Windows System Guardian 0.3.0 — Auditoría profunda de QA

**Fase:** Incident & Root Cause Foundation  
**Objetivo:** observar → detectar → correlacionar → explicar → recomendar → reparar de forma controlada → verificar  
**Veredicto:** **GO ESTÁTICO / RUNTIME PENDIENTE**

## Resumen ejecutivo

La fase 0.3.0 añade correlación determinística, atribución por proceso, Incident Engine, causa probable, recomendaciones estructuradas y ciclo de vida Active → Monitoring → Resolved.

Durante la auditoría no se aceptó la primera implementación tal cual. Se corrigieron varios problemas que podían producir falsa correlación, resolución incorrecta o pérdida de evidencia. Después del hardening, la inspección estática final queda con:

| Control | Resultado |
|---|---:|
| Archivos C# inspeccionados | 105 |
| Archivos XAML | 10 |
| Bindings revisados | 116 |
| Bindings sin `Mode=OneWay` | 0 |
| Tests `[Fact]` presentes | 69 |
| Errores XML/XAML | 0 |
| ProjectReference faltantes | 0 |
| TODO/FIXME/HACK/NotImplementedException | 0 |
| `SearchOption.AllDirectories` productivo | 0 |
| Operaciones destructivas de Windows | 0 |
| `File.Delete` productivo | 1, solo retención JSONL WSG |

**Hallazgos críticos abiertos: 0**  
**Hallazgos altos abiertos: 0**

> Este entorno no dispone de .NET SDK ni runtime WPF de Windows. Por ello el build Release, xUnit, WMI/Event Log y smoke/soak runtime deben ejecutarse en Windows antes de cerrar formalmente la fase.

---

# 1. Correcciones realizadas durante la auditoría

## A-01 — Resolución de incidentes dependía de cobertura global

**Problema original**

El lifecycle podía depender de que coincidieran todos los colectores del scheduler. Con frecuencias diferentes, esto podía impedir resolver incidentes o hacer que la noción de “captura completa” no representara el dominio real del incidente.

**Corrección**

Cada incidente declara `VerificationCollectors`.

Ejemplos:

- Memoria/CPU → `Rendimiento` + `Procesos`
- Storage → `Almacenamiento` + `Rendimiento de disco`
- Storage reliability → `Eventos de Windows`
- Servicios → `Servicios` y/o `Eventos de Windows`
- Red → `Red`

`IncidentLifecycleTracker` solo incrementa `ConsecutiveClearChecks` si todos los colectores requeridos se ejecutaron correctamente.

Se requieren **dos verificaciones limpias consecutivas del dominio**.

**Estado:** RESUELTO.

---

## A-02 — La UI read-only podía influir en resolución autoritativa

Cuando el Agent es propietario del store, la UI puede ejecutar capturas ad-hoc. Esas capturas no deben marcar incidentes persistentes como resueltos.

**Corrección**

`IObservabilityStore.IsReadOnly` + `ReadOnlyObservabilityStore`.

Cuando la UI consume el store autoritativo del Agent:

- puede observar y explicar;
- no escribe histórico;
- no acumula checks de resolución autoritativos.

**Estado:** RESUELTO.

---

## A-03 — Correlación por coexistencia podía mezclar problemas independientes

Se endureció `IncidentEngine`.

Ahora exige:

- familia causal compatible;
- alcance compatible;
- relación temporal explícita cuando aplica.

No se fusionan automáticamente:

- C: y D:;
- dos interfaces de red;
- dos servicios diferentes;
- un crecimiento de proceso antiguo con presión actual;
- WHEA y BugCheck separados por una ventana no razonable.

**Estado:** RESUELTO.

---

## A-04 — Evento genérico de almacenamiento podía atribuirse a un volumen lógico

Un evento `Disk/NTFS` cuya evidencia solo dice “Almacenamiento” no demuestra que el problema pertenezca a C:.

**Corrección**

Para `StorageIoReliability` se exige identidad compatible con el volumen y proximidad temporal.

Si no existe identidad suficiente:

- `StoragePressure` permanece como incidente del volumen;
- `StorageReliability` permanece como incidente independiente.

**Estado:** RESUELTO.

---

## A-05 — Hardware + crash podía crear causalidad solo por presencia

**Corrección**

`HardwareInstability` requiere WHEA + crash/BugCheck temporalmente relacionados.

WHEA sin crash genera:

`HardwareError`

con una explicación explícita de que no está demostrado que haya provocado un reinicio.

**Estado:** RESUELTO.

---

## A-06 — Identidad del incidente cambiaba cuando se enriquecía la evidencia

Un episodio inicialmente:

`MemoryPressure`

podía recibir después atribución por proceso y transformarse en:

`MemoryPressureByProcess`.

Si el fingerprint dependiera del conjunto exacto de Findings, se crearían incidentes distintos.

**Corrección**

La identidad se deriva de:

`familia + alcance estable`

La causa probable puede refinarse sin fragmentar el episodio.

**Estado:** RESUELTO.

---

## A-07 — `StateChangedAtUtc` se reescribía aunque el estado siguiera Active

Esto confundía:

- transición de estado;
- última observación;
- orden de histórico.

**Corrección**

- `StateChangedAtUtc` cambia al entrar en un estado distinto.
- `LastSeenUtc` representa observación reciente.
- selección/persistencia utilizan el timestamp más reciente entre LastSeen / StateChanged / Resolved según corresponda.
- se añadieron tests de transición y carga por ventana temporal.

**Estado:** RESUELTO.

---

## A-08 — Saturación histórica de CPU podía bajar el umbral de procesos actuales

La atribución distribuida permite conservar procesos ≥10% cuando CPU global está saturada. Una saturación global antigua dentro de la ventana podía aplicar ese umbral a un proceso actual aunque la CPU ya estuviera normal.

**Corrección**

La saturación global que habilita el umbral distribuido debe ser **fresca**.

Se añadió test:

`StaleGlobalCpuSaturation_DoesNotLowerProcessThreshold`.

**Estado:** RESUELTO.

---

## A-09 — Selección Top-N por procesos podía perder candidatos de handles/hilos

**Corrección**

`WindowsProcessResourceCollector` toma la unión de:

- top CPU;
- top working set;
- top private bytes;
- top handles;
- top threads;

con límite global acotado.

Además registra cobertura de procesos legibles/no legibles.

**Estado:** RESUELTO.

---

## A-10 — Event Log podía perder backlog >2,000 eventos

**Problema original**

El lector iba de nuevo→antiguo. Al alcanzar el límite avanzaba el cursor al registro más nuevo, de modo que eventos anteriores del backlog podían no volver a analizarse.

**Corrección**

El colector ahora:

1. consulta de **antiguo → nuevo**;
2. usa `EventRecordID > últimoProcesado`;
3. procesa como máximo 2,000 por ejecución;
4. actualiza el cursor al **último realmente procesado**;
5. si queda backlog, genera evidencia `observability:eventlog-backlog`;
6. continúa desde ese punto en la siguiente ejecución;
7. detecta recreación/limpieza del canal comparando el RecordId más reciente.

Esto convierte una tormenta de eventos en **retraso de análisis**, no en salto silencioso del lote pendiente.

Microsoft documenta que Windows Event Log soporta consultas XPath con condiciones sobre System y `TimeCreated[timediff(...)]`, así como operadores relacionales sobre elementos del evento.

**Estado:** RESUELTO durante vida del proceso.

**Residual:** el cursor aún no es durable entre reinicios del Agent; ver M-01.

---

## A-11 — Error de lectura del histórico de incidentes se ocultaba en UI

**Corrección**

La captura actual sigue disponible, pero la UI informa explícitamente si el histórico no pudo leerse.

**Estado:** RESUELTO.

---

# 2. Cobertura funcional auditada

## CPU

Implementado:

- CPU global.
- presión sostenida.
- atribución normalizada por proceso.
- escenarios con un consumidor dominante.
- escenarios distribuidos entre varios procesos.
- freshness guards para evitar atribución basada en datos obsoletos.

## Memoria

Implementado:

- RAM física.
- memoria disponible.
- commit / límite.
- presión sostenida.
- working set por proceso.
- private bytes.
- concentración de RAM por proceso.
- crecimiento temporal de private bytes.
- lenguaje conservador: patrón ≠ fuga confirmada.

## Recursos de proceso

Implementado:

- handles;
- threads;
- crecimiento sostenido;
- identidad PID + StartTime para reducir mezcla por reutilización de PID.

## Disco

Implementado:

- capacidad;
- crecimiento;
- busy percent;
- queue length;
- bytes/s;
- reads/s;
- writes/s;
- mounted volumes;
- Event Log de almacenamiento/filesystem;
- correlación conservadora entre rendimiento y confiabilidad.

## Red

Implementado:

- bytes/s;
- utilización respecto a link speed;
- errores/s;
- descartes/s;
- separación por interfaz.

La propia UI/documentación advierte que link speed local no demuestra congestión de WAN/Internet/switch/hipervisor.

## Windows Reliability

Implementado:

- Kernel-Power 41;
- EventLog 6008;
- BugCheck;
- NTFS 55/98;
- Disk 7/51/153;
- WHEA crítico;
- burst de WHEA corregidos;
- Service Control Manager.

## Servicios

Implementado:

- servicios esenciales configurados como automáticos y detenidos;
- eventos SCM;
- no se reinician automáticamente.

---

# 3. Correlación auditada

El motor evita usar proximidad temporal como única señal.

Familias implementadas:

- Memory
- CPU
- Process Memory
- Process CPU
- Process Resource Growth
- Storage
- Storage Reliability
- Network
- Services
- Hardware
- Generic

Causas probables relevantes:

- `MemoryPressureByProcess`
- `MemoryPressureWithMultipleProcesses`
- `ProcessMemoryGrowth`
- `CpuSaturationByProcess`
- `CpuSaturationWithMultipleProcesses`
- `ProcessCpuLoad`
- `StorageIoReliability`
- `StorageReliability`
- `StoragePressure`
- `HardwareInstability`
- `HardwareError`
- `ProcessResourceGrowth`
- `ServiceFailure`
- `NetworkPressure`
- `WindowsInstability`

Cada causa contiene:

- confidence enum;
- porcentaje de confianza;
- Evidence IDs;
- Finding fingerprints;
- explicación que distingue hecho observado de inferencia.

---

# 4. Recommendation Engine

**PASS de seguridad para esta fase.**

Todas las recomendaciones permanecen:

`AutomationAllowed = false`

No existen acciones productivas para:

- finalizar procesos;
- detener/iniciar servicios;
- DISM;
- SFC;
- CHKDSK repair;
- cambios de firewall;
- cambios de red;
- AD;
- Hyper-V;
- Cluster;
- BCD;
- Windows Update;
- eliminación de datos del sistema.

El plan incluye riesgo, elevación, reboot potencial y criterio de verificación.

---

# 5. Persistencia y seguridad

Continúa vigente el hardening 0.2.x:

- Agent = writer autoritativo en ProgramData.
- UI = reader cuando existe Agent store.
- fallback LocalAppData sin Agent.
- JSONL acotado.
- `TopDirectoryOnly`.
- no follow de reparse points.
- solo archivos administrados `yyyy-MM-dd.jsonl`.
- una sola llamada productiva `File.Delete`, confinada a retención.
- `SearchOption.AllDirectories`: 0.

La carpeta `incidents/` entra en el mismo modelo de retención protegida.

---

# 6. Pruebas

La suite fuente contiene **69 tests `[Fact]`**.

Se añadieron pruebas para, entre otros:

- diferentes volúmenes no se mezclan;
- diferentes NIC no se mezclan;
- diferentes servicios no se mezclan;
- fingerprint estable al refinar causa;
- múltiples consumidores de memoria;
- evento genérico de storage no se atribuye a C:;
- process finding fuera de ventana temporal;
- WHEA + crash dentro/fuera de ventana;
- verificación por colectores requeridos;
- reaparición desde Monitoring;
- transición temporal correcta;
- Warning→Critical persistidos;
- histórico corrupto tolerado;
- timestamp de incidente por última observación/transición;
- proceso antiguo no mantiene finding;
- CPU distribuida;
- CPU global obsoleta no reduce umbral de procesos actuales.

---

# 7. Riesgos residuales abiertos

## M-01 — Cursor Event Log no durable entre reinicios

**Severidad:** MEDIA.

Dentro de una ejecución, backfill por lotes ya es seguro. Pero `_lastRecordId` está en memoria.

Tras reinicio del Agent se utiliza nuevamente el lookback inicial (30 minutos).

**Impacto:** posible falso negativo histórico si el Agent estuvo detenido más tiempo.

**Plan:** persistir bookmark/cursor del canal con identidad del log y validación de recreación.

No afecta el principio “no tocar el sistema”, pero limita exhaustividad histórica.

---

## M-02 — No existe heartbeat independiente del Agent

El marker `.agent-owner` demuestra propiedad del store, no que el servicio esté vivo en ese instante.

**Impacto:** la UI puede utilizar correctamente el histórico autoritativo pero no distinguir por sí sola “Agent instalado pero detenido”.

**Plan:** heartbeat monotónico/UTC con tolerancia y estado `Agent activo / histórico sin actualización`.

---

## M-03 — JSONL sin versionado formal de esquema

Los records actuales se deserializan de forma tolerante y las líneas dañadas se omiten, pero no existe envelope con `schemaVersion`.

**Plan:** añadir envelope/version migration antes de estabilizar formatos de release 1.0.

---

## M-04 — Scheduler usa reloj UTC de pared

Un cambio manual muy grande hacia atrás puede retrasar `nextRunUtc`.

**Plan:** usar tiempo monotónico para cadencias y UTC solo para timestamps persistidos.

---

# 8. Límites funcionales deliberados, no defectos de 0.3.0

Aún no están en esta fase:

- page faults / paging rate;
- latencia de almacenamiento real;
- ETW profundo bajo demanda;
- server role packs (AD/DNS/DHCP/IIS/RDS/SMB/Hyper-V/Cluster);
- filesystem health;
- Component Store / DISM;
- SFC verify;
- Windows Update health;
- Boot/WinRE;
- VSS/backup readiness;
- Security Posture;
- reparación automática.

Estos corresponden a fases posteriores y no se presentan en UI como implementados.

---

# 9. QA estático final

Resultado del auditor equivalente ejecutado sobre el árbol final:

```text
C#                  105
XAML                 10
Bindings            116
Tests [Fact]          69

XAML/XML errors        0
Bindings sin OneWay    0
Project refs faltantes 0
TODO/FIXME/HACK        0
NotImplemented         0

AllDirectories         0
System destructive ops 0
```

El QA estático exige además presencia de:

- verification collectors;
- read-only authority guard;
- temporal correlation guards;
- entity-scope correlation;
- fresh CPU attribution;
- process handles/threads;
- Event Log forward backfill;
- recommendation-only mode.

---

# 10. Gate obligatorio en Windows

Antes de declarar la fase cerrada:

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

Objetivo:

- 0 errores;
- idealmente 0 warnings controlables;
- 69/69 tests;
- 3 auditorías PASS.

Después:

1. UI smoke test.
2. comprobar métricas reales.
3. generar carga controlada CPU/RAM/disco para validar detección.
4. validar Event Log en Windows real.
5. ejecutar soak de Agent mínimo 8 h.
6. reauditar el resultado runtime.

---

# 11. Veredicto

**GO ESTÁTICO para Windows System Guardian 0.3.0.**

No hay Critical/High abiertos en la inspección estática final.

**NO se autoriza todavía avanzar automáticamente a 0.4.0** hasta completar el gate runtime en Windows.

Esta fase deja una base adecuada para que el siguiente bloque —Windows Integrity & Servicing Health— se incorpore sin convertir WSG en una colección de comandos ni confiar en correlaciones débiles.
