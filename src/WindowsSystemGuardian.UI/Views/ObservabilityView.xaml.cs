using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class ObservabilityView : UserControl
{
    public ObservabilityView() => InitializeComponent();

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ObservabilityViewModel viewModel)
            await viewModel.RefreshAsync();
    }
}
