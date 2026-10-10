using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class IntegrityModuleItemViewModel
{
    public IntegrityModuleItemViewModel(CollectorResult result)
    {
        Name = result.CollectorName;
        Cost = result.Cost switch
        {
            DiagnosticCost.VeryLow => "Muy bajo",
            DiagnosticCost.Low => "Bajo",
            DiagnosticCost.Medium => "Medio",
            DiagnosticCost.High => "Alto / manual",
            DiagnosticCost.Disruptive => "Disruptivo",
            _ => result.Cost.ToString()
        };
        LastRun = result.CapturedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

        if (!result.Succeeded)
        {
            State = "No disponible";
            StatusColor = "#FF6B7280";
            Detail = result.Error ?? "El módulo no pudo completarse.";
            return;
        }

        var critical = result.Evidence.Count(e => e.Severity == EvidenceSeverity.Critical);
        var warning = result.Evidence.Count(e => e.Severity == EvidenceSeverity.Warning);
        if (critical > 0)
        {
            State = "Crítico";
            StatusColor = "#FFDC2626";
            Detail = result.Evidence.First(e => e.Severity == EvidenceSeverity.Critical).Message;
        }
        else if (warning > 0)
        {
            State = "Advertencia";
            StatusColor = "#FFF59E0B";
            Detail = result.Evidence.First(e => e.Severity == EvidenceSeverity.Warning).Message;
        }
        else
        {
            State = "Buen estado";
            StatusColor = "#FF159447";
            Detail = "La comprobación terminó sin condiciones de advertencia conocidas.";
        }
    }

    public string Name { get; }
    public string State { get; }
    public string StatusColor { get; }
    public string Detail { get; }
    public string Cost { get; }
    public string LastRun { get; }
}
