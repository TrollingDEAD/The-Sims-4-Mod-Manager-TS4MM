using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.Services;

/// <summary>File pickers and message boxes, kept out of the view models so they only decide what to ask.</summary>
public interface IDialogService
{
    IReadOnlyList<string> PickFiles(string title, string filter);
    string? PickFolder(string title);
    string? PickSaveFile(string title, string filter, string defaultFileName);
    Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null, bool destructive = false);
    Task ShowAsync(string title, string message);
}

/// <summary>
/// Uses the themed WPF-UI message box (the native one ignores the app's dark theme)
/// and the standard Windows file dialogs.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public IReadOnlyList<string> PickFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null, bool destructive = false)
    {
        var box = CreateBox(title, message);
        box.PrimaryButtonText = confirmText ?? L.T("Ja");
        box.PrimaryButtonAppearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary;
        box.CloseButtonText = cancelText ?? L.T("Abbrechen");
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public async Task ShowAsync(string title, string message)
    {
        var box = CreateBox(title, message);
        box.CloseButtonText = "OK";
        box.CloseButtonAppearance = ControlAppearance.Primary;
        await box.ShowDialogAsync();
    }

    private static MessageBox CreateBox(string title, string message) => new()
    {
        Title = title,
        Owner = Owner,
        MaxWidth = 640,
        Content = new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 580
        }
    };
}
