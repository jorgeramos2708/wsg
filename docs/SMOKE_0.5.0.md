# Smoke Test Manual — WSG 0.5.0 (Controlled Remediation Foundation)

> Estado automatizado al cerrar 0.5.0: build clean **0 errores / 0 advertencias**, **154/154 pruebas**, **3 auditorías PASS** (xaml-binding, static-safety con reglas de elevación, qa-static).
> Lo que sigue requiere ejecución manual elevada en una **VM de pruebas** (no en el equipo del operador).

## Preparación (VM de pruebas)

1. Instalar/copiar WSG 0.5.0 (UI + `WindowsSystemGuardian.PrivilegedHost.exe` en la misma carpeta — el build Release los deja juntos).
2. Verificar que la carpeta contiene ambos exe y que `WindowsSystemGuardian.PrivilegedHost.exe` tiene manifest `requireAdministrator`.

## Escenario A — Remediación desde un incidente real

1. Provocar corrupción controlada del component store (o restaurar un snapshot previo con corrupción conocida).
2. Lanzar WSG **como administrador**; esperar captura e incidentes.
3. En `Incidentes`, localizar el incidente con causa `ComponentStoreCorruption` (estado `Activo`).
   - [ ] El botón **"Reparar…"** es visible SOLO para causas soportadas; incidentes con otras causas no lo muestran.
4. Abrir el flujo de remediación:
   - [ ] Preflight visible con las 5 comprobaciones (elevación, reboot pendiente, en curso, espacio, advertencia de fuente).
   - [ ] El comando mostrado es exactamente `DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English`.
   - [ ] Sin marcar la casilla de aprobación, "Ejecutar reparación" permanece deshabilitado.
5. Marcar aprobación y ejecutar:
   - [ ] Aparece resultado estructurado (veredicto + exit code) al terminar.
   - [ ] El log de auditoría (`%LOCALAPPDATA%\WindowsSystemGuardian\Data\Remediation\remediation-*.jsonl`) contiene registros de preflight, aprobación y ejecución con el mismo requestId.
6. Verificación posterior:
   - [ ] Ejecutar "Correlacionar ahora": el collector de salud del almacén corre; tras **dos** capturas limpias el incidente pasa `Monitoring` → `Resolved`.

## Escenario B — SFC /scannow

1. Con un incidente `SystemFileIntegrity` activo, abrir remediación.
   - [ ] El comando mostrado es exactamente `sfc.exe /scannow`.
2. Ejecutar y validar veredicto + auditoría (igual que A).

## Escenario C — Caminos de bloqueo (fail-closed)

- [ ] WSG **sin admin**: el preflight bloquea con "reinicie WSG como administrador"; no aparece UAC y no se ejecuta nada.
- [ ] Rechazar el UAC al ejecutar: mensaje claro, sin reintento automático, incidente intacto, auditoría registra el fallo.
- [ ] Intentar ejecutar dos remediaciones de la misma acción en paralelo: la segunda se bloquea con "en curso".
- [ ] Eliminar/renombrar `PrivilegedHost.exe`: la remediación se bloquea ("no está desplegado").
- [ ] Corromper el JSON de resultado: la validación de contrato lo rechaza y no se acepta el veredicto.

## No verificado automáticamente (deuda consciente)

- Parseo de DISM/SFC contra salidas reales largas (las pruebas usan fixtures acotados).
- Comportamiento de UAC en entornos con políticas de AppLocker/WDAC restrictivas.
- Rendimiento del streaming de progreso con salidas muy grandes (el tail está limitado a 8192 caracteres).
