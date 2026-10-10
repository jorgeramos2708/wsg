using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler<string>? NavigateRequested;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void Navigate_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string destination)
            NavigateRequested?.Invoke(this, destination);
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel)
            await viewModel.RefreshAsync();
    }
}
