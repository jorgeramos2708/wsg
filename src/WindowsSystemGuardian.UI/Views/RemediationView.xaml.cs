using System.Windows;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class RemediationView : Window
{
    public RemediationView() => InitializeComponent();

    private async void Execute_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemediationViewModel vm)
            await vm.ExecuteAsync();
    }

    private void Approval_OnChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemediationViewModel vm)
            vm.SetOperatorApproved(true);
    }

    private void Approval_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemediationViewModel vm)
            vm.SetOperatorApproved(false);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
