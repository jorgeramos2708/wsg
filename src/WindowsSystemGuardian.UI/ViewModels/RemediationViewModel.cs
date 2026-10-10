using System.Collections.ObjectModel;
using System.Windows;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
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
/// → ejecución elevada → guía de verificación. El conteo de capturas limpias para
/// resolver el incidente sigue viviendo en el ciclo de vida de incidentes.
/// </summary>
public sealed class RemediationViewModel(
    RemediationPolicyEngine policyEngine,
    PreflightEngine preflightEngine,
    IRemediationPreflightProbe probe,
    RemediationEngine engine) : ObservableObject
{
    private Incident? _incident;
    private RemediationPlan? _plan;
    private bool _operatorApproved;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _outcomeText = string.Empty;
    private string _guidanceText = string.Empty;

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
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    public bool CanExecute => _plan is not null && _operatorApproved && !IsBusy;
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

    public void Initialize(Incident incident)
    {
        _incident = incident;
        _plan = null;
        _operatorApproved = false;
        _outcomeText = string.Empty;
        _guidanceText = string.Empty;
        Checks.Clear();
        Messages.Clear();
        OnPropertyChanged(nameof(IncidentTitle));
        OnPropertyChanged(nameof(RootCauseTitle));
        OnPropertyChanged(nameof(CommandDisplay));
        OnPropertyChanged(nameof(RiskSummary));
        OnPropertyChanged(nameof(PlanAvailable));
        OnPropertyChanged(nameof(CanExecute));

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
                OutcomeText = $"Veredicto de ejecución: {result.Outcome.Verdict} (exit={result.Outcome.ExitCode}).";
                GuidanceText =
                    "Para dar el incidente por resuelto, ejecute 'Correlacionar ahora' y el diagnóstico manual " +
                    "de verificación declarado por el incidente; se requieren dos capturas limpias consecutivas.";
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
}
