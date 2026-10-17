using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ActivityTests
{
    [Fact]
    public void LogIsBoundedNewestFirstAndDisplayFailuresAreIsolated()
    {
        var log = new ServerActivityLog();
        var notified = 0;
        log.Changed += () => throw new InvalidOperationException("Display unavailable");
        log.Changed += () => Interlocked.Increment(ref notified);
        Parallel.For(0, 500, i => log.Add("Request", "Serving"));
        log.Add("Last request", "Completed");
        var snapshot = log.Snapshot();
        Assert.Equal(ServerActivityLog.Capacity, snapshot.Length);
        Assert.Equal("Last request", snapshot[0].Operation);
        Assert.Equal(501, notified);
        snapshot[0] = new(default, "Modified copy", "", "");
        Assert.Equal("Last request", log.Snapshot()[0].Operation);
    }
}
