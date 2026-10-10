namespace WindowsSystemGuardian.Contracts.Observability;

/// <summary>
/// Persisted in JSONL as an enum value. Existing numeric values are explicit so
/// future categories cannot reinterpret historical 0.2/0.3 records.
/// </summary>
public enum EvidenceCategory
{
    Unknown = 0,
    Environment = 1,
    Storage = 2,
    Performance = 3,
    Reliability = 4,
    Services = 5,
    Network = 6,
    Integrity = 7,
    Servicing = 8,
    Security = 9,
    Observability = 10,
    Recovery = 11,
    Backup = 12
}
