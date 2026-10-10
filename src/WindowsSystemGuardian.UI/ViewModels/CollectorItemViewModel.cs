using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class CollectorItemViewModel(CollectorResult result)
{
    public string Name => result.CollectorName;
    public string Cost => result.Cost switch
    {
        DiagnosticCost.VeryLow => "Muy bajo",
        DiagnosticCost.Low => "Bajo",
        DiagnosticCost.Medium => "Medio",
        DiagnosticCost.High => "Alto",
        _ => "Disruptivo"
    };
    public string Status => result.Succeeded ? "Correcto" : "No disponible";
    public string Detail => result.Succeeded
        ? $"{result.Evidence.Count} evidencias · {result.Metrics.Count} métricas"
        : result.Error ?? "Error no especificado";
    public string Time => result.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss");
}
