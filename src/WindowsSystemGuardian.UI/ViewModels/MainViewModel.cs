using System.ComponentModel;
using System.Reflection;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public MainViewModel(
        SystemStateViewModel system,
        ObservabilityViewModel observability,
        DashboardViewModel dashboard,
        IncidentsViewModel incidents,
        IntegrityViewModel integrity,
        StorageViewModel storage,
        CleanupViewModel cleanup,
        DiagnosticsViewModel diagnostics,
        HistoryViewModel history)
    {
        System = system;
        Observability = observability;
        Dashboard = dashboard;
        Incidents = incidents;
        Integrity = integrity;
        Storage = storage;
        Cleanup = cleanup;
        Diagnostics = diagnostics;
        History = history;

        System.PropertyChanged += SystemOnPropertyChanged;
        Observability.PropertyChanged += ObservabilityOnPropertyChanged;
    }

    public SystemStateViewModel System { get; }
    public ObservabilityViewModel Observability { get; }
    public DashboardViewModel Dashboard { get; }
    public IncidentsViewModel Incidents { get; }
    public IntegrityViewModel Integrity { get; }
    public StorageViewModel Storage { get; }
    public CleanupViewModel Cleanup { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HistoryViewModel History { get; }

    public string LastUpdated => Observability.LastCapture == "—" ? System.LastUpdated : Observability.LastCapture;

    public string VersionText { get; } = BuildVersionText();

    private static string BuildVersionText()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return $"Versión {informational.Split('+')[0]}";

        return $"Versión {assembly.GetName().Version?.ToString(3) ?? "desconocida"}";
    }


    private void SystemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStateViewModel.LastUpdated))
            OnPropertyChanged(nameof(LastUpdated));
    }

    private void ObservabilityOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObservabilityViewModel.LastCapture))
            OnPropertyChanged(nameof(LastUpdated));
    }
}
