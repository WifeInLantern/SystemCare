using Microsoft.Win32;
using NSubstitute;
using SystemCare.Models;
using SystemCare.Services;
using SystemCare.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="RegistryCleanerViewModel"/> orchestrates <see cref="IRegistryCleanerService"/> scan/clean
/// results into the UI-bound <c>Issues</c>/<c>Categories</c> collections and status text. These tests
/// mock the service so the scan/restore flows can be verified without touching the real registry.
/// </summary>
public class RegistryCleanerViewModelTests
{
    private static readonly RegistryCategory[] Categories =
    [
        new() { Id = "uninstall", Name = "Uninstall leftovers", Description = "d1" },
        new() { Id = "apppaths", Name = "Invalid App Paths", Description = "d2" },
    ];

    private static RegistryIssue Issue(string categoryId) => new()
    {
        CategoryId = categoryId,
        CategoryName = categoryId,
        Hive = RegistryHive.CurrentUser,
        View = RegistryView.Default,
        SubKeyPath = @"Software\Foo",
    };

    private static (RegistryCleanerViewModel Vm, IRegistryCleanerService Registry) Build()
    {
        var registry = Substitute.For<IRegistryCleanerService>();
        registry.Categories.Returns(Categories);
        var vm = new RegistryCleanerViewModel(registry, Substitute.For<ISnackbarService>(),
            Substitute.For<IContentDialogService>(), Substitute.For<IHistoryService>());
        return (vm, registry);
    }

    [Fact]
    public void Constructor_PopulatesCategoriesFromService()
    {
        var (vm, _) = Build();

        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal("uninstall", vm.Categories[0].Category.Id);
        Assert.True(vm.Categories[0].IsSelected);
    }

    [Fact]
    public async Task ScanCommand_WithIssuesFound_PopulatesIssuesAndEnablesClean()
    {
        var (vm, registry) = Build();
        var found = new List<RegistryIssue> { Issue("uninstall"), Issue("uninstall"), Issue("apppaths") };
        registry.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(found));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Issues.Count);
        Assert.True(vm.CanClean);
        Assert.Equal(2, vm.Categories.Single(c => c.Category.Id == "uninstall").FoundCount);
        Assert.Equal(1, vm.Categories.Single(c => c.Category.Id == "apppaths").FoundCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ScanCommand_NoIssuesFound_DisablesCleanAndReportsClean()
    {
        var (vm, registry) = Build();
        registry.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RegistryIssue>()));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Issues);
        Assert.False(vm.CanClean);
        Assert.Equal("No invalid registry entries found — your registry looks clean.", vm.StatusText);
    }

    [Fact]
    public async Task ScanCommand_PassesOnlySelectedCategoryIds()
    {
        var (vm, registry) = Build();
        vm.Categories.Single(c => c.Category.Id == "apppaths").IsSelected = false;
        registry.ScanAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RegistryIssue>()));

        await vm.ScanCommand.ExecuteAsync(null);

        await registry.Received(1).ScanAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "uninstall" })),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OpenBackupsFolderCommand_DelegatesToService()
    {
        var (vm, registry) = Build();

        vm.OpenBackupsFolderCommand.Execute(null);

        registry.Received(1).OpenBackupsFolder();
    }

    [Fact]
    public async Task RestoreLastBackupCommand_UpdatesStatusTextFromServiceMessage()
    {
        var (vm, registry) = Build();
        registry.RestoreLastBackupAsync().Returns(Task.FromResult((true, "Backup restored from 2026-06-30.")));

        await vm.RestoreLastBackupCommand.ExecuteAsync(null);

        Assert.Equal("Backup restored from 2026-06-30.", vm.StatusText);
    }
}
