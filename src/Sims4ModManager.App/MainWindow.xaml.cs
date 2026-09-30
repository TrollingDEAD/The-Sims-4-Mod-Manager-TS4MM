using System.ComponentModel;
using System.IO;
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

        // Must run synchronously, not via Dispatcher.BeginInvoke: the whole point is to close the
        // edit transaction before the caller's own rescan can touch the Mods collection.
        _viewModel.ModEditCommitNeeded += () =>
        {
            ModsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            ModsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        };

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

    /// <summary>
    /// Feeds the grid's multi-selection into the view model for bulk tagging - WPF's DataGrid has no
    /// two-way SelectedItems binding, so this is the one place that has to bridge it by hand.
    /// </summary>
    private void ModsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        _viewModel.SetSelectedMods(ModsGrid.SelectedItems.Cast<ModEntryViewModel>());

    // --- Drag-and-drop install: drop a downloaded archive/mod/folder anywhere on the window and have -----
    // it go through the same install pipeline (safety scan included) as a watched Downloads file. ----------

    private static bool HasDroppableFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) &&
        ((string[])e.Data.GetData(DataFormats.FileDrop)!).Any(IsDroppableInstallSource);

    private static bool IsDroppableInstallSource(string path) =>
        Directory.Exists(path) || Sims4ModManager.Core.Tray.TrayInstaller.IsArchive(path) ||
        Sims4ModManager.Core.ModFileNaming.IsManagedModFile(Path.GetFileName(path)) ||
        Sims4ModManager.Core.Tray.TrayFileName.HasTrayExtension(path);

    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        bool droppable = HasDroppableFiles(e);
        e.Effects = droppable ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = droppable ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void MainWindow_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var sources = ((string[])e.Data.GetData(DataFormats.FileDrop)!).Where(IsDroppableInstallSource).ToList();
        if (sources.Count > 0)
            await _viewModel.Tray.InstallAsync(sources);
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

    /// <summary>Loads the Browse tab's category list (and an initial listing) the first time it's
    /// actually opened, instead of hitting the CurseForge API as soon as the app starts. Checked by
    /// index (Browse is the second sub-tab), not by header text, since the header gets translated at
    /// runtime for non-German locales. Uses e.Source, not sender: the ComboBox/ListBox inside the
    /// Browse tab are Selectors too, and their own SelectionChanged bubbles up into this same
    /// handler (sender is always the TabControl it's attached to either way) - e.Source is only the
    /// TabControl itself when a sub-tab was actually switched.</summary>
    private void CurseForgeSubTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source is System.Windows.Controls.TabControl { SelectedIndex: 1 })
            _ = _viewModel.Browse.EnsureCategoriesLoadedAsync();
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
