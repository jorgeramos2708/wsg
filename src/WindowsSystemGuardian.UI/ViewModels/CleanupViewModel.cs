using System.Collections.ObjectModel;
using WindowsSystemGuardian.Contracts.Cleanup;
using WindowsSystemGuardian.Core.Cleanup;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class CleanupViewModel(CleanupAnalysisService cleanupAnalysisService) : ObservableObject
{
    private bool _isBusy;
    private string _cleanupSummary = "Aún no se ha ejecutado el análisis de limpieza.";
    private CleanupAnalysisResult? _cleanupResult;

    public ObservableCollection<CleanupCandidateViewModel> CleanupCandidates { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string CleanupSummary
    {
        get => _cleanupSummary;
        private set => SetProperty(ref _cleanupSummary, value);
    }

    public string RecoverableSpace => _cleanupResult is null ? "—" : FormatBytes(_cleanupResult.TotalRecoverableBytes);
    public string CleanupFiles => _cleanupResult is null ? "—" : _cleanupResult.Candidates.Count.ToString("N0");
    public string CleanupLocked => _cleanupResult is null ? "—" : _cleanupResult.LockedFiles.ToString("N0");

    public async Task AnalyzeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        CleanupSummary = "Analizando únicamente rutas permitidas. No se eliminará ningún archivo...";
        try
        {
            _cleanupResult = await cleanupAnalysisService.AnalyzeAsync();
            CleanupCandidates.Clear();
            foreach (var candidate in _cleanupResult.Candidates.OrderByDescending(x => x.SizeBytes).Take(250))
                CleanupCandidates.Add(new CleanupCandidateViewModel(candidate));

            CleanupSummary = _cleanupResult.Truncated
                ? "Análisis parcial por límite de seguridad. La lista muestra los candidatos encontrados hasta el límite configurado."
                : $"Análisis completado en {_cleanupResult.Duration.TotalSeconds:F1}s. Solo se muestran candidatos seguros; no se ha eliminado nada.";

            OnPropertyChanged(nameof(RecoverableSpace));
            OnPropertyChanged(nameof(CleanupFiles));
            OnPropertyChanged(nameof(CleanupLocked));
        }
        catch (Exception ex)
        {
            CleanupSummary = $"No se pudo completar el análisis: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double KiB = 1024d;
        const double MiB = KiB * 1024d;
        const double GiB = MiB * 1024d;
        return bytes >= GiB ? $"{bytes / GiB:F2} GB" : bytes >= MiB ? $"{bytes / MiB:F1} MB" : $"{bytes / KiB:F0} KB";
    }
}
