namespace WindowsSystemGuardian.Contracts.Storage;

public enum VolumePurpose
{
    Unknown = 0,
    System,
    Data,
    Recovery,
    Efi,
    Reserved,
    Removable
}
