# Auditoría contra documentación oficial de Microsoft — WSG 0.8.0

> **Alcance**: revalidar el flujo completo acumulado y auditar la superficie nueva de este ciclo (verificación integrada en el diálogo de remediación). Cada ciclo cierra con esta auditoría.
> **Método**: documentación oficial de Microsoft Learn (revalidación por herencia de 0.5.0/0.6.0/0.7.0 + verificación puntual de la superficie nueva) y revisión del código contra las mismas fuentes.
> **Base**: veredictos vigentes de `docs/AUDIT_MSDOC_0.5.0.md` (WUA ResultCode, DISM CheckHealth/ScanHealth/RestoreHealth, SFC, Win32_Volume, fixes WinRE/VSS), `docs/AUDIT_MSDOC_0.6.0.md` (ServiceController, SCM) y `docs/AUDIT_MSDOC_0.7.0.md` (DispatcherTimer).

## 1. Superficie nueva de 0.8.0

| Afirmación de WSG | Fuente oficial | Veredicto |
|---|---|---|
| La verificación post-reparación usa los diagnósticos read-only ya auditados: DISM `/CheckHealth` (reparación de component store) y SFC `/verifyonly` (reparación de archivos del sistema) | [Repair a Windows Image](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image) · [sfc](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc) | ✅ OK — `/verifyonly` "Scans the integrity of all protected system files, without performing repairs": es exactamente la herramienta de comprobación correcta tras un `/scannow` |
| La ejecución de verificación pasa por el pipeline normal (`CaptureCollectorsAsync`): evidencia → hallazgos → incidentes → ciclo de vida → persistencia | Diseño interno (no depende de MS); el ciclo de vida exige verificación por dominio (regla de 0.4.1, revalidada) | ✅ OK — `IncidentLifecycleTracker` sigue siendo la única fuente del conteo de capturas limpias; sin duplicación |
| El estado del incidente tras la verificación se resuelve por fingerprint (sin inventar transiciones) | Regla de ciclo de vida de 0.4.1 (dos capturas limpias por dominio) | ✅ OK — si el incidente no aparece en la captura, se conserva el estado original |
| Los collectors ejecutados son exactamente los declarados por el incidente (`VerificationCollectors`), con nombres inexistentes reportados y no silenciados | Diseño interno + regla de verificación por dominio de 0.4.1 | ✅ OK |
| Diagnósticos High/manual (SFC `/verifyonly`) ejecutados solo por decisión explícita del operador dentro de este flujo | Diseño de scheduling de 0.4.1 (High nunca corre en la cadencia automática) | ✅ OK — la selección explícita es una acción del operador, no entra al scheduler |

## 2. Hallazgos del ciclo

### OK — El ciclo reparar→verificar queda cerrado sin saltos manuales

Con 0.8.0, tras una remediación exitosa: verificación automática → estado del incidente actualizado en el diálogo (`En verificación` tras la primera captura limpia; `Resuelto` tras la segunda — la segunda puede ser otra pasada de "Verificar de nuevo" o una captura de la cadencia normal). El conteo sigue siendo del `IncidentLifecycleTracker`: **ningún atajo que invente resoluciones**.

### OK — Regresión 0.5.0/0.6.0/0.7.0

179/179 pruebas en verde; las 3 auditorías PASS. Live monitoring sin cambios; remediación con sus 3 acciones sin cambios en su frontera de seguridad (catálogo, runas, ServiceController confinados).

### ⚠️ Parcialmente verificado — Paginación de Learn intermitente (deuda documental heredada)

Las páginas de referencia de `GetSystemTimes`/`GlobalMemoryStatusEx` (0.7.0) y el enum `VSS_WRITER_STATE` (0.5.0) seguían sin resolverse durante esta auditoría; se mantienen marcados como parcialmente verificados con respaldo empírico, sin cambios en este ciclo.

## 3. Fuentes

- [Repair a Windows Image (DISM)](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image) · [sfc](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc) · [ServiceController](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontroller) · [OperationResultCode](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/ne-wuapi-operationresultcode) · [DispatcherTimer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer)
- Auditorías heredadas: 0.5.0, 0.6.0, 0.7.0 (docs/)

## 4. Conclusión

La verificación integrada **no introduce nueva superficie de mutación ni nuevas interpretaciones de salida de Microsoft**: reutiliza exactamente los diagnósticos y el pipeline ya auditados, y el conteo de resolución sigue siendo exclusivamente del ciclo de vida existente. Sin datos erróneos ni falsos positivos conocidos; deudas documentales heredadas sin cambios.
