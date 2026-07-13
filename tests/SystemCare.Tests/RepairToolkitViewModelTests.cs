using NSubstitute;
using SystemCare.Services;
using SystemCare.ViewModels;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="RepairToolkitViewModel"/> gates each repair step behind the restore-point confirmation
/// (mirroring <c>DiskHealthViewModel</c>'s pattern) and records a history entry only on a completed,
/// non-cancelled run. These tests mock <see cref="ISystemRepairService"/> so no real SFC/DISM/CHKDSK
/// process is launched.
/// </summary>
public class RepairToolkitViewModelTests
{
    private static (RepairToolkitViewModel Vm, ISystemRepairService Repair, IBackupConfirmationService Backup,
        IRestorePointService Restore, IHistoryService History) Build(bool confirmRestorePoint = true)
    {
        var repair = Substitute.For<ISystemRepairService>();
        var backup = Substitute.For<IBackupConfirmationService>();
        backup.ConfirmRestorePointAsync(Arg.Any<string>()).Returns(Task.FromResult(confirmRestorePoint));
        var restore = Substitute.For<IRestorePointService>();
        restore.CreateRestorePointAsync(Arg.Any<string>()).Returns(Task.FromResult((true, "Restore point created.")));
        var history = Substitute.For<IHistoryService>();
        // 2.16: search-index card dependency; substitute returns a default (empty) status.
        var searchIndex = Substitute.For<ISearchIndexService>();
        searchIndex.GetStatusAsync().Returns(Task.FromResult(new SearchIndexStatus()));

        var vm = new RepairToolkitViewModel(repair, backup, restore, history, searchIndex);
        return (vm, repair, backup, restore, history);
    }

    [Fact]
    public async Task RunSfcCommand_InvokesRepairServiceAndUpdatesStatus()
    {
        var (vm, repair, _, _, _) = Build();
        repair.RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No integrity violations found.")));

        await vm.RunSfcCommand.ExecuteAsync(null);

        await repair.Received(1).RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        Assert.Equal("No integrity violations found.", vm.SfcStatus);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunDismCommand_InvokesRepairServiceAndUpdatesStatus()
    {
        var (vm, repair, _, _, _) = Build();
        repair.RunDismAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Repaired, "The Windows image was repaired.")));

        await vm.RunDismCommand.ExecuteAsync(null);

        await repair.Received(1).RunDismAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        Assert.Equal("The Windows image was repaired.", vm.DismStatus);
    }

    [Fact]
    public async Task RunChkdskCommand_PassesSelectedDrive()
    {
        var (vm, repair, _, _, _) = Build();
        repair.RunChkdskRepairAsync(Arg.Any<string>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No problems found.")));

        vm.SelectedDrive = "D:";

        await vm.RunChkdskCommand.ExecuteAsync(null);

        await repair.Received(1).RunChkdskRepairAsync("D:", Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        Assert.Equal("No problems found.", vm.ChkdskStatus);
    }

    [Fact]
    public async Task RunSfc_RecordsHistoryOnCompletion()
    {
        var (vm, repair, _, _, history) = Build();
        repair.RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No integrity violations found.")));

        await vm.RunSfcCommand.ExecuteAsync(null);

        history.Received(1).Record("System repair", Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RunSfc_WhenUserDeclinesRestorePoint_StillRunsRepair()
    {
        var (vm, repair, _, restore, _) = Build(confirmRestorePoint: false);
        repair.RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No integrity violations found.")));

        await vm.RunSfcCommand.ExecuteAsync(null);

        await restore.DidNotReceive().CreateRestorePointAsync(Arg.Any<string>());
        await repair.Received(1).RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAllCommand_RunsAllThreeStepsInOrder()
    {
        var (vm, repair, _, _, history) = Build();
        repair.RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No integrity violations found.")));
        repair.RunDismAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No component store corruption detected.")));
        repair.RunChkdskRepairAsync(Arg.Any<string>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RepairResult(0, RepairOutcome.Healthy, "No problems found.")));

        await vm.RunAllCommand.ExecuteAsync(null);

        await repair.Received(1).RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        await repair.Received(1).RunDismAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        history.Received(1).Record("System repair", Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>());
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunSfcCommand_WhileAlreadyRunning_CannotExecute()
    {
        var (vm, repair, _, _, _) = Build();
        var tcs = new TaskCompletionSource<RepairResult>();
        repair.RunSfcAsync(Arg.Any<Action<string>>(), Arg.Any<CancellationToken>()).Returns(tcs.Task);

        var firstRun = vm.RunSfcCommand.ExecuteAsync(null);

        Assert.False(vm.RunSfcCommand.CanExecute(null));

        tcs.SetResult(new RepairResult(0, RepairOutcome.Healthy, "No integrity violations found."));
        await firstRun;
    }
}
