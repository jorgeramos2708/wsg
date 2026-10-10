using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;
using WindowsSystemGuardian.UI.Views;

namespace WindowsSystemGuardian.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DashboardView _dashboardView;
    private readonly ObservabilityView _observabilityView;
    private readonly IncidentsView _incidentsView;
    private readonly IntegrityView _integrityView;
    private readonly StorageView _storageView;
    private readonly CleanupView _cleanupView;
    private readonly DiagnosticsView _diagnosticsView;
    private readonly HistoryView _historyView;

    public MainWindow(MainViewModel viewModel)
    {
        StartupDiagnostics.Write("MainWindow constructor entered.");
        InitializeComponent();
        StartupDiagnostics.Write("MainWindow.InitializeComponent completed.");

        _viewModel = viewModel;
        DataContext = viewModel;

        _dashboardView = new DashboardView { DataContext = viewModel.Dashboard };
        _observabilityView = new ObservabilityView { DataContext = viewModel.Observability };
        _incidentsView = new IncidentsView { DataContext = viewModel.Incidents };
        _integrityView = new IntegrityView { DataContext = viewModel.Integrity };
        _storageView = new StorageView { DataContext = viewModel.Storage };
        _cleanupView = new CleanupView { DataContext = viewModel.Cleanup };
        _diagnosticsView = new DiagnosticsView { DataContext = viewModel.Diagnostics };
        _historyView = new HistoryView { DataContext = viewModel.History };

        _dashboardView.NavigateRequested += async (_, destination) =>
            await NavigateToAsync(destination);

        PageHost.Content = _dashboardView;

        Loaded += async (_, _) =>
        {
            StartupDiagnostics.Write("MainWindow Loaded event fired.");
            await _viewModel.Dashboard.RefreshAsync();
            StartupDiagnostics.Write("Initial observability refresh completed.");
        };
    }

    private async void NavigationList_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PageHost is null ||
            NavigationList.SelectedItem is not ListBoxItem item)
            return;

        await ShowPageAsync(item.Tag?.ToString());
    }

    private async Task NavigateToAsync(string destination)
    {
        var target = NavigationList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                destination,
                StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            NavigationList.SelectedItem = target;
            return;
        }

        await ShowPageAsync(destination);
    }

    private async Task ShowPageAsync(string? destination)
    {
        PageHost.Content = destination switch
        {
            "observability" => _observabilityView,
            "incidents" => _incidentsView,
            "integrity" => _integrityView,
            "storage" => _storageView,
            "cleanup" => _cleanupView,
            "diagnostics" => _diagnosticsView,
            "history" => _historyView,
            _ => _dashboardView
        };

        if (string.Equals(destination, "history", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.History.RefreshAsync();
        }
        else if (string.Equals(destination, "incidents", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.Incidents.RefreshAsync();
        }
        else if (string.Equals(destination, "integrity", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.Integrity.RefreshQuickAsync();
        }
    }
}
