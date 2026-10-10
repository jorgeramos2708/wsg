using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is HistoryViewModel viewModel)
            await viewModel.RefreshAsync();
    }
}
