# Intent — WSG 0.5.0 Controlled Remediation Foundation

> Confirmado con el usuario (entrevista `interview-me`, 2026-10-10). Este documento es la fuente del alcance para 0.5.0.

- **Outcome**: WSG 0.5.0 con remediación controlada — la UI puede ejecutar, con preflight y aprobación explícita, `DISM /RestoreHealth` y `SFC /scannow` contra las causas raíz que 0.4.1 ya detecta (`ComponentStoreCorruption`, `SystemFileIntegrity`), y verificar después.
- **User**: el operador de WSG en Windows 10/11/Server (físico o VM), como administrador cuando toca reparar.
- **Why now**: 0.4.1 detecta y recomienda pero no ejecuta nada; el README nombra esta fase como la siguiente.
- **Success**: de un Incidente con esas causas raíz → preflight → aprobación explícita → ejecución elevada aislada (PrivilegedHost + UAC) → verificación (`DISM /CheckHealth`, `SFC /verifyonly`) → incidente pasa `Monitoring → Resolved`. Gate de QA intacto: 0 errores, 0 advertencias, pruebas y 3 auditorías PASS.
- **Constraint**: fail-closed; solo 2 acciones soportadas; comandos fijados en código (nada arbitrario); PrivilegedHost como ejecutable separado con UAC, **no** servicio permanente.
- **Out of scope**: CHKDSK fix, WinRE, VSS, SoftwareDistribution/Catroot2, BCD, servicios WU, remediación automática sin humano, cualquier acción no listada.
