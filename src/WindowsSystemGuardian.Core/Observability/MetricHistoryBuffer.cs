using WindowsSystemGuardian.Contracts.Observability;

namespace WindowsSystemGuardian.Core.Observability;

internal sealed class MetricHistoryBuffer(
    TimeSpan maximumAge,
    int maximumSamples = 25_000)
{
    private readonly object _sync = new();
    private readonly List<MetricSample> _samples = [];
    private bool _initialized;

    public bool IsInitialized
    {
        get
        {
            lock (_sync)
                return _initialized;
        }
    }

    public void Initialize(IEnumerable<MetricSample> samples, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (_initialized)
                return;

            _samples.AddRange(samples);
            _initialized = true;
            PruneUnsafe(nowUtc);
        }
    }

    public void AddRange(IEnumerable<MetricSample> samples, DateTime nowUtc)
    {
        lock (_sync)
        {
            _samples.AddRange(samples);
            _initialized = true;
            PruneUnsafe(nowUtc);
        }
    }

    public IReadOnlyList<MetricSample> Snapshot(DateTime nowUtc)
    {
        lock (_sync)
        {
            PruneUnsafe(nowUtc);
            return _samples
                .OrderBy(m => m.TimestampUtc)
                .ToList();
        }
    }

    private void PruneUnsafe(DateTime nowUtc)
    {
        var cutoff = nowUtc - maximumAge;
        _samples.RemoveAll(m => m.TimestampUtc < cutoff);

        if (_samples.Count <= maximumSamples)
            return;

        _samples.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));
        _samples.RemoveRange(0, _samples.Count - maximumSamples);
    }
}
