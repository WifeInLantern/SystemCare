using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// The pure backup policy: maps the two user settings (create-before-maintenance, ask-each-time) to one of
/// skip / create-silently / ask. Disabling restore points wins over "ask"; "ask" only applies when they're on.
/// </summary>
public class BackupConfirmationPolicyTests
{
    [Theory]
    [InlineData(false, false)] // restore points off
    [InlineData(false, true)]  // off wins even if "ask" is set
    public void Disabled_Skips(bool createBeforeMaintenance, bool askEachTime)
    {
        Assert.Equal(BackupChoice.Skip,
            BackupConfirmationService.Resolve(createBeforeMaintenance, askEachTime));
    }

    [Fact]
    public void Enabled_AskOff_CreatesSilently()
    {
        Assert.Equal(BackupChoice.CreateSilently,
            BackupConfirmationService.Resolve(createBeforeMaintenance: true, askEachTime: false));
    }

    [Fact]
    public void Enabled_AskOn_Asks()
    {
        Assert.Equal(BackupChoice.Ask,
            BackupConfirmationService.Resolve(createBeforeMaintenance: true, askEachTime: true));
    }
}
