# Auditoría contra documentación oficial de Microsoft — WSG 0.6.0

> **Alcance**: revalidar el flujo completo acumulado (diagnóstico → monitoreo → correlación → remediación 0.5.0/0.6.0 → verificación) y auditar la superficie nueva de este ciclo (control de servicios con `ServiceController`). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn + sondas empíricas de solo lectura en esta máquina (`sc query wuauserv`, `sc query bits`, `sc qc`; las sondas destructivas de stop/start quedan para la VM de smoke test, nunca en el equipo del operador).
> **Base**: los veredictos de 0.5.0 siguen vigentes (ver `docs/AUDIT_MSDOC_0.5.0.md` — WUA ResultCode, DISM CheckHealth/ScanHealth/RestoreHealth, SFC, Win32_Volume, fix de WinRE y de VSS transitorios).

## 1. Superficie nueva de 0.6.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| `ServiceController` permite conectar, consultar `Status`, `Stop()`, `Start()` y `WaitForStatus(status, timeout)` de un servicio Windows | [ServiceController Class (System.ServiceProcess)](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontroller) | ✅ OK — API y semántica exactas usadas por el runner de PrivilegedHost |
| `Stop()` también detiene servicios dependientes | Misma página: "Stops this service and any services that are dependent on this service" | ✅ Documentado; mitigación: dependientes de `bits`/`wuauserv` son demand-start (se rearman al usarse) y la verificación posterior reconfirma la salud |
| `WaitForStatus(status, timeout)` espera hasta el estado o expira | Misma página (overload con `TimeSpan`) | ✅ OK — timeout por paso (60 s) aprobado en spec; la expiración lanza `TimeoutException` → capturada → restauración best-effort → resultado `Indeterminate` (fail-closed) |
| `Start()`/`Stop()` requieren administrador | Misma página + PrivilegedHost es `requireAdministrator` | ✅ OK |
| `ServiceController` es `IDisposable` | Misma página (implementa `IDisposable`) | ✅ OK — uso con `using` en todos los pasos |
| Servicios `wuauserv` (Windows Update) y `bits` (Background Intelligent Transfer Service) | Nombres confirmados por la API del SCM: sondea empírico `sc query wuauserv` y `sc query bits` en esta máquina | ✅ OK (ambos existen; `bits` RUNNING/STOPPABLE; `wuauserv` STOPPED — normal: es demand-start y el runner maneja "ya estaba detenido") |
| Control de servicios confinado a PrivilegedHost | Regla nueva en `tools/static-safety-audit.ps1` | ✅ PASS — `ServiceController` no aparece fuera del runner autorizado |
| El chequeo de espacio del preflight no aplica a esta acción | Diseño de la spec 0.6.0 (AC3): un reinicio de servicios no consume disco | ✅ Probado (test con 0 bytes libres → Allowed con detalle "No aplica") |

## 2. Hallazgos del ciclo

### OK — Mapeo de la política a 3 acciones y UI guiada por política

`RemediationPolicyEngine.IsRemediableRootCause` es ahora la fuente única de causas soportadas (policía y visibilidad del botón "Reparar…"), eliminando la duplicación del ViewModel. `WindowsUpdateFailure` → `WindowsUpdateServiceRestart`; `ComponentStoreNonRepairable` y el resto siguen bloqueados.

### OK — Fail-closed ante fallo de servicio

Si `Stop`/`WaitForStatus`/`Start` de cualquier servicio lanza (timeout, servicio no disponible, dependencias), el runner: registra el fallo, intenta restauración best-effort (garantiza que WU no quede muerto) y devuelve exit 4 → veredicto `Indeterminate`. Nunca se interpreta un reinicio parcial como éxito (marcador `WU_RESTART_OK` solo tras los 4 pasos).

### ⚠️ Parcialmente verificado — Comportamiento real de stop/start (deuda consciente)

La ejecución real de stop/start de `bits`/`wuauserv` no se probó en el equipo del operador (mutación de estado; corresponde a la VM de smoke test). Lo validado: nombres de servicio, estados actuales (empírico), API y semántica (doc oficial), timeouts, restauración best-effort y confinamiento estático. El smoke test de `docs/SMOKE_0.5.0.md` se amplía con el escenario C-2 (reinicio de servicios WU) para la VM.

### OK — Regresión 0.5.0

Las 2 acciones de 0.5.0 y sus veredictos siguen en verde (175/175 pruebas; auditorías PASS). La acción `ComponentStoreNonRepairable` sigue bloqueada; `PendingServicingReboot` sigue sin acción (correcto: ya existe evidencia de reinicio pendiente — el preflight bloquearía además por reboot pendiente).

## 3. Fuentes

- [ServiceController Class (System.ServiceProcess) — .NET API](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontroller)
- [OperationResultCode (wuapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/ne-wuapi-operationresultcode) (revalidado en este ciclo)
- [Repair a Windows Image (DISM)](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image) (revalidado)
- [sfc](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc) · [vssadmin list writers](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/vssadmin-list-writers) · [chkdsk](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/chkdsk) (revalidados por referencia a 0.5.0)

## 4. Conclusión

El ciclo 0.6.0 se valida **contra la documentación oficial de la API de servicios de .NET y del SCM**, sin datos erróneos ni falsos positivos conocidos. La deuda consciente queda limitada al smoke test de stop/start en VM (documentado y pendiente del operador).
