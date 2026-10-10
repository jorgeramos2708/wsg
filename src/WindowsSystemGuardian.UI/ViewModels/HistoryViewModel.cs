using System.Collections.ObjectModel;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Persistence;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class HistoryViewModel(IObservabilityStore store) : ObservableObject
{
    private bool _isBusy;
    private string _summary = "Sin datos históricos cargados.";

    public ObservableCollection<HistoryEvidenceItemViewModel> Items { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string DataPath => store.RootPath;

    public string DataSource => store is ReadOnlyObservabilityStore
        ? "Origen: Agent compartido (fuente autoritativa)"
        : "Origen: captura local de la UI";

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var evidence = await store.LoadEvidenceSinceAsync(since);
            var metrics = await store.LoadMetricsSinceAsync(since);
            var findings = await store.LoadFindingsSinceAsync(since);
            var incidents = await store.LoadIncidentsSinceAsync(since);

            Items.Clear();
            foreach (var item in evidence
                         .OrderByDescending(e => e.TimestampUtc)
                         .Take(200))
            {
                Items.Add(new HistoryEvidenceItemViewModel(item));
            }

            Summary =
                $"Últimas 24 h: {evidence.Count:N0} evidencias · {metrics.Count:N0} muestras · {findings.Count:N0} hallazgos · {incidents.Count:N0} incidentes";
        }
        catch (Exception ex)
        {
            Summary = $"No se pudo cargar el histórico: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
