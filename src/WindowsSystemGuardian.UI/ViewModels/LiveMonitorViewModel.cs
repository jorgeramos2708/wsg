using System.Windows.Threading;
using WindowsSystemGuardian.Contracts.Observability;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Monitoring;

namespace WindowsSystemGuardian.UI.ViewModels;

/// <summary>
/// Monitoreo de recursos en tiempo real del Dashboard. DispatcherTimer (hilo de UI,
/// según la documentación de WPF) muestrea el collector de rendimiento cada 2 segundos
/// y publica las métricas para bindin OneWay. Las muestras vivas son de solo lectura:
/// NO se escriben en el store de evidencia (la cadencia de evidencia es la del
/// pipeline normal). Busy-guard: si la captura anterior sigue en curso, el tick se salta.
/// </summary>
public sealed class LiveMonitorViewModel : ObservableObject
{
    private readonly IObservabilityCollector _performanceCollector;
    private readonly DispatcherTimer _timer;
    private readonly object _captureSync = new();
    private bool _capturing;
    private bool _running;
    private LiveResourceSample? _sample;
    private string _statusText = "Monitoreo detenido.";

    public LiveMonitorViewModel(IObservabilityCollector performanceCollector)
    {
        _performanceCollector = performanceCollector;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

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

    public string CpuText => _sample is null ? "—" : $"{_sample.CpuPercent:0} %";
    public string MemoryText => _sample is null
        ? "—"
        : $"{(_sample.MemoryTotalBytes - _sample.MemoryAvailableBytes) / 1073741824.0:0.0} GB de {_sample.MemoryTotalBytes / 1073741824.0:0.0} GB ({_sample.MemoryLoadPercent:0} %)";
    public string CommitText => _sample is null ? "—" : $"{_sample.CommitPercent:0} % del límite";

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
        StatusText = "Monitoreo en vivo cada 2 segundos.";
        _timer.Start();
        _ = TickAsync();
    }

    public void Stop()
    {
        if (!Running)
            return;

        _timer.Stop();
        Running = false;
        StatusText = "Monitoreo detenido.";
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
            if (sample is null)
            {
                StatusText = "Muestra no disponible; se reintenta en el próximo tick.";
                return;
            }

            _sample = sample;
            StatusText = "Monitoreo en vivo cada 2 segundos.";
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(MemoryLoadPercent));
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(CommitPercent));
            OnPropertyChanged(nameof(CommitText));
            OnPropertyChanged(nameof(LastUpdatedText));
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
}
