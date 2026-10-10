using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Core.Environment;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class EnvironmentClassifierTests
{
    private readonly EnvironmentClassifier _sut = new();

    [Fact]
    public void PhysicalServer_IsNotMistakenForVm_WhenHypervisorIsAbsent()
    {
        var profile = _sut.Classify(Facts(productType: 3));

        Assert.Equal(NodeRole.PhysicalServer, profile.NodeRole);
        Assert.Equal(MachineContext.PhysicalConfirmed, profile.MachineContext);
        Assert.False(profile.IsVirtualMachine);
        Assert.Equal(VirtualizationPlatform.None, profile.VirtualizationPlatform);
        Assert.Equal("SERVER_SAFE", profile.PolicyName);
    }

    [Fact]
    public void HyperVHost_IsNotMistakenForGuest_OnlyBecauseHypervisorIsPresent()
    {
        var facts = Facts(
            productType: 3,
            manufacturer: "Dell Inc.",
            model: "PowerEdge R750",
            hypervisorPresent: true,
            vmms: true);

        var profile = _sut.Classify(facts);

        Assert.Equal(NodeRole.HyperVHost, profile.NodeRole);
        Assert.Equal(MachineContext.HypervisorHostConfirmed, profile.MachineContext);
        Assert.True(profile.IsHyperVHost);
        Assert.False(profile.IsVirtualMachine);
        Assert.Equal("HYPERV_HOST_CRITICAL", profile.PolicyName);
    }

    [Fact]
    public void HyperVGuest_IsClassifiedAsVirtualServer()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "Microsoft Corporation",
            model: "Virtual Machine",
            hypervisorPresent: true));

        Assert.Equal(NodeRole.VirtualServer, profile.NodeRole);
        Assert.Equal(MachineContext.VirtualGuestConfirmed, profile.MachineContext);
        Assert.True(profile.IsVirtualMachine);
        Assert.Equal(VirtualizationPlatform.HyperV, profile.VirtualizationPlatform);
        Assert.Equal("SERVER_VM_SAFE", profile.PolicyName);
    }

    [Fact]
    public void ProxmoxKvmGuest_IsClassifiedAsKvmQemu()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "QEMU",
            model: "Standard PC (Q35 + ICH9, 2009)",
            hypervisorPresent: true));

        Assert.True(profile.IsVirtualMachine);
        Assert.Equal(MachineContext.VirtualGuestConfirmed, profile.MachineContext);
        Assert.Equal(VirtualizationPlatform.KvmQemu, profile.VirtualizationPlatform);
    }

    [Fact]
    public void HypervisorPresentWithoutGuestOrHostEvidence_IsAmbiguousAndMostRestrictive()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "Generic Hardware",
            model: "Unknown Platform",
            hypervisorPresent: true));

        Assert.Equal(MachineContext.Ambiguous, profile.MachineContext);
        Assert.Equal(NodeRole.Unknown, profile.NodeRole);
        Assert.False(profile.IsVirtualMachine);
        Assert.Equal(VirtualizationPlatform.Unknown, profile.VirtualizationPlatform);
        Assert.Equal("MAXIMUM_PROTECTION", profile.PolicyName);
        Assert.Equal(DetectionConfidence.Low, profile.Confidence);
    }

    [Fact]
    public void WindowsWorkstation_WithVmms_IsRecognizedAsHyperVHost()
    {
        var profile = _sut.Classify(Facts(
            productType: 1,
            manufacturer: "Microsoft Corporation",
            model: "Surface Laptop Studio",
            hypervisorPresent: true,
            vmms: true));

        Assert.Equal(NodeRole.HyperVHost, profile.NodeRole);
        Assert.Equal(MachineContext.HypervisorHostConfirmed, profile.MachineContext);
        Assert.False(profile.IsVirtualMachine);
        Assert.Equal("HYPERV_HOST_CRITICAL", profile.PolicyName);
    }

    [Fact]
    public void GenericFailoverClusterNode_ReceivesClusterPolicy()
    {
        var profile = _sut.Classify(Facts(productType: 3, cluster: true));

        Assert.Equal(NodeRole.ClusterNode, profile.NodeRole);
        Assert.Equal("CLUSTER_CRITICAL", profile.PolicyName);
    }

    [Fact]
    public void HyperVClusterNode_ReceivesMostRestrictiveClusterPolicy()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "HPE",
            model: "ProLiant DL380",
            hypervisorPresent: true,
            vmms: true,
            cluster: true));

        Assert.Equal(NodeRole.HyperVClusterNode, profile.NodeRole);
        Assert.Equal("CLUSTER_CRITICAL", profile.PolicyName);
    }


    [Fact]
    public void PhysicalWindows11_WithVbsRunning_IsNotMistakenForGuest()
    {
        var profile = _sut.Classify(Facts(
            productType: 1,
            manufacturer: "Dell Inc.",
            model: "Latitude 7440",
            hypervisorPresent: true,
            vbsRunning: true));

        Assert.Equal(MachineContext.PhysicalConfirmed, profile.MachineContext);
        Assert.Equal(NodeRole.PhysicalWorkstation, profile.NodeRole);
        Assert.False(profile.IsVirtualMachine);
        Assert.True(profile.UsesWindowsHypervisorForSecurity);
        Assert.Equal("WORKSTATION_SAFE", profile.PolicyName);
    }

    [Fact]
    public void PhysicalServer_WithVbsRunning_RemainsPhysicalServer()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "HPE",
            model: "ProLiant DL380",
            hypervisorPresent: true,
            vbsRunning: true));

        Assert.Equal(MachineContext.PhysicalConfirmed, profile.MachineContext);
        Assert.Equal(NodeRole.PhysicalServer, profile.NodeRole);
        Assert.False(profile.IsVirtualMachine);
        Assert.True(profile.UsesWindowsHypervisorForSecurity);
        Assert.Equal("SERVER_SAFE", profile.PolicyName);
    }

    [Fact]
    public void VMwareGuest_IsClassifiedAsVirtualServer()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "VMware, Inc.",
            model: "VMware Virtual Platform",
            hypervisorPresent: true));

        Assert.Equal(MachineContext.VirtualGuestConfirmed, profile.MachineContext);
        Assert.True(profile.IsVirtualMachine);
        Assert.Equal(VirtualizationPlatform.VMware, profile.VirtualizationPlatform);
        Assert.Equal("SERVER_VM_SAFE", profile.PolicyName);
    }

    [Fact]
    public void HypervisorPresent_WithFailedDeviceGuardProbe_IsAmbiguousAndFailsClosed()
    {
        var profile = _sut.Classify(Facts(
            productType: 1,
            manufacturer: "Dell Inc.",
            model: "Latitude",
            hypervisorPresent: true,
            deviceGuardProbeSucceeded: false));

        Assert.Equal(MachineContext.Ambiguous, profile.MachineContext);
        Assert.Equal("MAXIMUM_PROTECTION", profile.PolicyName);
        Assert.NotEqual(DetectionConfidence.High, profile.Confidence);
    }

    [Fact]
    public void NestedHyperVGuest_RemainsGuestAndReceivesCriticalNestedPolicy()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "Microsoft Corporation",
            model: "Virtual Machine",
            hypervisorPresent: true,
            vmms: true));

        Assert.True(profile.IsVirtualMachine);
        Assert.True(profile.IsHyperVHost);
        Assert.True(profile.IsNestedVirtualization);
        Assert.Equal(MachineContext.VirtualGuestConfirmed, profile.MachineContext);
        Assert.Equal(NodeRole.VirtualServer, profile.NodeRole);
        Assert.Equal("NESTED_HYPERV_HOST_CRITICAL", profile.PolicyName);
    }

    [Fact]
    public void NestedClusterGuest_KeepsClusterCriticalPolicy()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            manufacturer: "Microsoft Corporation",
            model: "Virtual Machine",
            hypervisorPresent: true,
            vmms: true,
            cluster: true));

        Assert.True(profile.IsVirtualMachine);
        Assert.True(profile.IsNestedVirtualization);
        Assert.Equal(NodeRole.ClusterNode, profile.NodeRole);
        Assert.Equal("CLUSTER_CRITICAL", profile.PolicyName);
    }

    [Fact]
    public void ServiceProbeFailure_FailsClosedToMaximumProtection()
    {
        var profile = _sut.Classify(Facts(
            productType: 3,
            serviceProbeSucceeded: false));

        Assert.Equal("MAXIMUM_PROTECTION", profile.PolicyName);
        Assert.Equal(DetectionConfidence.Low, profile.Confidence);
        Assert.False(profile.EnvironmentSignalsComplete);
    }

    private static EnvironmentFacts Facts(
        uint productType,
        string manufacturer = "Dell Inc.",
        string model = "Physical Server",
        bool hypervisorPresent = false,
        bool vmms = false,
        bool cluster = false,
        bool serviceProbeSucceeded = true,
        bool vbsRunning = false,
        bool deviceGuardProbeSucceeded = true) => new(
            "TEST",
            productType == 1 ? "Windows 11 Pro" : "Windows Server 2025 Datacenter",
            "10.0",
            "26100",
            productType,
            manufacturer,
            model,
            hypervisorPresent,
            0,
            false,
            "WORKGROUP",
            vmms,
            cluster,
            serviceProbeSucceeded,
            vbsRunning,
            deviceGuardProbeSucceeded);
}
