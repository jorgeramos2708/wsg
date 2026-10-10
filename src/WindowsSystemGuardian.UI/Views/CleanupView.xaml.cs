using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class CleanupView : UserControl
{
    public CleanupView() => InitializeComponent();

    private async void AnalyzeCleanup_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CleanupViewModel viewModel)
            await viewModel.AnalyzeAsync();
    }
}
