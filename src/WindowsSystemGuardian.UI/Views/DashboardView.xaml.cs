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

    /// <summary>0.7.0: el monitoreo en vivo corre solo mientras el Dashboard es visible.</summary>
    private void DashboardView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel)
            viewModel.Live.Start();
    }

    private void DashboardView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel)
            viewModel.Live.Stop();
    }

    private void LiveToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel)
            viewModel.Live.Toggle();
    }
}
