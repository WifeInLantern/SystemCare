using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.Services;

/// <summary>What to do when a maintenance action is about to create a restore point.</summary>
public enum BackupChoice
{
    /// <summary>Restore points are disabled — skip without asking.</summary>
    Skip,
    /// <summary>Create the restore point without prompting.</summary>
    CreateSilently,
    /// <summary>Ask the user Yes/No first.</summary>
    Ask,
}

public interface IBackupConfirmationService
{
    /// <summary>
    /// Decides whether to create a restore point before <paramref name="operation"/>, honoring the user's
    /// backup preference (never / always / ask). In "ask" mode this shows a Yes/No dialog and returns the
    /// user's choice; <paramref name="operation"/> is a short phrase such as "installing Windows updates".
    /// </summary>
    Task<bool> ConfirmRestorePointAsync(string operation);
}

/// <summary>
/// Central gate for every automatic "create a restore point before maintenance" backup, so the user can be
/// asked Yes/No each time. Explicit, user-initiated "Create restore point" buttons bypass this (the click is
/// the consent).
/// </summary>
public sealed class BackupConfirmationService(ISettingsService settings, IContentDialogService dialogs)
    : IBackupConfirmationService
{
    /// <summary>Pure policy mapping the two settings to an action. Unit-tested.</summary>
    public static BackupChoice Resolve(bool createBeforeMaintenance, bool askEachTime) =>
        !createBeforeMaintenance ? BackupChoice.Skip
        : askEachTime ? BackupChoice.Ask
        : BackupChoice.CreateSilently;

    public async Task<bool> ConfirmRestorePointAsync(string operation)
    {
        switch (Resolve(settings.Current.CreateRestorePointBeforeMaintenance, settings.Current.AskBeforeBackup))
        {
            case BackupChoice.Skip: return false;
            case BackupChoice.CreateSilently: return true;
        }

        // Ask mode — show the prompt on the UI thread (callers may be on a worker thread).
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return true; // no UI available — default to the safe choice (back up)

        return dispatcher.CheckAccess()
            ? await PromptAsync(operation)
            : await dispatcher.InvokeAsync(() => PromptAsync(operation)).Task.Unwrap();
    }

    private async Task<bool> PromptAsync(string operation)
    {
        var result = await dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Create a restore point?",
            Content = $"Create a system restore point before {operation}?\n\n" +
                      "It lets you roll back if something goes wrong. Choose Skip to continue without one.",
            PrimaryButtonText = "Create restore point",
            CloseButtonText = "Skip",
        });
        return result == ContentDialogResult.Primary;
    }
}
