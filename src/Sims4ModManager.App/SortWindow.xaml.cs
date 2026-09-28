using Sims4ModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace Sims4ModManager.App;

public partial class SortWindow : FluentWindow
{
    public SortWindow(SortViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += sorted =>
        {
            DialogResult = sorted;
            Close();
        };
    }
}
