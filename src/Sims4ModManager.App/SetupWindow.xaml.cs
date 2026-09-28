using Sims4ModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace Sims4ModManager.App;

public partial class SetupWindow : FluentWindow
{
    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        // Closing with the X counts as done as well - the assistant should not nag on every start.
        Closed += (_, _) => viewModel.Main.Settings.TryUpdate(s => s.SetupCompleted = true);
    }
}
