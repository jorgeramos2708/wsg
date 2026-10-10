using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>Resultado del flujo de remediación hasta la fase alcanzada.</summary>
public sealed record RemediationFlowResult(
    Guid RequestId,
    RemediationPhase ReachedPhase,
    bool Executed,
    RemediationOutcome? Outcome,
    IReadOnlyList<string> Messages);

/// <summary>
/// Orquesta el flujo completo: política → preflight → aprobación → ejecución elevada.
/// Fail-closed en cada frontera; cada fase escribe su registro de auditoría. La
/// verificación posterior (post-ejecución) vive en <see cref="RemediationVerificationService"/>
/// y el conteo de capturas limpias en el ciclo de vida de incidentes.
/// </summary>
public sealed class RemediationEngine(
    RemediationPolicyEngine policyEngine,
    PreflightEngine preflightEngine,
    ApprovalGate approvalGate,
    IRemediationPreflightProbe preflightProbe,
    IElevatedRemediationLauncher launcher,
    IRemediationRecordStore recordStore)
{
    private readonly object _inFlightSync = new();
    private readonly HashSet<RemediationActionId> _inFlight = new();

    public async Task<RemediationFlowResult> ExecuteAsync(
        string? rootCauseKind,
        IncidentState incidentState,
        RemediationApproval? approval,
        CancellationToken cancellationToken = default)
    {
        var requestId = approval?.RequestId ?? Guid.NewGuid();
        var messages = new List<string>();

        // Fase 1: política. Un bloqueo aquí se registra como preflight: nada se planificó.
        if (!policyEngine.TryBuildPlan(rootCauseKind, incidentState, out var plan, out var blockedReason))
        {
            messages.Add(blockedReason);
            Record(requestId, RemediationActionId.None, RemediationPhase.Preflight, "Bloqueada por política", blockedReason);
            return new RemediationFlowResult(requestId, RemediationPhase.Preflight, Executed: false, null, messages);
        }

        Record(requestId, plan.Action, RemediationPhase.Preflight, "Plan calculado", plan.CommandDisplay);

        // Fase 2: preflight.
        var alreadyRunning = IsInFlight(plan.Action);
        var preflight = preflightEngine.Evaluate(plan, preflightProbe, alreadyRunning);
        Record(requestId, plan.Action, RemediationPhase.Preflight,
            preflight.Allowed ? "Preflight superado" : "Preflight bloqueado",
            string.Join(" | ", preflight.Allowed ? preflight.Checks.Where(c => c.Passed).Select(c => c.Name) : preflight.BlockingReasons));

        if (!preflight.Allowed)
        {
            messages.AddRange(preflight.BlockingReasons);
            return new RemediationFlowResult(requestId, RemediationPhase.Preflight, Executed: false, null, messages);
        }

        // Fase 3: aprobación explícita.
        if (!approvalGate.IsApproved(plan, requestId, approval, out var approvalReason))
        {
            messages.Add(approvalReason);
            Record(requestId, plan.Action, RemediationPhase.Approval, "Aprobación ausente o no coincidente", approvalReason);
            return new RemediationFlowResult(requestId, RemediationPhase.Approval, Executed: false, null, messages);
        }

        Record(requestId, plan.Action, RemediationPhase.Approval, "Aprobación explícita registrada", plan.CommandDisplay);
        messages.Add($"Ejecutando: {plan.CommandDisplay}");

        // Fase 4: ejecución elevada. Una sola remediación por acción a la vez.
        if (!TryMarkInFlight(plan.Action))
        {
            messages.Add("Ya existe una remediación en curso para esta acción.");
            return new RemediationFlowResult(requestId, RemediationPhase.Preflight, Executed: false, null, messages);
        }

        try
        {
            var outcome = await launcher.LaunchAsync(plan, requestId, cancellationToken).ConfigureAwait(false);
            Record(requestId, plan.Action, RemediationPhase.Execution,
                $"Ejecución terminada: {outcome.Verdict}",
                $"exit={outcome.ExitCode}, timedOut={outcome.TimedOut}");
            messages.Add($"Ejecución terminada con veredicto: {outcome.Verdict}.");
            return new RemediationFlowResult(requestId, RemediationPhase.Execution, Executed: true, outcome, messages);
        }
        catch (OperationCanceledException)
        {
            Record(requestId, plan.Action, RemediationPhase.Execution, "Ejecución cancelada por el operador", plan.CommandDisplay);
            messages.Add("Ejecución cancelada por el operador.");
            return new RemediationFlowResult(requestId, RemediationPhase.Execution, Executed: false, null, messages);
        }
        catch (Exception ex)
        {
            Record(requestId, plan.Action, RemediationPhase.Execution, "Fallo al lanzar la ejecución elevada", ex.Message);
            messages.Add($"No se pudo lanzar la ejecución elevada: {ex.Message}");
            return new RemediationFlowResult(requestId, RemediationPhase.Execution, Executed: false, null, messages);
        }
        finally
        {
            UnmarkInFlight(plan.Action);
        }
    }

    private void Record(Guid requestId, RemediationActionId action, RemediationPhase phase, string summary, string detail) =>
        recordStore.Append(new RemediationRecord(DateTime.UtcNow, requestId, action, phase, summary, detail));

    private bool IsInFlight(RemediationActionId action)
    {
        lock (_inFlightSync)
            return _inFlight.Contains(action);
    }

    private bool TryMarkInFlight(RemediationActionId action)
    {
        lock (_inFlightSync)
            return _inFlight.Add(action);
    }

    private void UnmarkInFlight(RemediationActionId action)
    {
        lock (_inFlightSync)
            _inFlight.Remove(action);
    }
}
