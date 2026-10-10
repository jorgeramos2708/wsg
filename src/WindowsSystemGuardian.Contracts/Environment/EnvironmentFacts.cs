namespace WindowsSystemGuardian.Contracts.Environment;

public sealed record EnvironmentFacts(
    string MachineName,
    string OperatingSystemCaption,
    string Version,
    string BuildNumber,
    uint ProductType,
    string Manufacturer,
    string Model,
    bool HypervisorPresent,
    ushort DomainRole,
    bool PartOfDomain,
    string Domain,
    bool VmmsServicePresent,
    bool ClusterServicePresent,
    bool ServiceProbeSucceeded = true,
    bool VbsRunning = false,
    bool DeviceGuardProbeSucceeded = true);
