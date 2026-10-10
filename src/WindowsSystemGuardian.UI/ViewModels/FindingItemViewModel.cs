using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class FindingItemViewModel(Finding finding)
{
    public string Severity => finding.Severity switch
    {
        FindingSeverity.Critical => "Crítico",
        FindingSeverity.Warning => "Advertencia",
        _ => "Información"
    };

    public string Title => finding.Title;
    public string Explanation => finding.Explanation;
    public string Recommendation => finding.Recommendation;
    public string Entity => finding.AffectedEntity;
    public string Time => finding.DetectedAtUtc.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string Confidence => finding.Confidence switch
    {
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.High => "Alta",
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.Medium => "Media",
        _ => "Baja"
    };
    public bool IsCritical => finding.Severity == FindingSeverity.Critical;
    public bool IsWarning => finding.Severity == FindingSeverity.Warning;
    public string SeverityColor => finding.Severity switch
    {
        FindingSeverity.Critical => "#FFD92D2D",
        FindingSeverity.Warning => "#FFE99A00",
        _ => "#FF3578D4"
    };
}
