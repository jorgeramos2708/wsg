namespace WindowsSystemGuardian.Contracts.Environment;

public enum NodeRole
{
    Unknown = 0,
    PhysicalWorkstation,
    VirtualWorkstation,
    PhysicalServer,
    VirtualServer,
    DomainController,
    ClusterNode,
    HyperVHost,
    HyperVClusterNode
}
