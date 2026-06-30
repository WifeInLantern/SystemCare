using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.Services;

public interface IConfirmDialogService
{
    Task<bool> ConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText = "Cancel");
}

public sealed class ConfirmDialogService(IContentDialogService dialogs) : IConfirmDialogService
{
    public async Task<bool> ConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText = "Cancel")
    {
        var result = await dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
        });
        return result == ContentDialogResult.Primary;
    }
}
