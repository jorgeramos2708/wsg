using WindowsSystemGuardian.Contracts.Incidents;

namespace WindowsSystemGuardian.Core.Incidents;

/// <summary>
/// Mantiene el ciclo de vida de incidentes. Un incidente no se considera resuelto
/// tras una sola captura limpia: requiere dos comprobaciones consecutivas de los
/// colectores que realmente pueden verificar esa familia de incidente.
/// </summary>
public sealed class IncidentLifecycleTracker
{
    private readonly Dictionary<string, Incident> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private bool _initialized;

    public bool IsInitialized
    {
        get { lock (_sync) return _initialized; }
    }

    public void Initialize(IEnumerable<Incident> previous)
    {
        lock (_sync)
        {
            if (_initialized)
                return;

            foreach (var incident in previous
                         .GroupBy(i => i.Fingerprint, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.OrderByDescending(StateTimestamp).First()))
            {
                _latest[incident.Fingerprint] = incident;
            }

            _initialized = true;
        }
    }

    /// <summary>
    /// Compatibilidad para pruebas/capturas completas explícitas. Cuando
    /// <paramref name="fullCoverage"/> es true se considera verificable cualquier
    /// incidente ausente; cuando es false ninguno puede avanzar a resuelto.
    /// </summary>
    public IReadOnlyList<Incident> Apply(
        IReadOnlyList<Incident> current,
        bool fullCoverage,
        DateTime nowUtc) =>
        ApplyInternal(current, _ => fullCoverage, nowUtc);

    /// <summary>
    /// Verificación por dominio: un incidente solo acumula checks limpios cuando
    /// todos los colectores requeridos por ese incidente fueron ejecutados con éxito
    /// en la captura actual. Esto evita depender de que coincidan todos los módulos
    /// del scheduler y evita resolver por ausencia de una señal que no se comprobó.
    /// </summary>
    public IReadOnlyList<Incident> Apply(
        IReadOnlyList<Incident> current,
        IReadOnlySet<string> successfulCollectors,
        DateTime nowUtc) =>
        ApplyInternal(
            current,
            incident => CanVerifyClear(incident, successfulCollectors),
            nowUtc);

    private IReadOnlyList<Incident> ApplyInternal(
        IReadOnlyList<Incident> current,
        Func<Incident, bool> canVerifyClear,
        DateTime nowUtc)
    {
        lock (_sync)
        {
            if (!_initialized)
                _initialized = true;

            var emitted = new List<Incident>();
            var currentFingerprints = current.Select(i => i.Fingerprint)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var currentIncident in current)
            {
                var incident = currentIncident;
                if (_latest.TryGetValue(incident.Fingerprint, out var previous))
                {
                    incident = incident with
                    {
                        FirstSeenUtc = previous.State != IncidentState.Resolved && previous.FirstSeenUtc < incident.FirstSeenUtc
                            ? previous.FirstSeenUtc
                            : incident.FirstSeenUtc,
                        OccurrenceCount = previous.State == IncidentState.Resolved
                            ? incident.OccurrenceCount
                            : Math.Max(previous.OccurrenceCount, incident.OccurrenceCount),
                        State = IncidentState.Active,
                        ConsecutiveClearChecks = 0,
                        ResolvedAtUtc = null,
                        StateChangedAtUtc = previous.State == IncidentState.Active
                            ? previous.StateChangedAtUtc ?? nowUtc
                            : nowUtc
                    };
                }
                else
                {
                    incident = incident with { StateChangedAtUtc = incident.StateChangedAtUtc ?? nowUtc };
                }

                _latest[incident.Fingerprint] = incident;
                emitted.Add(incident);
            }

            foreach (var pair in _latest.ToList())
            {
                if (currentFingerprints.Contains(pair.Key) || pair.Value.State == IncidentState.Resolved)
                    continue;

                var previous = pair.Value;
                if (!canVerifyClear(previous))
                    continue;

                var clearChecks = previous.ConsecutiveClearChecks + 1;
                var state = clearChecks >= 2 ? IncidentState.Resolved : IncidentState.Monitoring;
                var transition = previous with
                {
                    Id = $"{previous.Fingerprint}:{state}:{nowUtc:yyyyMMddHHmmssfff}",
                    State = state,
                    ConsecutiveClearChecks = clearChecks,
                    ResolvedAtUtc = state == IncidentState.Resolved ? nowUtc : null,
                    StateChangedAtUtc = nowUtc
                };
                _latest[pair.Key] = transition;
                emitted.Add(transition);
            }

            return emitted
                .OrderByDescending(i => i.State == IncidentState.Active)
                .ThenByDescending(i => i.Severity)
                .ThenByDescending(StateTimestamp)
                .ToList();
        }
    }

    public IReadOnlyList<Incident> Snapshot()
    {
        lock (_sync)
            return _latest.Values.OrderByDescending(StateTimestamp).ToList();
    }

    private static bool CanVerifyClear(Incident incident, IReadOnlySet<string> successfulCollectors)
    {
        var required = incident.VerificationCollectors;
        if (required is null || required.Count == 0)
            return false;

        return required.All(successfulCollectors.Contains);
    }

    public static DateTime StateTimestamp(Incident incident) =>
        LatestTimestamp(incident);

    private static DateTime LatestTimestamp(Incident incident)
    {
        var latest = incident.LastSeenUtc;
        if (incident.StateChangedAtUtc is { } stateChanged && stateChanged > latest)
            latest = stateChanged;
        if (incident.ResolvedAtUtc is { } resolved && resolved > latest)
            latest = resolved;
        return latest;
    }
}
