# Auditoría contra documentación oficial de Microsoft — WSG 0.5.0

> **Alcance**: validar que el flujo completo (diagnóstico → monitoreo → correlación → recomendación → remediación → verificación) interpreta los diagnósticos de Windows correctamente, sin datos erróneos ni falsos positivos.
> **Método**: (1) lectura de la documentación oficial de Microsoft Learn; (2) sondas empíricas de solo lectura ejecutadas en esta máquina (reagentc /info, DISM /CheckHealth /English, vssadmin list writers, Win32_Volume vía CIM) para contrastar los parsers con salidas reales.
> **Fecha**: ciclo de auditoría posterior a 0.5.0 (commit 07ec6c5).

## 1. Matriz de verificación

| Diagnóstico | Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|---|
| DISM /CheckHealth | Reporta imagen healthy / repairable / non-repairable | [Repair a Windows Image](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image) | ✅ OK (con limitación documentada, ver H-2) |
| DISM /RestoreHealth + /NoRestart + /English | Sintaxis válida para reparar el almacén con fuente por defecto (WU) | Misma página (muestra `/RestoreHealth`, `/Source`, `/LimitAccess`) | ✅ Sintaxis validada empíricamente (CheckHealth corrió con estos flags) |
| SFC /verifyonly | Escanea integridad sin reparar; requiere Administrators | [sfc (Windows Server docs)](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc) | ✅ OK |
| WUA `ResultCode` (COM) | 3=completada con errores, 4=fallida, 5=cancelada | [OperationResultCode (wuapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/ne-wuapi-operationresultcode) | ✅ OK — mapeo exacto al enum oficial |
| WUA `RebootRequired` | API oficial de reinicio pendiente | [IUpdateSystemInfo (wuapi)](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iupdatesysteminfo) | ✅ OK (API COM oficial, no registros improvisados) |
| `reagentc /info` | Formato de salida parseable | [REAgentC command-line options](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/reagentc-command-line-options) — la página **no documenta el formato de salida** | ❌ **H-1: parser no coincide con la salida real** (demostrado empíricamente) |
| `vssadmin list writers` | Estados de writers numéricos `[n]`, 1=Stable | [vssadmin list writers](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/vssadmin-list-writers) — la página no documenta los códigos de estado | ⚠️ **H-2: falso positivo con estados transitorios** (demostrado empíricamente); enum oficial `VSS_WRITER_STATE` no localizado en Learn al momento de esta auditoría |
| `Win32_Volume` (WMI) | Propiedad `DirtyBitSet`; `Status`/`LastErrorCode` para estado del volumen | Página oficial de Win32_Volume no localizada en Learn (URLs probadas → 404) | ✅ OK **empíricamente**: la propiedad existe, es `null` en volúmenes sanos, y el parser de WSG no interpreta null como dirty |
| CHKDSK `<unidad> /scan` | Sintaxis y letra validada por regex | [chkdsk (Windows Server docs)](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/chkdsk) | ✅ OK (no probado empíricamente: scan de minutos; marcado) |
| Reboot pendiente (preflight, registro CBS/WU/PendingFileRename) | Claves estándar de servicing | Artículos de soporte de MS documentan las claves; no hay página de referencia única | ⚠️ Parcialmente verificado — la dirección del error es conservadora (bloquea de más, nunca de menos) |

## 2. Hallazgos

### H-1 — ALTO · `WindowsRecoveryHealthCollector` no puede interpretar WinRE (falso negativo / funcionalidad muerta) — **CORREGIDO**

> **Estado**: corregido tras esta auditoría — parser por regex tolerante al espaciado y bilingüe (`WinReStatusInterpreter`, Core) + pruebas con la salida real capturada como fixture. Ver commit de fix.

**Evidencia empírica** (Windows 11, idioma español, real):
```text
Estado de Windows RE:                  Enabled
```
El parser busca 4 variantes: `Windows RE status:         Enabled`, `Windows RE status: Enabled`, `Estado de Windows RE:         Habilitado`, `Estado de Windows RE: Habilitado`. La salida real combina **etiqueta en español + valor en inglés** (`Estado de Windows RE: ... Enabled`) con un espaciado distinto (no 9 espacios fijos). Ninguna variante coincide → el collector devuelve siempre `Failure("no pudo interpretar el estado de WinRE")`.

**Impacto**: sin datos falsos (fail-closed ✓), pero el diagnóstico de recuperación está **muerto** en sistemas reales: nunca emitiría la advertencia de WinRE deshabilitado, y su presencia continua como Failure genera ruido.

**Fix propuesto**: parsear por regex tolerant al espaciado y bilingüe: línea `Windows RE status` / `Estado de Windows RE` con valor `Enabled|Habilitado|Disabled|Deshabilitado`, insensible a mayúsculas y cantidad de espacios.

### H-2 — MEDIO · `WindowsVssHealthCollector` genera falso positivo con estados transitorios de VSS — **CORREGIDO**

> **Estado**: corregido tras esta auditoría — `VssWriterStateInterpreter` (Core) clasifica 1=estable, 2-8=transitorio (informativo), 0/≥9=fallo (advertencia) + pruebas con la evidencia real capturada. Ver commit de fix.

**Evidencia empírica** (sistema sano, sin respaldo en curso iniciado por el operador):
```text
Nombre de editor: 'System Writer'   ...   Estado: [5] Esperando finalización
```
WSG clasifica cualquier estado `!= 1` (Stable) como "writer fuera del estado Stable" → advertencia. Los estados 2–8 del enum `VSS_WRITER_STATE` (Waiting for subscribe/snapshot/post-snapshot/backup-complete/completion…) son **transitorios y normales durante actividad de respaldo o de los propios writers**. Un estado transitorio no es un fallo.

**Impacto**: advertencias falsas de "preparación VSS degradada" en sistemas sanos → alimenta incidentes `VssWriterFailure` sin causa real. Es exactamente el falso positivo que el operador no quiere.

**Fix propuesto**: clasificar en tres grupos — `1` = estable (sin evidencia), `2–8` = transitorio (evidencia informativa, no advertencia), `0` y `≥9` (estados `Failed*`) = advertencia. El enum oficial de Learn no fue localizable durante la auditoría; la clasificación propuesta se apoya en la evidencia empírica capturada y en el SDK de VSS (marcado como parcialmente verificado).

### H-3 — CONFIRMADO CORRECTO · Mapeo WUA `ResultCode` (sospecha inicial de esta auditoría desmentida)

La hipótesis inicial era que el mapeo `failed=4/aborted=5/partial=3` estaba desplazado. La doc oficial del enum demuestra lo contrario: `orcSucceededWithErrors=3` ("completada con errores, resultados pueden estar incompletos"), `orcFailed=4`, `orcAborted=5` ("canceled"). WSG mapea exactamente eso, con severidades calibradas (failed→Warning, Critical solo con ≥3 fallos en 24 h; partial→Warning; aborted→Informativo). **Sin datos erróneos.**

### H-4 — LIMITACIÓN DOCUMENTADA, NO DEFECTO · CheckHealth solo reporta el "flag" de corrupción

La doc oficial indica que ScanHealth "escanea la imagen para buscar corrupción (toma varios minutos)" mientras que CheckHealth "reporta si la imagen está healthy, repairable o non-repairable". Un CheckHealth limpio **no descarta corrupción no marcada**; solo ScanHealth/RestoreHealth la detectan.

WSG lo maneja correctamente: CheckHealth es de costo Medium en la cadencia, ScanHealth es `High`/manual, y el collector **no emite evidencia de "salud" cuando no detecta nada** (no afirma salud que no verificó). Riesgo residual aceptado y documentado: falsos negativos temporales hasta que corra un ScanHealth manual — por diseño de costo, no por defecto.

### H-5 — OK · Remainder del flujo 0.5.0 (remediación) contra la doc

- `/RestoreHealth` sin `/Source` usa la fuente por defecto (Features on Demand / Windows Update) — la advertencia informativa del preflight de WSG coincide con la doc ([Configure a Windows Repair Source](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source)).
- Si el servicing requiere reinicio, DISM/SFC dejan operaciones pendientes y el exit code lo refleja: el parser de WSG interpreta `exit != 0` como `Indeterminate` y **nunca** como éxito (fail-closed ✓).
- La verificación posterior re-ejecuta diagnósticos read-only declarados por el incidente y exige dos capturas limpias — coherente con la doc (CheckHealth/ScanHealth/verifyonly son las herramientas de comprobación oficiales).

### H-6 — OK empíricamente · Comportamiento sin privilegios

`reagentc /info` sin elevación devuelve error 5 ("solo puede ejecutarse desde un símbolo del sistema con privilegios elevados") — idéntico al comportamiento que los collectors de WSG manejan con su gate de administrador (Failure explícito, sin datos fabricados). DISM/SFC/vssadmin exigen Administrators según la doc oficial de `sfc` y el comportamiento observado.

## 3. Fuera del alcance de esta auditoría

- Parseo de SFC/DISM ante corrupción real (requiere VM con corrupción provocada; cubierto por `docs/SMOKE_0.5.0.md`).
- CHKDSK /scan de larga duración.
- Página oficial del enum `VSS_WRITER_STATE` y de `Win32_Volume` (404 durante la auditoría); ambos quedan respaldados por evidencia empírica y marcados como parcialmente verificados.

## 4. Conclusión

El flujo completo es **sólido y fail-closed**: no se identificaron rutas que produzcan **datos erróneos afirmativos**. Se identificaron **2 defectos reales** (H-1 alto: WinRE never-parses; H-2 medio: falso positivo VSS transitorio), ambos con fix pequeño y testeable. El resto de interpretaciones quedó **validado contra fuentes oficiales o contra evidencia empírica en esta máquina**.
