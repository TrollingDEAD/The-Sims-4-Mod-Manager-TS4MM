using Sims4ModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace Sims4ModManager.App;

public partial class MergeWindow : FluentWindow
{
    public MergeWindow(MergeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += merged =>
        {
            DialogResult = merged;
            Close();
        };
    }
}
