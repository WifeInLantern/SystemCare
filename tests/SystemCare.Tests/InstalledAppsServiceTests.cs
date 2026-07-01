using SystemCare.Models;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="InstalledAppsService.UninstallAsync"/> runs the app's uninstaller via <c>cmd /c</c> and now
/// reports the real outcome from the process exit code, so a cancelled/failed uninstall (non-zero exit) is
/// distinguished from success and the caller can skip the leftover scan. These drive a harmless
/// <c>cmd /c exit N</c> as the stand-in "uninstaller".
/// </summary>
public class InstalledAppsServiceTests
{
    private static InstalledApp AppWithUninstall(string command) => new()
    {
        Name = "Test App",
        UninstallString = command,
    };

    [Fact]
    public async Task UninstallAsync_ExitCodeZero_ReturnsTrue()
    {
        var svc = new InstalledAppsService();
        Assert.True(await svc.UninstallAsync(AppWithUninstall("exit 0")));
    }

    [Fact]
    public async Task UninstallAsync_NonZeroExit_ReturnsFalse()
    {
        var svc = new InstalledAppsService();
        Assert.False(await svc.UninstallAsync(AppWithUninstall("exit 7")));
    }

    [Theory]
    [InlineData("exit 3010")]  // success, reboot required
    [InlineData("exit 1641")]  // success, reboot initiated
    public async Task UninstallAsync_RebootSuccessCodes_ReturnTrue(string command)
    {
        var svc = new InstalledAppsService();
        Assert.True(await svc.UninstallAsync(AppWithUninstall(command)));
    }
}
