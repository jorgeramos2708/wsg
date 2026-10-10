using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class HistoryEvidenceItemViewModel(EvidenceRecord evidence)
{
    public string Time => evidence.TimestampUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string Source => evidence.Source;
    public string Category => evidence.Category.ToString();
    public string Entity => evidence.Entity;
    public string Severity => evidence.Severity switch
    {
        EvidenceSeverity.Critical => "Crítico",
        EvidenceSeverity.Warning => "Advertencia",
        EvidenceSeverity.Healthy => "Buen estado",
        _ => "Información"
    };
    public string Message => evidence.Message;
    public string SeverityColor => evidence.Severity switch
    {
        EvidenceSeverity.Critical => "#FFD92D2D",
        EvidenceSeverity.Warning => "#FFE99A00",
        EvidenceSeverity.Healthy => "#FF269447",
        _ => "#FF3578D4"
    };
}
