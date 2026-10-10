namespace WindowsSystemGuardian.Contracts.Environment;

public sealed record EnvironmentProfile(
    string MachineName,
    string OperatingSystem,
    string Version,
    string BuildNumber,
    NodeRole NodeRole,
    bool IsVirtualMachine,
    bool IsHyperVHost,
    bool IsClusterNode,
    bool IsDomainController,
    VirtualizationPlatform VirtualizationPlatform,
    DetectionConfidence Confidence,
    bool DomainJoined,
    string Domain,
    string PolicyName)
{
    public MachineContext MachineContext { get; init; } = MachineContext.Unknown;
    public bool UsesWindowsHypervisorForSecurity { get; init; }
    public bool IsNestedVirtualization { get; init; }
    public bool EnvironmentSignalsComplete { get; init; } = true;

    public string RoleDisplay => NodeRole switch
    {
        NodeRole.DomainController when IsVirtualMachine => "Domain Controller virtual",
        NodeRole.DomainController => "Domain Controller",
        NodeRole.ClusterNode when IsVirtualMachine => "Nodo de Failover Cluster virtual",
        NodeRole.ClusterNode => "Nodo de Failover Cluster",
        NodeRole.HyperVHost => "Nodo principal Hyper-V",
        NodeRole.HyperVClusterNode => "Nodo Hyper-V Cluster",
        NodeRole.PhysicalWorkstation => "PC / Laptop físico",
        NodeRole.VirtualWorkstation => IsNestedVirtualization ? "Workstation virtual · host Hyper-V anidado" : "Workstation virtual",
        NodeRole.PhysicalServer => "Servidor físico",
        NodeRole.VirtualServer => IsNestedVirtualization ? "Servidor virtual · host Hyper-V anidado" : "Servidor virtual",
        _ when MachineContext == MachineContext.Ambiguous => "Entorno físico/virtual ambiguo",
        _ => "Entorno desconocido"
    };

    public string VirtualizationDisplay
    {
        get
        {
            if (IsVirtualMachine)
            {
                var guest = VirtualizationPlatform switch
                {
                    VirtualizationPlatform.HyperV => "VM sobre Hyper-V",
                    VirtualizationPlatform.VMware => "VM sobre VMware",
                    VirtualizationPlatform.KvmQemu => "VM sobre KVM/QEMU (Proxmox compatible)",
                    VirtualizationPlatform.VirtualBox => "VM sobre VirtualBox",
                    _ => "Máquina virtual (plataforma no determinada)"
                };

                return IsNestedVirtualization ? $"{guest} · Hyper-V anidado activo" : guest;
            }

            if (MachineContext == MachineContext.HypervisorHostConfirmed)
                return "Hipervisor Hyper-V local";

            if (UsesWindowsHypervisorForSecurity)
                return "Equipo físico con hipervisor de seguridad de Windows (VBS)";

            if (MachineContext == MachineContext.Ambiguous)
                return "Virtualización detectada; guest/host no confirmado";

            return MachineContext == MachineContext.PhysicalConfirmed ? "No virtualizado" : "No determinado";
        }
    }
}
