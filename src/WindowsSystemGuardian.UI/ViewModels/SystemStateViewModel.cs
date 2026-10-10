using System.Collections.ObjectModel;
using WindowsSystemGuardian.Contracts.Dashboard;
using WindowsSystemGuardian.Contracts.Health;
using WindowsSystemGuardian.Core.Dashboard;
using WindowsSystemGuardian.Core.Health;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class SystemStateViewModel(
    SystemSnapshotService snapshotService,
    StorageHealthEvaluator storageHealthEvaluator) : ObservableObject
{
    private DashboardSnapshot? _snapshot;
    private bool _isBusy;
    private string _statusText = "Inicializando...";

    public ObservableCollection<VolumeItemViewModel> Volumes { get; } = [];

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

    public string MachineName => _snapshot?.Environment.MachineName ?? "Detectando...";
    public string OperatingSystem => _snapshot?.Environment.OperatingSystem ?? "Windows";
    public string Role => _snapshot?.Environment.RoleDisplay ?? "Detectando entorno";
    public string Virtualization => _snapshot?.Environment.VirtualizationDisplay ?? "Detectando virtualización";
    public string Policy => _snapshot?.Environment.PolicyName ?? "MAXIMUM_PROTECTION";
    public string Domain => _snapshot?.Environment.DomainJoined == true
        ? _snapshot.Environment.Domain
        : "No unido a dominio";

    public string DetectionConfidence => _snapshot?.Environment.Confidence switch
    {
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.High => "Alta",
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.Medium => "Media",
        WindowsSystemGuardian.Contracts.Environment.DetectionConfidence.Low => "Baja",
        _ => "No determinada"
    };

    public int StorageScore => _snapshot?.StorageHealth.Score ?? 0;
    public string StorageScoreText => _snapshot is null ? "—" : StorageScore.ToString();

    public string StorageHealth => _snapshot?.StorageHealth.Status switch
    {
        HealthStatus.Healthy => "Buen estado",
        HealthStatus.Warning => "Advertencia",
        HealthStatus.Critical => "Crítico",
        _ => "No determinado"
    };

    public string StorageSummary =>
        _snapshot?.StorageHealth.Summary ?? "Esperando lectura de almacenamiento.";

    public string LastUpdated =>
        _snapshot is null
            ? "—"
            : _snapshot.CapturedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public string VolumeCount => Volumes.Count.ToString("N0");
    public string HealthyVolumeCount => Volumes.Count(v => v.HealthStatus == HealthStatus.Healthy).ToString("N0");
    public string WarningVolumeCount => Volumes.Count(v => v.HealthStatus == HealthStatus.Warning).ToString("N0");
    public string CriticalVolumeCount => Volumes.Count(v => v.HealthStatus == HealthStatus.Critical).ToString("N0");

    // Esta tarjeta describe las barreras internas de WSG, no el estado de Defender/Firewall.
    public string ProtectionState => "Modo protegido WSG";
    public string ProtectionValue => "Activo";
    public string ProtectionSummary => _snapshot is null
        ? "Aplicando política conservadora..."
        : $"Política {Policy} · sin remediación automática";

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Leyendo estado del sistema...";

        try
        {
            _snapshot = await snapshotService.CaptureAsync();
            Volumes.Clear();

            foreach (var volume in _snapshot.Volumes)
            {
                Volumes.Add(new VolumeItemViewModel(
                    volume,
                    storageHealthEvaluator.EvaluateVolume(volume)));
            }

            NotifySnapshotProperties();
            StatusText = "Estado actualizado correctamente.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo completar la lectura: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(MachineName));
        OnPropertyChanged(nameof(OperatingSystem));
        OnPropertyChanged(nameof(Role));
        OnPropertyChanged(nameof(Virtualization));
        OnPropertyChanged(nameof(Policy));
        OnPropertyChanged(nameof(Domain));
        OnPropertyChanged(nameof(DetectionConfidence));
        OnPropertyChanged(nameof(StorageScore));
        OnPropertyChanged(nameof(StorageScoreText));
        OnPropertyChanged(nameof(StorageHealth));
        OnPropertyChanged(nameof(StorageSummary));
        OnPropertyChanged(nameof(LastUpdated));
        OnPropertyChanged(nameof(VolumeCount));
        OnPropertyChanged(nameof(HealthyVolumeCount));
        OnPropertyChanged(nameof(WarningVolumeCount));
        OnPropertyChanged(nameof(CriticalVolumeCount));
        OnPropertyChanged(nameof(ProtectionState));
        OnPropertyChanged(nameof(ProtectionValue));
        OnPropertyChanged(nameof(ProtectionSummary));
    }
}
