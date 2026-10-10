namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class DiagnosticsViewModel(ObservabilityViewModel observability)
{
    public ObservabilityViewModel Observability { get; } = observability;
}
