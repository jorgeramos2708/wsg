using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class IntegrityView : UserControl
{
    public IntegrityView() => InitializeComponent();

    private async void RefreshQuick_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrityViewModel vm)
            await vm.RefreshQuickAsync();
    }

    private async void DismScan_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrityViewModel vm)
            await vm.RunComponentStoreScanAsync();
    }

    private async void SfcVerify_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrityViewModel vm)
            await vm.RunSystemFileVerificationAsync();
    }

    private async void FileSystemScan_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrityViewModel vm)
            await vm.RunFileSystemScanAsync();
    }
}
