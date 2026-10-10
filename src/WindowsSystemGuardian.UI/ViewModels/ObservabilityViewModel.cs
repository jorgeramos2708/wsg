using System.Collections.ObjectModel;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class ObservabilityViewModel(
    ObservabilityService observabilityService) : ObservableObject
{
    private ObservabilitySnapshot? _snapshot;
    private bool _isBusy;
    private string _statusText = "Esperando captura de observabilidad.";

    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];
    public ObservableCollection<IncidentItemViewModel> Incidents { get; } = [];
    public ObservableCollection<CollectorItemViewModel> Collectors { get; } = [];

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

    public bool HasSnapshot => _snapshot is not null;
    public IReadOnlyList<WindowsSystemGuardian.Contracts.Incidents.Incident> CurrentIncidents => _snapshot?.Incidents ?? [];
    public int HealthScore => _snapshot?.HealthScore ?? 0;
    public string HealthScoreText => _snapshot is null ? "—" : HealthScore.ToString();

    public string HealthLabel => _snapshot is null
        ? "Analizando"
        : HealthScore switch
        {
            >= 90 => "Buen estado",
            >= 70 => "Advertencia",
            _ => "Crítico"
        };

    public string HealthColor => _snapshot is null
        ? "#FF6B7280"
        : HealthScore switch
        {
            >= 90 => "#FF269447",
            >= 70 => "#FFE99A00",
            _ => "#FFD92D2D"
        };

    public string WarningCount => (_snapshot?.Incidents.Count(i =>
        i.State != WindowsSystemGuardian.Contracts.Incidents.IncidentState.Resolved &&
        i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Warning) ?? 0).ToString("N0");
    public string CriticalCount => (_snapshot?.Incidents.Count(i =>
        i.State != WindowsSystemGuardian.Contracts.Incidents.IncidentState.Resolved &&
        i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Critical) ?? 0).ToString("N0");
    public string HealthyAreaCount => Math.Max(
        0,
        (_snapshot?.SuccessfulCollectorCount ?? 0) - AffectedAreaCount()).ToString("N0");
    public string EvidenceCount => (_snapshot?.Evidence.Count ?? 0).ToString("N0");
    public string IncidentCount => (_snapshot?.Incidents.Count ?? 0).ToString("N0");
    public string CriticalIncidentCount => (_snapshot?.Incidents.Count(i => i.Severity == WindowsSystemGuardian.Contracts.Incidents.IncidentSeverity.Critical) ?? 0).ToString("N0");
    public string SuccessfulCollectors => (_snapshot?.SuccessfulCollectorCount ?? 0).ToString("N0");
    public string FailedCollectors => (_snapshot?.FailedCollectorCount ?? 0).ToString("N0");

    public string LastCapture => _snapshot is null
        ? "—"
        : _snapshot.CapturedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public double CpuPercent => MetricValue("system.cpu.percent");
    public double MemoryPercent => MetricValue("system.memory.percent");
    public double CommitPercent => MetricValue("system.commit.percent");
    public double DiskBusyPercent => LatestMaxMetricValue("storage.busy_percent");
    public double NetworkUtilizationPercent => LatestMaxMetricValue("network.utilization.percent");
    public string CpuPercentText => _snapshot is null ? "—" : $"{CpuPercent:F0}%";
    public string MemoryPercentText => _snapshot is null ? "—" : $"{MemoryPercent:F0}%";
    public string CommitPercentText => _snapshot is null ? "—" : $"{CommitPercent:F0}%";
    public string DiskBusyPercentText => _snapshot is null ? "—" : $"{DiskBusyPercent:F0}%";
    public string NetworkUtilizationText => _snapshot is null ? "—" : $"{NetworkUtilizationPercent:F0}%";
    public string TopCpuProcess => LatestEntityByMetric("process.cpu.percent", out var cpu)
        is { } cpuEntity ? $"{cpuEntity} · {cpu:F0}%" : "No disponible";
    public string TopMemoryProcess => LatestEntityByMetric("process.memory.working_set_bytes", out var bytes)
        is { } memoryEntity ? $"{memoryEntity} · {bytes / (1024d * 1024d * 1024d):F1} GB" : "No disponible";
    public string ProcessCount => _snapshot is null ? "—" : MetricValue("process.count").ToString("F0");

    public string UptimeText
    {
        get
        {
            if (_snapshot is null)
                return "—";

            var seconds = MetricValue("system.uptime.seconds");
            if (seconds <= 0) return "No disponible";
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalDays >= 1
                ? $"{span.TotalDays:F1} días"
                : $"{span.TotalHours:F1} h";
        }
    }

    public string AutomaticStoppedServices =>
        _snapshot is null ? "—" : MetricValue("services.automatic_stopped").ToString("F0");

    public string RelevantEvents =>
        _snapshot is null ? "—" : MetricValue("events.relevant").ToString("F0");

    public string CollectorSummary => _snapshot is null
        ? "Sin captura"
        : $"{_snapshot.SuccessfulCollectorCount} correctos · {_snapshot.FailedCollectorCount} con error";

    public string PersistenceStatus => _snapshot is null
        ? "Histórico pendiente"
        : _snapshot.PersistenceDegraded
            ? "Persistencia degradada"
            : "Histórico operativo";

    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Recopilando evidencia de bajo impacto...";

        try
        {
            _snapshot = await observabilityService.CaptureAsync(DiagnosticCost.Medium);

            Findings.Clear();
            foreach (var finding in _snapshot.Findings
                         .Where(f => f.State == FindingLifecycleState.Active)
                         .Take(50))
            {
                Findings.Add(new FindingItemViewModel(finding));
            }

            Incidents.Clear();
            foreach (var incident in _snapshot.Incidents.Take(30))
                Incidents.Add(new IncidentItemViewModel(incident));

            Collectors.Clear();
            foreach (var collector in _snapshot.Collectors
                         .OrderBy(c => c.Cost)
                         .ThenBy(c => c.CollectorName))
            {
                Collectors.Add(new CollectorItemViewModel(collector));
            }

            NotifySnapshotProperties();

            if (_snapshot.PersistenceDegraded)
            {
                StatusText =
                    $"Captura válida; el histórico está degradado. {_snapshot.PersistenceError}";
            }
            else
            {
                StatusText = _snapshot.FailedCollectorCount == 0
                    ? "Observabilidad actualizada correctamente."
                    : $"Captura completada con {_snapshot.FailedCollectorCount} módulo(s) no disponibles.";
            }
        }
        catch (Exception ex)
        {
            StatusText =
                $"No se pudo completar la observabilidad: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private double MetricValue(string name) => _snapshot?.Metrics
        .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(m => m.TimestampUtc)
        .Select(m => m.Value)
        .FirstOrDefault() ?? 0d;

    private double LatestMaxMetricValue(string name)
    {
        if (_snapshot is null)
            return 0d;

        var matches = _snapshot.Metrics
            .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return 0d;

        var newest = matches.Max(m => m.TimestampUtc);
        return matches
            .Where(m => newest - m.TimestampUtc <= TimeSpan.FromSeconds(5))
            .Select(m => m.Value)
            .DefaultIfEmpty(0d)
            .Max();
    }

    private string? LatestEntityByMetric(string name, out double value)
    {
        value = 0d;
        if (_snapshot is null)
            return null;

        var matches = _snapshot.Metrics
            .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return null;

        var newest = matches.Max(m => m.TimestampUtc);
        var best = matches
            .Where(m => newest - m.TimestampUtc <= TimeSpan.FromSeconds(5))
            .OrderByDescending(m => m.Value)
            .FirstOrDefault();
        if (best is null)
            return null;

        value = best.Value;
        return best.Entity;
    }

    private int AffectedAreaCount() => _snapshot?.Findings
        .Where(f => f.State == FindingLifecycleState.Active)
        .Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
        .Select(f => f.Category)
        .Distinct()
        .Count() ?? 0;

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(HealthScore));
        OnPropertyChanged(nameof(HealthScoreText));
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(HealthColor));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(HealthyAreaCount));
        OnPropertyChanged(nameof(EvidenceCount));
        OnPropertyChanged(nameof(IncidentCount));
        OnPropertyChanged(nameof(CriticalIncidentCount));
        OnPropertyChanged(nameof(SuccessfulCollectors));
        OnPropertyChanged(nameof(FailedCollectors));
        OnPropertyChanged(nameof(LastCapture));
        OnPropertyChanged(nameof(CpuPercent));
        OnPropertyChanged(nameof(MemoryPercent));
        OnPropertyChanged(nameof(CommitPercent));
        OnPropertyChanged(nameof(DiskBusyPercent));
        OnPropertyChanged(nameof(NetworkUtilizationPercent));
        OnPropertyChanged(nameof(CpuPercentText));
        OnPropertyChanged(nameof(MemoryPercentText));
        OnPropertyChanged(nameof(CommitPercentText));
        OnPropertyChanged(nameof(DiskBusyPercentText));
        OnPropertyChanged(nameof(NetworkUtilizationText));
        OnPropertyChanged(nameof(TopCpuProcess));
        OnPropertyChanged(nameof(TopMemoryProcess));
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(AutomaticStoppedServices));
        OnPropertyChanged(nameof(RelevantEvents));
        OnPropertyChanged(nameof(CollectorSummary));
        OnPropertyChanged(nameof(PersistenceStatus));
    }
}
