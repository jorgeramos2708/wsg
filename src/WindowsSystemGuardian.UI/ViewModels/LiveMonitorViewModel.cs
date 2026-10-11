using System.Collections.ObjectModel;
using System.Windows.Threading;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Monitoring;
using WindowsSystemGuardian.Windows.Observability;

namespace WindowsSystemGuardian.UI.ViewModels;

/// <summary>Fila de la lista de principales consumidores (proceso/servicio) en vivo.</summary>
public sealed class LiveProcessItemViewModel(
    string name,
    int processId,
    double cpuPercent,
    long workingSetBytes,
    IReadOnlyList<string> serviceNames,
    bool significant)
{
    public string Name => serviceNames.Count == 0
        ? $"{name} (PID {processId})"
        : $"{name} (PID {processId}) · {string.Join(", ", serviceNames)}";
    public double CpuPercent => cpuPercent;
    public string CpuText => $"{cpuPercent:0.#} %";
    public string MemoryText => workingSetBytes / 1073741824.0 >= 1
        ? $"{workingSetBytes / 1073741824.0:0.00} GB"
        : $"{workingSetBytes / 1048576.0:0} MB";
    public bool Significant => significant;
    public string SignificanceMark => significant ? "▲" : string.Empty;
}

/// <summary>Fila de capacidad por unidad de disco en vivo.</summary>
public sealed class LiveDiskVolumeViewModel(string letter, double totalBytes, double freeBytes)
{
    public string Letter => letter;
    public double UsedPercent => totalBytes <= 0
        ? 0
        : Math.Clamp((totalBytes - freeBytes) / totalBytes * 100, 0, 100);
    public double FreePercent => totalBytes <= 0
        ? 0
        : Math.Clamp(freeBytes / totalBytes * 100, 0, 100);
    public string UsedText => $"{(totalBytes - freeBytes) / 1073741824.0:0} GB usados ({UsedPercent:0} %)";
    public string FreeText => $"{freeBytes / 1073741824.0:0.#} GB libres ({FreePercent:0} %)";
}

/// <summary>
/// Monitoreo de recursos en tiempo real del Dashboard. DispatcherTimer (hilo de UI,
/// según la documentación de WPF) muestrea cada 2 segundos: rendimiento del sistema
/// (CPU/RAM/commit/uptime), consumidores significativos (procesos y servicios) y
/// actividad/capacidad de discos. Publica históricos para las sparklines (0.11.0).
/// Las muestras vivas son de solo lectura: NO se escriben en el store de evidencia.
/// Busy-guard: si la captura anterior sigue en curso, el tick se salta.
/// </summary>
public sealed class LiveMonitorViewModel : ObservableObject
{
    private readonly IObservabilityCollector _performanceCollector;
    private readonly LiveProcessSampler _processSampler;
    private readonly LiveDiskSampler _diskSampler;
    private readonly LiveNetworkSampler _networkSampler;
    private readonly DispatcherTimer _timer;
    private readonly object _captureSync = new();
    private readonly List<double> _cpuHistory = [];
    private readonly List<double> _memoryHistory = [];
    private readonly List<double> _commitHistory = [];
    private readonly List<double> _diskHistory = [];
    private readonly List<double> _netInHistory = [];
    private readonly List<double> _netOutHistory = [];
    private bool _capturing;
    private bool _running;
    private LiveResourceSample? _sample;
    private LiveDiskSample? _diskSample;
    private LiveNetworkSample? _networkSample;
    private string _statusText = string.Empty;

    public LiveMonitorViewModel(IObservabilityCollector performanceCollector, LiveProcessSampler processSampler, LiveDiskSampler diskSampler, LiveNetworkSampler networkSampler)
    {
        _performanceCollector = performanceCollector;
        _processSampler = processSampler;
        _diskSampler = diskSampler;
        _networkSampler = networkSampler;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public ObservableCollection<LiveProcessItemViewModel> TopProcesses { get; } = [];
    public ObservableCollection<LiveDiskVolumeViewModel> DiskVolumes { get; } = [];

    /// <summary>Histórico para las sparklines. Una lista nueva por tick activa el re-render del control.</summary>
    public IReadOnlyList<double> CpuHistory => [.. _cpuHistory];
    public IReadOnlyList<double> MemoryHistory => [.. _memoryHistory];
    public IReadOnlyList<double> CommitHistory => [.. _commitHistory];
    public IReadOnlyList<double> DiskHistory => [.. _diskHistory];
    public IReadOnlyList<double> NetInHistory => [.. _netInHistory];
    public IReadOnlyList<double> NetOutHistory => [.. _netOutHistory];

    public bool Running
    {
        get => _running;
        private set
        {
            if (SetProperty(ref _running, value))
            {
                OnPropertyChanged(nameof(StatusDotText));
                OnPropertyChanged(nameof(StatusDotColor));
            }
        }
    }

    public double CpuPercent => _sample?.CpuPercent ?? 0;
    public double MemoryLoadPercent => _sample?.MemoryLoadPercent ?? 0;
    public double CommitPercent => _sample?.CommitPercent ?? 0;
    public double DiskBusyPercent => _diskSample?.BusyPercent ?? 0;

    public string CpuText => _sample is null ? "—" : $"{_sample.CpuPercent:0} %";
    public string MemoryText => _sample is null
        ? "—"
        : $"{(_sample.MemoryTotalBytes - _sample.MemoryAvailableBytes) / 1073741824.0:0.0} GB de {_sample.MemoryTotalBytes / 1073741824.0:0.0} GB";

    /// <summary>
    /// "Memoria comprometida" (commit) es la memoria virtual que Windows ha garantizado
    /// a los procesos, respaldada por RAM + archivo de paginación; el límite es
    /// RAM + pagefile (GlobalMemoryStatusEx, TotalPageFile/AvailPageFile, sysinfoapi.h).
    /// </summary>
    public string CommitText => _sample is null
        ? "—"
        : $"{_sample.CommitPercent:0} % del límite (RAM + archivo de paginación)";

    public string DiskThroughputText => (_diskSample?.BytesPerSec) switch
    {
        null or <= 0 => "—",
        var bps when bps.Value / 1048576.0 >= 1 => $"{bps.Value / 1048576.0:0.0} MB/s",
        var bps => $"{bps.Value / 1024.0:0} KB/s"
    };

    public string UptimeText => _sample is null
        ? "—"
        : TimeSpan.FromSeconds(_sample.UptimeSeconds) switch
        {
            { TotalHours: var h } span when h >= 24 => $"{(int)(h / 24)} d {span.Hours} h",
            { TotalHours: var h } => $"{(int)h} h {Math.Floor(_sample.UptimeSeconds % 3600 / 60)} min"
        };

    public double NetInBytesPerSec => _networkSample?.BytesReceivedPerSec ?? 0;
    public double NetOutBytesPerSec => _networkSample?.BytesSentPerSec ?? 0;

    public string NetInText => FormatNetSpeed(_networkSample?.BytesReceivedPerSec);
    public string NetOutText => FormatNetSpeed(_networkSample?.BytesSentPerSec);

    private static string FormatNetSpeed(double? bytesPerSec) => bytesPerSec switch
    {
        null or <= 0 => "—",
        var bps when bps.Value / 1048576.0 >= 1 => $"{bps.Value / 1048576.0:0.0} MB/s",
        var bps when bps.Value / 1024.0 >= 1 => $"{bps.Value / 1024.0:0.0} KB/s",
        var bps => $"{bps.Value:0} B/s"
    };

    public string LastUpdatedText => _sample is null
        ? "—"
        : $"Actualizado {_sample.CapturedAtUtc.ToLocalTime():HH:mm:ss}";

    /// <summary>Indicador textual del estado del timer (sin converters en la vista).</summary>
    public string StatusDotText => Running ? "●" : "○";
    public string StatusDotColor => Running ? "#FF269447" : "#FF9AA5B4";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void Start()
    {
        if (Running)
            return;

        Running = true;
        StatusText = string.Empty;
        _timer.Start();
        _ = TickAsync();
    }

    public void Stop()
    {
        if (!Running)
            return;

        _timer.Stop();
        Running = false;
    }

    public void Toggle()
    {
        if (Running) Stop(); else Start();
    }

    private async Task TickAsync()
    {
        if (!Running)
            return;

        lock (_captureSync)
        {
            if (_capturing)
                return;
            _capturing = true;
        }

        try
        {
            var result = await _performanceCollector.CollectAsync();
            var sample = LiveResourceSampleReader.FromCollectorResult(result);

            // Consumidores significativos (procesos y servicios que alojan) y discos,
            // en segundo plano; se actualizan con la misma cadencia del tick.
            var topProcesses = await Task.Run(() => _processSampler.Sample(
                Environment.ProcessorCount, topN: 5));
            _diskSample = await Task.Run(() => _diskSampler.Sample());
            _networkSample = await Task.Run(() => _networkSampler.Sample());

            if (sample is null)
            {
                StatusText = "Muestra no disponible; se reintenta en el próximo tick.";
                ReplaceTopProcesses(topProcesses);
                ReplaceDiskVolumes(_diskSample);
                return;
            }

            _sample = sample;
            StatusText = string.Empty;
            AppendHistory(_cpuHistory, sample.CpuPercent);
            AppendHistory(_memoryHistory, sample.MemoryLoadPercent);
            AppendHistory(_commitHistory, sample.CommitPercent);
            if (_diskSample?.BusyPercent is { } busy)
                AppendHistory(_diskHistory, busy);
            if (_networkSample?.BytesReceivedPerSec is { } rx)
                AppendHistory(_netInHistory, rx / 1024.0);
            if (_networkSample?.BytesSentPerSec is { } tx)
                AppendHistory(_netOutHistory, tx / 1024.0);

            ReplaceTopProcesses(topProcesses);
            ReplaceDiskVolumes(_diskSample);
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(MemoryLoadPercent));
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(CommitPercent));
            OnPropertyChanged(nameof(CommitText));
            OnPropertyChanged(nameof(DiskBusyPercent));
            OnPropertyChanged(nameof(DiskThroughputText));
            OnPropertyChanged(nameof(NetInText));
            OnPropertyChanged(nameof(NetOutText));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(CpuHistory));
            OnPropertyChanged(nameof(MemoryHistory));
            OnPropertyChanged(nameof(CommitHistory));
            OnPropertyChanged(nameof(DiskHistory));
        }
        catch (OperationCanceledException)
        {
            // Cancelación: el siguiente tick reintenta.
        }
        catch (Exception ex)
        {
            StatusText = $"Muestra fallida: {ex.GetBaseException().Message}";
        }
        finally
        {
            lock (_captureSync)
            {
                _capturing = false;
            }
        }
    }

    private static void AppendHistory(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > MonitoringThresholds.HistoryCapacity)
            history.RemoveRange(0, history.Count - MonitoringThresholds.HistoryCapacity);
    }

    private void ReplaceTopProcesses(IReadOnlyList<LiveProcessConsumption> topProcesses)
    {
        TopProcesses.Clear();
        foreach (var consumption in topProcesses)
            TopProcesses.Add(new LiveProcessItemViewModel(
                consumption.Name,
                consumption.ProcessId,
                consumption.CpuPercent,
                consumption.WorkingSetBytes,
                consumption.ServiceNames,
                consumption.CpuSignificant || consumption.MemorySignificant));
    }

    private void ReplaceDiskVolumes(LiveDiskSample? diskSample)
    {
        DiskVolumes.Clear();
        foreach (var volume in diskSample?.Capacity ?? [])
            DiskVolumes.Add(new LiveDiskVolumeViewModel(volume.DriveLetter, volume.TotalBytes, volume.FreeBytes));
    }
}
