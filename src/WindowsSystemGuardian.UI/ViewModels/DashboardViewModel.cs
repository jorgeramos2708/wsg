namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class DashboardViewModel(
    SystemStateViewModel system,
    ObservabilityViewModel observability,
    LiveMonitorViewModel liveMonitor)
{
    public SystemStateViewModel System { get; } = system;
    public ObservabilityViewModel Observability { get; } = observability;
    public LiveMonitorViewModel Live { get; } = liveMonitor;

    public async Task RefreshAsync()
    {
        await System.RefreshAsync();
        await Observability.RefreshAsync();
    }
}
