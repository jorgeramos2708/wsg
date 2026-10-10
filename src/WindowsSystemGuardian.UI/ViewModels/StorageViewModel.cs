namespace WindowsSystemGuardian.UI.ViewModels;

public sealed class StorageViewModel(SystemStateViewModel system)
{
    public SystemStateViewModel System { get; } = system;
}
