namespace Sims4ModManager.App.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>A single background-event notification shown briefly in the toast stack (see <see cref="MainViewModel.ShowToast"/>).</summary>
public sealed class ToastViewModel
{
    public ToastViewModel(string message, ToastKind kind)
    {
        Message = message;
        Kind = kind;
    }

    public string Message { get; }
    public ToastKind Kind { get; }
}
