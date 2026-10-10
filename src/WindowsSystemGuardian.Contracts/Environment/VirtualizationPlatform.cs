namespace WindowsSystemGuardian.Contracts.Environment;

public enum VirtualizationPlatform
{
    None = 0,
    HyperV,
    VMware,
    KvmQemu,
    VirtualBox,
    Unknown
}
