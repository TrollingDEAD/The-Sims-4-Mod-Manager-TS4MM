namespace Sims4ModManager.App.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// A single background-event notification shown briefly in the toast stack (see
/// <see cref="MainViewModel.ShowToast"/>) and kept afterward in the toast history (bell icon) so one
/// missed while away from the keyboard isn't lost for good once it auto-dismisses.
/// </summary>
public sealed class ToastViewModel
{
    public ToastViewModel(string message, ToastKind kind)
    {
        Message = message;
        Kind = kind;
        Timestamp = DateTime.Now;
    }

    public string Message { get; }
    public ToastKind Kind { get; }
    public DateTime Timestamp { get; }
    public string TimeLabel => Timestamp.ToString("t", Sims4ModManager.Core.Localization.L.Culture);
}
