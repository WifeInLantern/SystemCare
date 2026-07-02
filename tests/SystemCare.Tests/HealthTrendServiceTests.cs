using System.IO;
using SystemCare.Services;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// <see cref="HealthTrendService"/> keeps at most one health snapshot per local calendar day
/// (a later scan replaces today's earlier one), caps the series at 365 entries oldest-out,
/// and tolerates a corrupt store by starting empty. Uses the internal path-seam constructor
/// so nothing touches the real %AppData% store.
/// </summary>
public class HealthTrendServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"systemcare-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (Exception) { }
    }

    [Fact]
    public void Record_SameDayTwice_KeepsOnlyTheLatestScore()
    {
        var svc = new HealthTrendService(_path);

        svc.Record(70);
        svc.Record(85);

        var all = svc.GetAll();
        var snap = Assert.Single(all);
        Assert.Equal(85, snap.Score);
    }

    [Fact]
    public void Record_PersistsAcrossInstances()
    {
        new HealthTrendService(_path).Record(70);

        var reloaded = new HealthTrendService(_path);
        Assert.Equal(70, Assert.Single(reloaded.GetAll()).Score);
    }

    [Fact]
    public void CorruptFile_StartsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{ not valid json !!");

        var svc = new HealthTrendService(_path);
        Assert.Empty(svc.GetAll());

        svc.Record(50); // and recovers by writing a fresh store
        Assert.Single(svc.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsACopy_NotTheLiveList()
    {
        var svc = new HealthTrendService(_path);
        svc.Record(70);

        var first = svc.GetAll();
        svc.Record(90); // same-day replace

        Assert.Equal(70, Assert.Single(first).Score); // earlier snapshot list unaffected
    }
}
