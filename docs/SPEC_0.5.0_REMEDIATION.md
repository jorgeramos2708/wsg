# Spec: WSG 0.5.0 — Controlled Remediation Foundation

> Fuente de intent: `docs/intent/wsg-0.5-remediation.md` (confirmado). Especifica la primera rebanada de remediación: **DISM /RestoreHealth + SFC /scannow** con preflight, aprobación explícita, ejecución elevada aislada y verificación posterior.

## 1. Objective

WSG 0.4.1 observa, detecta, correlaciona y recomienda, pero no ejecuta reparaciones. 0.5.0 añade la **base de remediación controlada** para cerrar el ciclo `recommend → repair (controlado) → verify` limitada a dos acciones, con el mismo enfoque fail-closed del resto del proyecto.

**User story principal**: como operador, ante un Incidente con causa raíz `ComponentStoreCorruption` o `SystemFileIntegrity`, quiero un botón "Reparar" que muestre el preflight, me pida aprobación explícita del comando exacto, ejecute la reparación en un proceso elevado aislado y luego verifique el resultado, de modo que el incidente pase a `Monitoring` solo cuando la verificación lo justifique.

**Acceptance criteria**:

1. AC1 — Solo los Incidentes con causa raíz `ComponentStoreCorruption` o `SystemFileIntegrity` (y estado `Active`) ofrecen la acción de remediación correspondiente; cualquier otra causa no la ofrece.
2. AC2 — El flujo completo es: preflight → pantalla de aprobación (comando exacto visible) → ejecución elevada → verificación → actualización del incidente. Ningún paso se puede saltar.
3. AC3 — Sin privilegios de administrador, el preflight lo reporta y la ejecución no se intenta (sin bucles de UAC, sin auto-elevación).
4. AC4 — La aprobación es explícita, por acción y cada vez; no existe "recordar decisión" ni aprobación en lote.
5. AC5 — Tras la ejecución, la verificación usa los diagnósticos de solo lectura ya existentes (`DISM /CheckHealth`, `SFC /verifyonly`); el incidente pasa a `Monitoring` con una verificación limpia y a `Resolved` con dos (regla de ciclo existente).
6. AC6 — Toda ejecución y verificación queda registrada en el store (log de auditoría append-only) con requestId, acción, preflight, aprobación, resultado y veredicto.
7. AC7 — El gate de QA se mantiene: 0 errores, 0 advertencias, pruebas en verde, 3 auditorías PASS.

## 2. Tech Stack

- Sin cambios: .NET (TFM actual de la solución, `net10.0-windows`), WPF + ModernWpfUI (UI), Microsoft.Extensions.Hosting (Agent), xunit.v3 (tests).
- Nuevo proyecto: `WindowsSystemGuardian.PrivilegedHost` (consola, mismo TFM, `app.manifest` con `requestedExecutionLevel requireAdministrator`).
- Sin dependencias nuevas de NuGet.

## 3. Commands

```powershell
# Build/QA (desde la raíz de la solución)
dotnet clean .\WindowsSystemGuardian.sln
dotnet restore .\WindowsSystemGuardian.sln
dotnet build .\WindowsSystemGuardian.sln -c Release
dotnet test .\WindowsSystemGuardian.sln -c Release

# Auditorías (Set-ExecutionPolicy -Scope Process Bypass)
.\tools\xaml-binding-audit.ps1
.\tools\static-safety-audit.ps1
.\tools\qa-static-audit.ps1

# Ejecutar la UI
dotnet run --project .\src\WindowsSystemGuardian.UI\WindowsSystemGuardian.UI.csproj -c Release
```

**Comandos externos permitidos en 0.5.0** (fijados en código; ni uno más):

| Tipo | Comando | Dónde vive | Timeout |
|---|---|---|---|
| Reparación | `DISM.exe /Online /Cleanup-Image /RestoreHealth /NoRestart /English` | PrivilegedHost | 60 min |
| Reparación | `sfc.exe /scannow` | PrivilegedHost | 45 min |
| Verificación | `DISM.exe /Online /Cleanup-Image /CheckHealth /NoRestart /English` | ReadOnlyDiagnosticProcessRunner (existente) | 5 min (existente) |
| Verificación | `sfc.exe /verifyonly` | ReadOnlyDiagnosticProcessRunner (existente) | existente |

## 4. Project Structure

```
src/
  WindowsSystemGuardian.Contracts/Remediation/
    RemediationActionId.cs        (enum: None, ComponentStoreRepair, SystemFileRepair)
    RemediationPlan.cs            (acción, causa raíz origen, comandos, riesgo)
    PreflightResult.cs            (checks + veredicto Allowed/Blocked con motivos)
    RemediationApproval.cs        (requestId, acción, marca de tiempo, huella del comando)
    RemediationOutcome.cs         (exit code, timedOut, salida acotada, parsed verdict)
    RemediationRecord.cs          (registro append-only de auditoría)
  WindowsSystemGuardian.Core/Remediation/
    RemediationPolicyEngine.cs    (qué acciones permite cada causa raíz/estado; fail-closed)
    PreflightEngine.cs            (privilegios, pending reboot, espacio, proceso en curso)
    ApprovalGate.cs               (exige aprobación explícita vigente por requestId)
    RemediationEngine.cs          (orquesta: plan → preflight → approval → run → verify)
    RemediationVerificationService.cs (mapea diagnósticos de verificación a veredicto)
  WindowsSystemGuardian.PrivilegedHost/
    Program.cs                    (main delgado: parse args → runner → JSON resultado)
    ElevatedRemediationRunner.cs  (los 2 comandos, mismo patrón que ReadOnlyDiagnosticProcessRunner)
    app.manifest                  (requireAdministrator)
  WindowsSystemGuardian.Windows/Remediation/
    ElevatedProcessLauncher.cs    (ÚNICO punto con Verb="runas"; exe fijo + args de enum)
  WindowsSystemGuardian.UI/
    Views/RemediationView.xaml(.cs)  (preflight → aprobación → progreso → verificación)
    ViewModels/RemediationViewModel.cs
tests/
  WindowsSystemGuardian.Core.Tests/
    RemediationPolicyEngineTests.cs
    PreflightEngineTests.cs
    RemediationVerificationTests.cs
    RemediationOutcomeParserTests.cs
docs/
  intent/wsg-0.5-remediation.md   (ya existe)
  SPEC_0.5.0_REMEDIATION.md       (este documento)
  SAFETY.md                       (se actualizará: frontera de elevación)
  ARCHITECTURE.md                 (se actualizará: capa Remediation)
```

## 5. Code Style

Se mantiene el estilo existente: file-scoped namespaces, `internal sealed` para implementaciones, `record` para datos inmutables, `Nullable` activo, XML docs breves en español, nombres explícitos de dominio. Ejemplo del patrón a seguir (contratos):

```csharp
namespace WindowsSystemGuardian.Contracts.Remediation;

/// <summary>Acción de remediación soportada en 0.5.0. Fail-closed: cualquier valor fuera de este enum no ejecuta nada.</summary>
internal enum RemediationActionId
{
    None = 0,
    ComponentStoreRepair,   // DISM /Online /Cleanup-Image /RestoreHealth /NoRestart /English
    SystemFileRepair        // sfc /scannow
}

/// <summary>Resultado estructurado que PrivilegedHost escribe a disco al terminar.</summary>
internal sealed record RemediationOutcome(
    RemediationActionId Action,
    Guid RequestId,
    int ExitCode,
    bool TimedOut,
    string OutputTail,
    RemediationParsedVerdict Verdict,
    DateTime CompletedAtUtc);
```

## 6. Testing Strategy

- Framework: xunit.v3 (existente), proyecto `WindowsSystemGuardian.Core.Tests`.
- La lógica de decisión vive en Core y se prueba sin elevar ni ejecutar procesos reales:
  - `RemediationPolicyEngineTests`: mapea causa raíz→acción; rechaza causas no soportadas, acciones desconocidas, incidentes no `Active`, y cualquier combinación fuera de la tabla (fail-closed).
  - `PreflightEngineTests`: privilegios ausentes/presentes, pending reboot, remediation ya en curso, veredicto bloqueado con motivos.
  - `RemediationVerificationTests`: veredicto limpio/no limpio, mapeo a ciclo de incidentes (`Monitoring` con 1 verificación limpia, `Resolved` con 2).
  - `RemediationOutcomeParserTests`: parseo de salida/exit codes de DISM y SFC (fixtures de texto), timeout, salida vacía.
- El `PrivilegedHost` no se prueba ejecutando DISM real en pruebas automatizadas (requiere admin y minutos); su lógica de parseo vive en Core y su `Program.cs` queda delgado. Se valida con smoke test manual documentado.

## 7. Boundaries

- **Always**:
  - Preflight antes de cada ejecución; aprobación explícita por acción y por requestId; verificación posterior obligatoria.
  - Registrar todo en el log de auditoría append-only.
  - Fail-closed ante cualquier valor, estado o privilegio inesperado.
  - Mantener el gate de QA (0 errores, 0 advertencias, pruebas, 3 auditorías PASS) en cada commit.
- **Ask first** (requiere aprobación del usuario):
  - Añadir una acción de remediación nueva (otro comando).
  - Cambiar el contrato args/JSON de PrivilegedHost.
  - Tocar `ReadOnlyDiagnosticProcessRunner` o las políticas de `SafetyPolicyEngine` existentes.
  - Añadir cualquier dependencia NuGet.
- **Never**:
  - Ejecutar comandos arbitrarios o construir argumentos desde strings externos.
  - Remediación desde el Agent o en background; remediación sin humano presente.
  - Persistir aprobaciones ("recordar decisión") o aprobar en lote.
  - Auto-elevarse en bucle o relanzar UAC automáticamente tras un rechazo.
  - Dar un incidente por `Resolved` sin dos verificaciones limpias.
  - Saltarse auditorías o eliminar pruebas que fallan sin aprobación.

## 8. Success Criteria

1. AC1–AC7 anteriores, verificados con pruebas + smoke test manual documentado (escenario real de remediación en VM de pruebas).
2. `static-safety-audit.ps1` PASS con las nuevas reglas (incluye la excepción de elevación declarada y acotada).
3. Sin regresión: los flujos de 0.4.1 (observabilidad, integridad, incidentes, limpieza) siguen en verde.
4. Documentación actualizada: SAFETY.md (frontera de elevación + formato del log de auditoría), ARCHITECTURE.md (capa Remediation + diagrama del flujo), README (sección 0.5.0 + acciones ahora implementadas vs. aún no implementadas).

## 9. Open Questions

1. **Progreso en vivo**: ¿la UI muestra salida cruda en streaming del proceso elevado (cola de líneas) o solo un spinner + resultado final? *Propuesta por defecto*: cola de líneas acotada (últimas N) + resultado final; sin parseo de % de DISM en 0.5.0.
2. **Fuente de reparación de DISM**: `/RestoreHealth` puede requerir Windows Update/red. *Propuesta por defecto*: el preflight marca "fuente de reparación no garantizada offline" como advertencia informativa; no se añade `/Source` en 0.5.0.
3. **PrivilegedHost firmado**: ¿se valida firma/hash del exe antes de lanzarlo? *Propuesta por defecto*: se valida que la ruta sea la del propio directorio de la aplicación (exe desplegado por el build) y que la versión de ensamblado coincida con la UI; firma Authenticode queda fuera de 0.5.0.

> Estas tres preguntas tienen propuesta por defecto; se implementan así salvo indicación contraria al aprobar la spec.
