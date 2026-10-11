using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Cleanup;
using WindowsSystemGuardian.Core.Dashboard;
using WindowsSystemGuardian.Core.Environment;
using WindowsSystemGuardian.Core.Health;
using WindowsSystemGuardian.Core.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Recommendations;
using WindowsSystemGuardian.Core.Safety;
using WindowsSystemGuardian.Core.Remediation;
using WindowsSystemGuardian.Persistence;
using WindowsSystemGuardian.UI.ViewModels;
using WindowsSystemGuardian.UI.Views;
using WindowsSystemGuardian.Windows.Remediation;
using WindowsSystemGuardian.Windows.Cleanup;
using WindowsSystemGuardian.Windows.Backup;
using WindowsSystemGuardian.Windows.Integrity;
using WindowsSystemGuardian.Windows.Recovery;
using WindowsSystemGuardian.Windows.Servicing;
using WindowsSystemGuardian.Windows.Environment;
using WindowsSystemGuardian.Windows.Observability;
using WindowsSystemGuardian.Windows.Storage;

namespace WindowsSystemGuardian.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private int _unhandledDialogShown;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            StartupDiagnostics.WriteException("Dispatcher unhandled exception", args.Exception);
            if (Interlocked.Exchange(ref _unhandledDialogShown, 1) == 0)
            {
                MessageBox.Show(
                    $"Se produjo un error inesperado.\n\n{args.Exception.GetBaseException().Message}\n\nLog: {StartupDiagnostics.LogPath}",
                    "Windows System Guardian",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            // Un error de Dispatcher desconocido puede dejar la UI en estado parcial.
            // Se registra y se cierra de forma segura en lugar de continuar ciegamente.
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                StartupDiagnostics.WriteException("AppDomain unhandled exception", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupDiagnostics.WriteException("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupDiagnostics.Write("App.OnStartup entered.");

        var services = new ServiceCollection();

        services.AddSingleton<IEnvironmentProbe, WindowsEnvironmentProbe>();
        services.AddSingleton<IVolumeProvider, WindowsVolumeProvider>();
        services.AddSingleton<ICleanupRootProvider, WindowsCleanupRootProvider>();
        services.AddSingleton<IFileLockProbe, BasicFileLockProbe>();

        services.AddSingleton<EnvironmentClassifier>();
        services.AddSingleton<StorageCapacityPolicy>();
        services.AddSingleton<StorageHealthEvaluator>();
        services.AddSingleton<SafetyPolicyEngine>();
        services.AddSingleton<SystemSnapshotService>();
        services.AddSingleton<SafeCleanupFileScanner>();
        services.AddSingleton<CleanupAnalysisService>();

        services.AddSingleton<IObservabilityStore>(_ => ObservabilityDataPathResolver.CreateInteractiveStore());
        services.AddSingleton<IObservabilityCollector, WindowsPerformanceCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsStorageObservabilityCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsEventLogCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsServiceHealthCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsProcessResourceCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsDiskPerformanceCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsNetworkPerformanceCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsFileSystemHealthCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsComponentStoreCheckCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsUpdateHealthCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsRecoveryHealthCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsVssHealthCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsComponentStoreScanCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsSystemFileVerificationCollector>();
        services.AddSingleton<IObservabilityCollector, WindowsFileSystemScanCollector>();
        services.AddSingleton<FindingEngine>();
        services.AddSingleton<RootCauseCorrelationEngine>();
        services.AddSingleton<RecommendationEngine>();
        services.AddSingleton<IncidentEngine>();
        services.AddSingleton<IncidentLifecycleTracker>();
        services.AddSingleton<ObservabilityService>();

        // Monitoreo controlada 0.5.0: preflight, aprobación explícita, host elevado y auditoría.
        services.AddSingleton<WindowsRemediationPreflightProbe>();
        services.AddSingleton<IRemediationPreflightProbe>(sp => sp.GetRequiredService<WindowsRemediationPreflightProbe>());
        services.AddSingleton<IElevatedRemediationLauncher, ElevatedProcessLauncher>();
        services.AddSingleton<IRemediationRecordStore, RemediationRecordStore>();
        services.AddSingleton<RemediationPolicyEngine>();
        services.AddSingleton<PreflightEngine>();
        services.AddSingleton<ApprovalGate>();
        services.AddSingleton<RemediationEngine>();
        services.AddSingleton<RemediationVerificationService>();
        services.AddTransient<RemediationViewModel>(sp => new RemediationViewModel(
            sp.GetRequiredService<RemediationPolicyEngine>(),
            sp.GetRequiredService<PreflightEngine>(),
            sp.GetRequiredService<IRemediationPreflightProbe>(),
            sp.GetRequiredService<RemediationEngine>(),
            sp.GetRequiredService<ObservabilityService>(),
            sp.GetRequiredService<RemediationVerificationService>()));

        // Monitoreo en tiempo real 0.7.0: el collector de rendimiento (solo lectura) sin escrituras al store.
        services.AddSingleton<LiveMonitorViewModel>(sp => new LiveMonitorViewModel(
            sp.GetServices<IObservabilityCollector>().First(c => c.Name == "Rendimiento")));

        services.AddSingleton<SystemStateViewModel>();
        services.AddSingleton<ObservabilityViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<IncidentsViewModel>(sp => new IncidentsViewModel(
            sp.GetRequiredService<ObservabilityViewModel>(),
            sp.GetRequiredService<IObservabilityStore>(),
            incident =>
            {
                var viewModel = sp.GetRequiredService<RemediationViewModel>();
                viewModel.Initialize(incident);
                var window = new RemediationView { DataContext = viewModel, Owner = MainWindow };
                window.ShowDialog();
            }));
        services.AddSingleton<IntegrityViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        StartupDiagnostics.Write("Dependency injection container built.");

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        StartupDiagnostics.Write("MainWindow resolved.");

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();
        StartupDiagnostics.Write("MainWindow.Show completed.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupDiagnostics.Write("App.OnExit entered.");
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
