using Xunit;

namespace SmartWindowTool.Tests;

public class OperationVersionTests
{
    [Fact]
    public void Invalidate_MakesCapturedVersionStale()
    {
        var version = new OperationVersion();
        int captured = version.Capture();

        version.Invalidate();

        Assert.False(version.IsCurrent(captured));
        Assert.True(version.IsCurrent(version.Capture()));
    }
}
