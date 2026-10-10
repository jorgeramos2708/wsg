using System.Collections.ObjectModel;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class IntegrityViewModel(
    ObservabilityService observabilityService) : ObservableObject
{
    private static readonly string[] QuickCollectors =
    [
        "Integridad del sistema de archivos",
        "Salud del almacén de componentes",
        "Salud de Windows Update",
        "Preparación de recuperación WinRE",
        "Preparación VSS / respaldo"
    ];

    private bool _isBusy;
    private string _statusText = "Listo para comprobar la integridad de Windows.";

    public ObservableCollection<IntegrityModuleItemViewModel> Modules { get; } = [];
    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ModuleCount => Modules.Count.ToString("N0");
    public string WarningCount => Modules.Count(m => m.State == "Advertencia").ToString("N0");
    public string CriticalCount => Modules.Count(m => m.State == "Crítico").ToString("N0");

    public Task RefreshQuickAsync() => RunAsync(QuickCollectors, "Comprobando integridad y servicing de bajo/medio impacto...");

    public Task RunComponentStoreScanAsync() =>
        RunAsync(["Escaneo profundo del almacén de componentes"], "Ejecutando DISM /ScanHealth en modo diagnóstico...");

    public Task RunSystemFileVerificationAsync() =>
        RunAsync(["Verificación de archivos protegidos"], "Ejecutando SFC /verifyonly. No se repararán archivos...");

    public Task RunFileSystemScanAsync() =>
        RunAsync(["Escaneo profundo del sistema de archivos"], "Ejecutando CHKDSK /scan sobre volúmenes NTFS elegibles...");

    private async Task RunAsync(IReadOnlyCollection<string> names, string runningText)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusText = runningText;
        try
        {
            var selected = observabilityService.Collectors
                .Where(c => names.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (selected.Count == 0)
            {
                StatusText = "No se encontraron los módulos solicitados.";
                return;
            }

            var snapshot = await observabilityService.CaptureCollectorsAsync(selected);

            foreach (var result in snapshot.Collectors)
                UpsertModule(new IntegrityModuleItemViewModel(result));

            Findings.Clear();
            foreach (var finding in snapshot.Findings
                         .Where(f => f.Category is EvidenceCategory.Integrity or EvidenceCategory.Servicing or EvidenceCategory.Recovery or EvidenceCategory.Backup)
                         .Where(f => f.State == FindingLifecycleState.Active)
                         .Take(40))
            {
                Findings.Add(new FindingItemViewModel(finding));
            }

            OnPropertyChanged(nameof(ModuleCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(CriticalCount));

            StatusText = snapshot.FailedCollectorCount == 0
                ? "Diagnóstico de integridad completado. No se ejecutaron reparaciones."
                : $"Diagnóstico completado con {snapshot.FailedCollectorCount} módulo(s) no disponibles. No se ejecutaron reparaciones.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo completar el diagnóstico: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpsertModule(IntegrityModuleItemViewModel item)
    {
        var existing = Modules.FirstOrDefault(m => string.Equals(m.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Modules.Remove(existing);
        Modules.Add(item);
    }
}
