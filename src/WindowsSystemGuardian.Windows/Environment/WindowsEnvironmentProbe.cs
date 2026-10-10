using System.Management;
using WindowsSystemGuardian.Contracts.Environment;
using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Environment;

public sealed class WindowsEnvironmentProbe : IEnvironmentProbe
{
    public Task<EnvironmentFacts> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Probe(cancellationToken), cancellationToken);

    private static EnvironmentFacts Probe(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows System Guardian requiere Windows.");

        cancellationToken.ThrowIfCancellationRequested();

        var osCaption = System.Environment.OSVersion.VersionString;
        var version = System.Environment.OSVersion.Version.ToString();
        var build = System.Environment.OSVersion.Version.Build.ToString();
        uint productType = 1;

        using (var searcher = new ManagementObjectSearcher(
                   "SELECT Caption, Version, BuildNumber, ProductType FROM Win32_OperatingSystem"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                osCaption = ReadString(item, "Caption", osCaption);
                version = ReadString(item, "Version", version);
                build = ReadString(item, "BuildNumber", build);
                productType = ReadUInt32(item, "ProductType", productType);
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var manufacturer = "Unknown";
        var model = "Unknown";
        var hypervisorPresent = false;
        ushort domainRole = 0;
        var partOfDomain = false;
        var domain = string.Empty;

        using (var searcher = new ManagementObjectSearcher(
                   "SELECT Manufacturer, Model, HypervisorPresent, DomainRole, PartOfDomain, Domain FROM Win32_ComputerSystem"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                manufacturer = ReadString(item, "Manufacturer", manufacturer);
                model = ReadString(item, "Model", model);
                hypervisorPresent = ReadBool(item, "HypervisorPresent");
                domainRole = ReadUInt16(item, "DomainRole");
                partOfDomain = ReadBool(item, "PartOfDomain");
                domain = ReadString(item, "Domain", string.Empty);
                break;
            }
        }

        var services = QueryServicePresence();
        var deviceGuard = QueryDeviceGuard();

        return new EnvironmentFacts(
            System.Environment.MachineName,
            osCaption,
            version,
            build,
            productType,
            manufacturer,
            model,
            hypervisorPresent,
            domainRole,
            partOfDomain,
            domain,
            services.Vmms,
            services.Cluster,
            services.Succeeded,
            deviceGuard.VbsRunning,
            deviceGuard.Succeeded);
    }

    private static (bool Vmms, bool Cluster, bool Succeeded) QueryServicePresence()
    {
        var vmms = false;
        var cluster = false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Service WHERE Name='vmms' OR Name='ClusSvc'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var name = ReadString(item, "Name", string.Empty);
                vmms |= name.Equals("vmms", StringComparison.OrdinalIgnoreCase);
                cluster |= name.Equals("ClusSvc", StringComparison.OrdinalIgnoreCase);
            }

            return (vmms, cluster, true);
        }
        catch
        {
            // Fallo fail-closed: el classifier recibirá Succeeded=false y aplicará MAXIMUM_PROTECTION.
            return (false, false, false);
        }
    }

    private static (bool VbsRunning, bool Succeeded) QueryDeviceGuard()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard"));
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                var value = item["VirtualizationBasedSecurityStatus"];
                if (value is null)
                    return (false, true);

                // 0 = no habilitado, 1 = habilitado sin ejecutar, 2 = habilitado y ejecutándose.
                return (Convert.ToUInt32(value) == 2, true);
            }

            return (false, true);
        }
        catch
        {
            // En sistemas donde la clase/namespace no exista, la ausencia de señal se conserva
            // explícitamente para no usar HypervisorPresent como prueba de guest.
            return (false, false);
        }
    }

    private static string ReadString(ManagementObject item, string name, string fallback) =>
        item.Properties[name]?.Value?.ToString() is { Length: > 0 } value ? value : fallback;

    private static bool ReadBool(ManagementObject item, string name) =>
        item.Properties[name]?.Value is bool value && value;

    private static uint ReadUInt32(ManagementObject item, string name, uint fallback) =>
        item.Properties[name]?.Value is null ? fallback : Convert.ToUInt32(item.Properties[name]!.Value);

    private static ushort ReadUInt16(ManagementObject item, string name) =>
        item.Properties[name]?.Value is null ? (ushort)0 : Convert.ToUInt16(item.Properties[name]!.Value);
}
