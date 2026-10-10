namespace WindowsSystemGuardian.Contracts.Environment;

public enum MachineContext
{
    Unknown = 0,
    PhysicalConfirmed,
    VirtualGuestConfirmed,
    HypervisorHostConfirmed,
    Ambiguous
}
