using System.Collections.ObjectModel;
using System.Windows;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Observability;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.UI.ViewModels;

/// <summary>Fila de preflight lista para mostrar (símbolo incluido para la vista).</summary>
public sealed class RemediationCheckViewModel(PreflightCheck check)
{
    public string Symbol => check.Passed ? "✓" : "✗";
    public string Name => check.Name;
    public string Detail => check.Detail;
}

/// <summary>Línea de progreso lista para mostrar.</summary>
public sealed class RemediationMessageViewModel(string text)
{
    public string Text => text;
}

/// <summary>
/// Flujo de remediación controlada en la UI: plan → preflight → aprobación explícita
/// → ejecución elevada → verificación integrada (0.8.0: tras una ejecución exitosa el
/// diálogo ejecuta los collectors de verificación declarados por el incidente vía el
/// pipeline normal de ObservabilityService y muestra el estado actualizado según el
/// ciclo de vida).
/// </summary>
public sealed class RemediationViewModel(
    RemediationPolicyEngine policyEngine,
    PreflightEngine preflightEngine,
    IRemediationPreflightProbe probe,
    RemediationEngine engine,
    ObservabilityService observability,
    RemediationVerificationService verificationService) : ObservableObject
{
    private Incident? _incident;
    private RemediationPlan? _plan;
    private RemediationOutcome? _lastOutcome;
    private bool _operatorApproved;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _outcomeText = string.Empty;
    private string _guidanceText = string.Empty;
    private string _incidentStateText = string.Empty;
    private string _verificationSummaryText = string.Empty;

    public ObservableCollection<RemediationCheckViewModel> Checks { get; } = [];
    public ObservableCollection<RemediationMessageViewModel> Messages { get; } = [];

    public string IncidentTitle => _incident?.Title ?? string.Empty;
    public string RootCauseTitle => _incident?.RootCause.Title ?? string.Empty;
    public string CommandDisplay => _plan?.CommandDisplay ?? string.Empty;
    public string RiskSummary => _plan?.RiskSummary ?? string.Empty;

    public bool PlanAvailable => _plan is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanExecute));
                OnPropertyChanged(nameof(CanVerify));
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    public bool CanExecute => _plan is not null && _operatorApproved && !IsBusy;
    public bool CanVerify => _lastOutcome is not null && !IsBusy;
    public bool CanClose => !IsBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OutcomeText
    {
        get => _outcomeText;
        private set => SetProperty(ref _outcomeText, value);
    }

    public string GuidanceText
    {
        get => _guidanceText;
        private set => SetProperty(ref _guidanceText, value);
    }

    public string IncidentStateText
    {
        get => _incidentStateText;
        private set => SetProperty(ref _incidentStateText, value);
    }

    public string VerificationSummaryText
    {
        get => _verificationSummaryText;
        private set => SetProperty(ref _verificationSummaryText, value);
    }

    public void Initialize(Incident incident)
    {
        _incident = incident;
        _plan = null;
        _lastOutcome = null;
        _operatorApproved = false;
        _outcomeText = string.Empty;
        _guidanceText = string.Empty;
        _incidentStateText = string.Empty;
        _verificationSummaryText = string.Empty;
        Checks.Clear();
        Messages.Clear();
        OnPropertyChanged(nameof(IncidentTitle));
        OnPropertyChanged(nameof(RootCauseTitle));
        OnPropertyChanged(nameof(CommandDisplay));
        OnPropertyChanged(nameof(RiskSummary));
        OnPropertyChanged(nameof(PlanAvailable));
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(IncidentStateText));
        OnPropertyChanged(nameof(VerificationSummaryText));

        if (!policyEngine.TryBuildPlan(incident.RootCause.Kind, incident.State, out var plan, out var blockedReason))
        {
            StatusText = blockedReason;
            return;
        }

        _plan = plan;
        var preflight = preflightEngine.Evaluate(plan, probe, remediationAlreadyRunning: false);
        foreach (var check in preflight.Checks)
            Checks.Add(new RemediationCheckViewModel(check));

        StatusText = preflight.Allowed
            ? "Preflight superado. Revise el comando y apruebe esta ejecución concreta."
            : "Preflight bloqueado: " + string.Join(" · ", preflight.BlockingReasons);
    }

    public void SetOperatorApproved(bool approved)
    {
        _operatorApproved = approved;
        OnPropertyChanged(nameof(CanExecute));
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute || _incident is null || _plan is null)
            return;

        IsBusy = true;
        Messages.Add(new RemediationMessageViewModel($"Ejecutando: {_plan.CommandDisplay}"));
        try
        {
            var approval = new RemediationApproval(
                Guid.NewGuid(),
                _plan.Action,
                _plan.CommandDisplay,
                DateTime.UtcNow);

            var result = await engine.ExecuteAsync(
                _incident.RootCause.Kind,
                _incident.State,
                approval);

            foreach (var message in result.Messages)
                Messages.Add(new RemediationMessageViewModel(message));

            if (result.Executed && result.Outcome is not null)
            {
                _lastOutcome = result.Outcome;
                OutcomeText = $"Veredicto de ejecución: {result.Outcome.Verdict} (exit={result.Outcome.ExitCode}).";
                GuidanceText = "La verificación integrada se ejecuta ahora con los diagnósticos declarados por el incidente.";
                OnPropertyChanged(nameof(CanVerify));
                await VerifyNowAsync();
            }
            else
            {
                OutcomeText = "La remediación no se ejecutó. El incidente permanece sin cambios.";
                GuidanceText = string.Empty;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Verificación integrada (0.8.0): ejecuta exactamente los collectors declarados por
    /// el incidente a través del pipeline normal de ObservabilityService (evidencia →
    /// hallazgos → incidentes → ciclo de vida → persistencia) y muestra el estado
    /// actualizado. No duplica el conteo del IncidentLifecycleTracker.
    /// </summary>
    public async Task VerifyNowAsync()
    {
        if (!CanVerify || _incident is null || _lastOutcome is null)
            return;

        IsBusy = true;
        try
        {
            var collectorNames = verificationService.GetVerificationCollectorNames(_incident);
            var selected = observability.Collectors
                .Where(c => collectorNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var missing = collectorNames
                .Where(name => !selected.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (missing.Count > 0)
                Messages.Add(new RemediationMessageViewModel(
                    $"Collectors de verificación no disponibles en esta sesión: {string.Join(", ", missing)}."));

            if (selected.Count == 0)
            {
                VerificationSummaryText = "No hay collectors de verificación ejecutables; el incidente requiere captura manual.";
                OnPropertyChanged(nameof(VerificationSummaryText));
                return;
            }

            Messages.Add(new RemediationMessageViewModel(
                $"Ejecutando verificación: {string.Join(", ", selected.Select(c => c.Name))}…"));

            var snapshot = await observability.CaptureCollectorsAsync(selected);
            var updated = RemediationVerificationService.ResolveUpdatedIncident(_incident, snapshot.Incidents);
            var evaluation = verificationService.Evaluate(_lastOutcome, updated, snapshot.Findings);

            IncidentStateText = DescribeState(updated.State);
            OnPropertyChanged(nameof(IncidentStateText));

            if (evaluation.ExecutionReportedSuccess && evaluation.VerificationClean)
                VerificationSummaryText = "Verificación limpia: sin hallazgos activos del incidente.";
            else
                VerificationSummaryText = "El incidente sigue abierto o persisten hallazgos tras la verificación.";
            OnPropertyChanged(nameof(VerificationSummaryText));

            Messages.Add(new RemediationMessageViewModel($"· {VerificationSummaryText}"));
            foreach (var note in evaluation.Notes)
                Messages.Add(new RemediationMessageViewModel($"· {note}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DescribeState(IncidentState state) => state switch
    {
        IncidentState.Active => "El incidente sigue Activo (verificación no concluyente).",
        IncidentState.Monitoring => "El incidente pasó a En verificación (1 captura limpia); se requiere otra captura limpia para resolverlo.",
        _ => "El incidente quedó Resuelto (2 capturas limpias consecutivas)."
    };
}
