using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sims4ModManager.App.ViewModels;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private bool _setupChecked;

    /// <summary>Ctrl+F: focus the search box of the visible tab.</summary>
    public ICommand FocusSearchCommand { get; }

    /// <summary>Ctrl+K: focus the global search in the title bar.</summary>
    public ICommand FocusGlobalSearchCommand { get; }

    public MainWindow()
    {
        FocusSearchCommand = new RelayCommand(() =>
        {
            var box = TraySearchBox.IsVisible ? TraySearchBox : ModSearchBox;
            box.Focus();
            box.SelectAll();
        });

        FocusGlobalSearchCommand = new RelayCommand(() =>
        {
            GlobalSearchBox.Focus();
            GlobalSearchBox.SelectAll();
        });

        InitializeComponent();
        DataContext = _viewModel;
        RestorePlacement();
        _viewModel.ModScrollRequested += mod => Dispatcher.BeginInvoke(() =>
        {
            ModsGrid.ScrollIntoView(mod);
            ModsGrid.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);

        // Window backdrop/title bar follow the theme chosen in the view model (saved in the settings).
        Loaded += (_, _) => Services.ThemeService.Apply(_viewModel.IsDarkTheme, _viewModel.AccentColor);

        // First start: the setup assistant, once the main window is on screen; then a due scheduled backup.
        ContentRendered += async (_, _) =>
        {
            if (_setupChecked)
                return;
            _setupChecked = true;
            if (_viewModel.ShouldShowSetup)
                OpenSetup();
            await _viewModel.Saves.RunScheduledBackupIfDueAsync();
        };
    }

    private void OpenSetup()
    {
        var window = new SetupWindow(new SetupViewModel(_viewModel)) { Owner = this };
        window.ShowDialog();
    }

    private void OpenSetup_Click(object sender, RoutedEventArgs e) => OpenSetup();

    /// <summary>Picking a search result navigates to it.</summary>
    private void SearchResults_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is SearchResultViewModel result)
        {
            _viewModel.Search.OpenCommand.Execute(result);
            SearchResults.SelectedItem = null;
        }
    }

    private void LargestList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: StorageBarViewModel bar })
            _viewModel.Storage.ShowModCommand.Execute(bar);
    }

    private void ReplacementList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView { SelectedItem: GameReplacementViewModel vm })
            _viewModel.Game.ShowReplacementModCommand.Execute(vm);
    }

    private void MissingMeshList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView { SelectedItem: MissingMeshViewModel vm })
            _viewModel.Game.ShowMissingMeshModCommand.Execute(vm);
    }

    private void UpdateList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView { SelectedItem: CurseForgeModViewModel vm })
            _viewModel.Updates.ShowModCommand.Execute(vm);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Core.Localization.L.T(L.T("Sims 4 Mods-Ordner auswählen")),
            InitialDirectory = _viewModel.ModsPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetModsPath(dialog.FolderName);
        }
    }

    /// <summary>Restores the last window size/position, unless it would end up off-screen (e.g. monitor removed).</summary>
    private void RestorePlacement()
    {
        var placement = _viewModel.LoadWindowPlacement();
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var window = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var visible = Rect.Intersect(screen, window);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 50)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // RestoreBounds holds the normal (non-maximized) size even while maximized.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _viewModel.SaveWindowPlacement(new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = WindowState == WindowState.Maximized
        });
        base.OnClosing(e);
    }
}
