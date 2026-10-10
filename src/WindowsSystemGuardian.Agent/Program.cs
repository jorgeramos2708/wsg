using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WindowsSystemGuardian.Agent;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Health;
using WindowsSystemGuardian.Core.Observability;
using WindowsSystemGuardian.Core.Correlation;
using WindowsSystemGuardian.Core.Incidents;
using WindowsSystemGuardian.Core.Recommendations;
using WindowsSystemGuardian.Core.Scheduling;
using WindowsSystemGuardian.Persistence;
using WindowsSystemGuardian.Windows.Observability;
using WindowsSystemGuardian.Windows.Backup;
using WindowsSystemGuardian.Windows.Integrity;
using WindowsSystemGuardian.Windows.Recovery;
using WindowsSystemGuardian.Windows.Servicing;
using WindowsSystemGuardian.Windows.Storage;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Windows System Guardian Agent");

builder.Services.AddSingleton<IVolumeProvider, WindowsVolumeProvider>();
builder.Services.AddSingleton<StorageCapacityPolicy>();
builder.Services.AddSingleton<StorageHealthEvaluator>();
builder.Services.AddSingleton<IObservabilityStore>(_ =>
{
    var root = LocalObservabilityStore.GetDefaultAgentRoot();
    ObservabilityDataPathResolver.MarkAgentOwnership(root);
    return new LocalObservabilityStore(root);
});
builder.Services.AddSingleton<IObservabilityCollector, WindowsPerformanceCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsStorageObservabilityCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsEventLogCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsServiceHealthCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsProcessResourceCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsDiskPerformanceCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsNetworkPerformanceCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsFileSystemHealthCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsComponentStoreCheckCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsUpdateHealthCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsRecoveryHealthCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsVssHealthCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsComponentStoreScanCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsSystemFileVerificationCollector>();
builder.Services.AddSingleton<IObservabilityCollector, WindowsFileSystemScanCollector>();
builder.Services.AddSingleton<FindingEngine>();
builder.Services.AddSingleton<RootCauseCorrelationEngine>();
builder.Services.AddSingleton<RecommendationEngine>();
builder.Services.AddSingleton<IncidentEngine>();
builder.Services.AddSingleton<IncidentLifecycleTracker>();
builder.Services.AddSingleton<ObservabilityService>();
builder.Services.AddSingleton<ScanLoadController>();
builder.Services.AddSingleton<DiagnosticScheduler>();
builder.Services.AddHostedService<GuardianWorker>();

await builder.Build().RunAsync();
