using System.Windows;
using System.Windows.Controls;
using WindowsSystemGuardian.UI.ViewModels;

namespace WindowsSystemGuardian.UI.Views;

public partial class IncidentsView : UserControl
{
    public IncidentsView() => InitializeComponent();

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IncidentsViewModel vm)
            await vm.RefreshAsync();
    }

    private void Remediate_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IncidentItemViewModel item })
            item.OpenRemediation();
    }
}
