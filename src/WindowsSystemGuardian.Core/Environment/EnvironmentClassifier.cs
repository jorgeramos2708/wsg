using WindowsSystemGuardian.Contracts.Environment;

namespace WindowsSystemGuardian.Core.Environment;

public sealed class EnvironmentClassifier
{
    public EnvironmentProfile Classify(EnvironmentFacts facts)
    {
        var detectedPlatform = DetectVirtualizationPlatform(facts.Manufacturer, facts.Model);
        var isServer = facts.ProductType is 2 or 3;
        var isDomainController = facts.ProductType == 2;
        var guestEvidence = detectedPlatform != VirtualizationPlatform.None;
        var isHyperVHost = facts.VmmsServicePresent;
        var isClusterNode = facts.ClusterServicePresent && isServer;
        var usesSecurityHypervisor = facts.HypervisorPresent &&
                                     facts.VbsRunning &&
                                     !guestEvidence &&
                                     !isHyperVHost;
        var isNested = guestEvidence && isHyperVHost;

        var machineContext = ResolveMachineContext(
            facts,
            guestEvidence,
            isHyperVHost,
            usesSecurityHypervisor);

        var isVm = guestEvidence;
        var platform = isVm
            ? detectedPlatform
            : isHyperVHost ? VirtualizationPlatform.HyperV
            : machineContext == MachineContext.Ambiguous
                ? VirtualizationPlatform.Unknown
                : VirtualizationPlatform.None;

        var role = ResolveRole(isServer, isDomainController, isVm, isHyperVHost, isClusterNode, machineContext);
        var confidence = ResolveConfidence(facts, machineContext, guestEvidence);
        var policy = ResolvePolicy(role, machineContext, facts.ServiceProbeSucceeded, isNested);

        return new EnvironmentProfile(
            facts.MachineName,
            facts.OperatingSystemCaption,
            facts.Version,
            facts.BuildNumber,
            role,
            isVm,
            isHyperVHost,
            isClusterNode,
            isDomainController,
            platform,
            confidence,
            facts.PartOfDomain,
            facts.Domain,
            policy)
        {
            MachineContext = machineContext,
            UsesWindowsHypervisorForSecurity = usesSecurityHypervisor,
            IsNestedVirtualization = isNested,
            EnvironmentSignalsComplete = facts.ServiceProbeSucceeded &&
                                         (!facts.HypervisorPresent || guestEvidence || isHyperVHost || facts.DeviceGuardProbeSucceeded)
        };
    }

    private static MachineContext ResolveMachineContext(
        EnvironmentFacts facts,
        bool guestEvidence,
        bool isHyperVHost,
        bool usesSecurityHypervisor)
    {
        // SMBIOS de guest tiene prioridad: una VM puede además ejecutar Hyper-V anidado.
        if (guestEvidence)
            return MachineContext.VirtualGuestConfirmed;

        if (isHyperVHost)
            return MachineContext.HypervisorHostConfirmed;

        // VBS/HVCI utiliza el hipervisor de Windows sin convertir el equipo físico en guest.
        if (usesSecurityHypervisor)
            return MachineContext.PhysicalConfirmed;

        if (facts.HypervisorPresent)
            return MachineContext.Ambiguous;

        return MachineContext.PhysicalConfirmed;
    }

    private static NodeRole ResolveRole(
        bool isServer,
        bool isDomainController,
        bool isVm,
        bool isHyperVHost,
        bool isClusterNode,
        MachineContext context)
    {
        if (isDomainController)
            return NodeRole.DomainController;

        if (isClusterNode)
        {
            if (isHyperVHost && !isVm)
                return NodeRole.HyperVClusterNode;
            return NodeRole.ClusterNode;
        }

        if (isHyperVHost && !isVm)
            return NodeRole.HyperVHost;

        if (context == MachineContext.Ambiguous)
            return NodeRole.Unknown;

        if (isServer)
            return isVm ? NodeRole.VirtualServer : NodeRole.PhysicalServer;

        return isVm ? NodeRole.VirtualWorkstation : NodeRole.PhysicalWorkstation;
    }

    private static string ResolvePolicy(
        NodeRole role,
        MachineContext context,
        bool serviceProbeSucceeded,
        bool isNested)
    {
        if (!serviceProbeSucceeded || context == MachineContext.Ambiguous)
            return "MAXIMUM_PROTECTION";

        // Roles de infraestructura crítica prevalecen sobre la topología nested.
        if (role is NodeRole.ClusterNode or NodeRole.HyperVClusterNode)
            return "CLUSTER_CRITICAL";

        if (role == NodeRole.DomainController)
            return "DOMAIN_CONTROLLER_CRITICAL";

        if (isNested)
            return "NESTED_HYPERV_HOST_CRITICAL";

        return role switch
        {
            NodeRole.PhysicalWorkstation => "WORKSTATION_SAFE",
            NodeRole.VirtualWorkstation => "WORKSTATION_VM_SAFE",
            NodeRole.PhysicalServer => "SERVER_SAFE",
            NodeRole.VirtualServer => "SERVER_VM_SAFE",
            NodeRole.HyperVHost => "HYPERV_HOST_CRITICAL",
            _ => "MAXIMUM_PROTECTION"
        };
    }

    private static DetectionConfidence ResolveConfidence(
        EnvironmentFacts facts,
        MachineContext context,
        bool guestEvidence)
    {
        if (!facts.ServiceProbeSucceeded)
            return DetectionConfidence.Low;

        if (context is MachineContext.Ambiguous or MachineContext.Unknown)
            return DetectionConfidence.Low;

        if (facts.HypervisorPresent && !guestEvidence && !facts.VmmsServicePresent && !facts.DeviceGuardProbeSucceeded)
            return DetectionConfidence.Medium;

        return DetectionConfidence.High;
    }

    private static VirtualizationPlatform DetectVirtualizationPlatform(string manufacturer, string model)
    {
        var source = $"{manufacturer} {model}".ToUpperInvariant();

        if (source.Contains("VMWARE"))
            return VirtualizationPlatform.VMware;
        if (source.Contains("VIRTUALBOX") || source.Contains("ORACLE CORPORATION"))
            return VirtualizationPlatform.VirtualBox;
        if (source.Contains("QEMU") || source.Contains("KVM") || source.Contains("RED HAT"))
            return VirtualizationPlatform.KvmQemu;
        if (source.Contains("MICROSOFT CORPORATION") && source.Contains("VIRTUAL MACHINE"))
            return VirtualizationPlatform.HyperV;

        return VirtualizationPlatform.None;
    }
}
