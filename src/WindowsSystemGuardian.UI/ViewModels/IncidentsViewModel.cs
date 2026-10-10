using System.Collections.ObjectModel;
using WindowsSystemGuardian.Contracts.Incidents;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class IncidentsViewModel(
    ObservabilityViewModel observability,
    IObservabilityStore store,
    Action<Incident>? remediationOpener = null) : ObservableObject
{
    private bool _isBusy;
    private string _statusText = "Esperando correlación.";

    public ObservableCollection<IncidentItemViewModel> Incidents { get; } = [];

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

    public string IncidentCount => Incidents.Count(i => i.Incident.State != IncidentState.Resolved).ToString("N0");
    public string CriticalIncidentCount => Incidents.Count(i => i.Incident.State != IncidentState.Resolved && i.Incident.Severity == IncidentSeverity.Critical).ToString("N0");
    public string ResolvedIncidentCount => Incidents.Count(i => i.Incident.State == IncidentState.Resolved).ToString("N0");
    public string StorePath => store.RootPath;

    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusText = "Correlacionando evidencia e histórico reciente...";
        try
        {
            await observability.RefreshAsync();
            IReadOnlyList<Incident> persisted;
            string? historyError = null;
            try
            {
                persisted = await store.LoadIncidentsSinceAsync(DateTime.UtcNow.AddHours(-24));
            }
            catch (Exception ex)
            {
                persisted = [];
                historyError = ex.GetBaseException().Message;
            }

            var combined = persisted
                .Concat(observability.CurrentIncidents)
                .GroupBy(i => i.Fingerprint, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(StateTimestamp).First())
                .OrderBy(i => i.State == IncidentState.Resolved)
                .ThenByDescending(i => i.Severity)
                .ThenByDescending(StateTimestamp)
                .Take(50)
                .ToList();

            Incidents.Clear();
            foreach (var incident in combined)
                Incidents.Add(new IncidentItemViewModel(incident, remediationOpener));

            OnPropertyChanged(nameof(IncidentCount));
            OnPropertyChanged(nameof(CriticalIncidentCount));
            OnPropertyChanged(nameof(ResolvedIncidentCount));
            StatusText = historyError is not null
                ? $"Captura actual disponible; no se pudo leer el histórico de incidentes: {historyError}"
                : combined.Count == 0
                    ? "No hay incidentes correlacionados en las últimas 24 horas."
                    : $"{combined.Count} incidente(s) correlacionado(s) en la ventana reciente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DateTime StateTimestamp(Incident incident)
    {
        var latest = incident.LastSeenUtc;
        if (incident.StateChangedAtUtc is { } stateChanged && stateChanged > latest)
            latest = stateChanged;
        if (incident.ResolvedAtUtc is { } resolved && resolved > latest)
            latest = resolved;
        return latest;
    }
}
