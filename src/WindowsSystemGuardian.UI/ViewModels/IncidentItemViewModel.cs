using System.Windows;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class IncidentItemViewModel
{
    private readonly Action<Incident>? _remediationOpener;

    public IncidentItemViewModel(Incident incident, Action<Incident>? remediationOpener = null)
    {
        Incident = incident;
        _remediationOpener = remediationOpener;
        AffectedEntitiesText = incident.AffectedEntities.Count == 0
            ? "No determinado"
            : string.Join(", ", incident.AffectedEntities);
        VerificationText = incident.VerificationCriteria.Count == 0
            ? "Recopilar nueva evidencia y confirmar que el síntoma no reaparece."
            : string.Join(" · ", incident.VerificationCriteria);
        Recommendations = incident.Recommendations
            .OrderBy(r => r.Order)
            .Select(r => new RecommendationItemViewModel(r))
            .ToList();
    }

    public Incident Incident { get; }
    public string Title => Incident.Title;
    public string Summary => Incident.Summary;
    public string Impact => Incident.Impact;
    public string RootCauseTitle => Incident.RootCause.Title;
    public string RootCauseExplanation => Incident.RootCause.Explanation;
    public string Confidence => $"{Incident.RootCause.ConfidencePercent}% · {TranslateConfidence()}";
    public string AffectedEntitiesText { get; }
    public string VerificationText { get; }
    public IReadOnlyList<RecommendationItemViewModel> Recommendations { get; }
    public string Severity => Incident.Severity == IncidentSeverity.Critical ? "Crítico" : "Advertencia";
    public string SeverityColor => Incident.Severity == IncidentSeverity.Critical ? "#FFD92D2D" : "#FFE99A00";
    public string State => Incident.State switch
    {
        IncidentState.Active => "Activo",
        IncidentState.Monitoring => "En verificación",
        _ => "Resuelto"
    };
    public string StateColor => Incident.State switch
    {
        IncidentState.Active => SeverityColor,
        IncidentState.Monitoring => "#FF2563A8",
        _ => "#FF269447"
    };
    public string Time => Incident.ResolvedAtUtc.HasValue
        ? $"{Incident.FirstSeenUtc.ToLocalTime():dd/MM HH:mm} → resuelto {Incident.ResolvedAtUtc.Value.ToLocalTime():dd/MM HH:mm}"
        : $"{Incident.FirstSeenUtc.ToLocalTime():dd/MM HH:mm} → {Incident.LastSeenUtc.ToLocalTime():dd/MM HH:mm}";
    public string Occurrences => Incident.OccurrenceCount.ToString("N0");
    public string RootCauseKind => Incident.RootCause.Kind;

    /// <summary>
    /// 0.6.0: la visibilidad del botón la decide la política (fuente única de causas
    /// soportadas), no una lista duplicada en la UI.
    /// </summary>
    public bool CanRemediate => Incident.State == IncidentState.Active &&
        RemediationPolicyEngine.IsRemediableRootCause(Incident.RootCause.Kind);

    public Visibility RemediationVisibility => CanRemediate
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void OpenRemediation() => _remediationOpener?.Invoke(Incident);

    private string TranslateConfidence() => Incident.RootCause.Confidence switch
    {
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.High => "confianza alta",
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.Medium => "confianza media",
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.Low => "confianza baja",
        _ => "confianza no determinada"
    };
}

public sealed class RecommendationItemViewModel(RecommendationStep step)
{
    public int Order => step.Order;
    public string Title => step.Title;
    public string Detail => step.Detail;
    public string Risk => step.Risk switch
    {
        RecommendationRisk.Informational => "Informativo",
        RecommendationRisk.Low => "Riesgo bajo",
        RecommendationRisk.Medium => "Riesgo medio",
        RecommendationRisk.High => "Riesgo alto",
        _ => "Bloqueado"
    };
    public string Approval => step.AutomationAllowed
        ? "Automatizable con política"
        : "No se ejecuta automáticamente";
    public string Verification => step.Verification;
}
