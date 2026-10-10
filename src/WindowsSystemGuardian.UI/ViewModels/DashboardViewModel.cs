namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class DashboardViewModel(
    SystemStateViewModel system,
    ObservabilityViewModel observability)
{
    public SystemStateViewModel System { get; } = system;
    public ObservabilityViewModel Observability { get; } = observability;

    public async Task RefreshAsync()
    {
        await System.RefreshAsync();
        await Observability.RefreshAsync();
    }
}
