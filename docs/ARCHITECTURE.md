# WSG 0.5.0 — Architecture

## Capas

- `Contracts`: Evidence, Findings, Incidents, storage/environment models, remediation models.
- `Core`: policy, observability, Findings, correlation, Incident lifecycle, recommendations, remediation (policy, preflight, approval, engine, verification).
- `Windows`: proveedores Windows, collectors específicos, launcher de elevación.
- `PrivilegedHost`: consola `requireAdministrator` que ejecuta los comandos del catálogo de remediación y emite un resultado JSON estructurado.
- `Persistence`: histórico local autoritativo/read-only + log de auditoría de remediación.
- `Agent`: captura programada (nunca remedia).
- `UI`: ModernWPF.
- `Tests`: lógica Core/Persistence.

## Remediation (0.5.0)

```text
Incidente (ComponentStoreCorruption | SystemFileIntegrity, Active)
    ↓
RemediationPolicyEngine → RemediationPlan (comando del catálogo)
    ↓
PreflightEngine (privilegios, reboot pendiente, en curso, espacio)
    ↓
ApprovalGate (aprobación explícita por requestId, comando exacto)
    ↓
ElevatedProcessLauncher (runas/UAC) → PrivilegedHost (requireAdministrator)
    ↓  comando fijo: DISM /RestoreHealth | SFC /scannow
RemediationOutcome JSON → validación de contrato
    ↓
Verificación (collectors read-only declarados por el incidente)
    ↓
IncidentLifecycleTracker: Monitoring (×1 limpia) → Resolved (×2 limpias)

Auditoría: RemediationRecordStore (JSONL append-only por fase)
```

Límites: solo dos acciones; `ComponentStoreNonRepairable` y cualquier otra causa se rechazan; sin privilegio no se ejecuta; sin aprobación no se ejecuta; el Agent no remedia. La frontera completa está en `docs/SAFETY.md`.

## Integrity & Servicing

```text
WindowsFileSystemHealthCollector ─┐
WindowsComponentStoreCheckCollector│
WindowsUpdateHealthCollector       │
WindowsRecoveryHealthCollector     ├─> Evidence/Metric
WindowsVssHealthCollector          │       ↓
                                   │    Finding
High/manual:                       │       ↓
ComponentStoreScan                 │    Incident
SystemFileVerification             │       ↓
FileSystemScan ────────────────────┘  Root Cause
                                           ↓
                                     Recommendation
```

### External command boundary

```text
Collector
   ↓
ReadOnlyDiagnosticProcessRunner
   ↓ fixed enum
System32 executable + fixed arguments
```

No existe API para enviar una cadena arbitraria a shell/PowerShell/cmd.

## Scheduling

Collectors `Low/Medium` pueden entrar en la cadencia normal. Los diagnósticos `High` se ejecutan únicamente por acción explícita del usuario y nunca desde el scheduler normal.

## Remediation boundary

0.4.1 termina antes de la ejecución:

```text
Evidence → Finding → Incident → Root Cause → Recommendation → Verification Criteria
                                                        X
                                                 no execution
```
