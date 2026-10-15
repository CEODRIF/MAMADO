using AusbildungsWegInterview.Core.Audio;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class DeviceChangeMonitorTests
{
    private static DeviceChangeMonitor M => new();

    [Fact]
    public void NothingChanged_NoAction()
    {
        var (a, n, l) = M.Evaluate("d1", "d1", new[] { "d1", "d2" }, "d1");
        Assert.Equal(DeviceChangeAction.None, a);
        Assert.Null(n);
        Assert.Null(l);
    }

    [Fact]
    public void ExplicitSelection_StillActive_KeepsCapturing_EvenIfDefaultMoved()
    {
        // User explicitly picked d1; the default moved to d2 — we must NOT follow it.
        var (a, n, l) = M.Evaluate("d1", "d1", new[] { "d1", "d2" }, "d2");
        Assert.Equal(DeviceChangeAction.None, a);
        Assert.Null(n);
    }

    [Fact]
    public void ExplicitSelection_Lost_RaisesDeviceLost()
    {
        var (a, n, l) = M.Evaluate("d1", "d1", new[] { "d2" }, "d2");
        Assert.Equal(DeviceChangeAction.DeviceLost, a);
        Assert.Equal("d1", l);
    }

    [Fact]
    public void FollowDefault_DefaultMoved_RestartsOnNewDefault()
    {
        var (a, n, l) = M.Evaluate(null, "d1", new[] { "d1", "d2" }, "d2");
        Assert.Equal(DeviceChangeAction.RestartOnNewDefault, a);
        Assert.Equal("d2", n);
        Assert.Null(l);
    }

    [Fact]
    public void FollowDefault_SameDefault_NoAction()
    {
        var (a, n, l) = M.Evaluate(null, "d1", new[] { "d1", "d2" }, "d1");
        Assert.Equal(DeviceChangeAction.None, a);
    }

    [Fact]
    public void FollowDefault_CapturedDeviceLost_RaisesDeviceLost()
    {
        var (a, n, l) = M.Evaluate(null, "d1", new[] { "d2" }, "d2");
        Assert.Equal(DeviceChangeAction.DeviceLost, a);
        Assert.Equal("d1", l);
    }

    [Fact]
    public void FollowDefault_DeviceLost_AndNoDefault_StillDeviceLost()
    {
        var (a, n, l) = M.Evaluate(null, "d1", Array.Empty<string>(), null);
        Assert.Equal(DeviceChangeAction.DeviceLost, a);
        Assert.Equal("d1", l);
        Assert.Null(n);
    }

    [Fact]
    public void FollowDefault_NewDefaultCaseInsensitive()
    {
        var (a, n, _) = M.Evaluate(null, "D1", new[] { "D1", "D2" }, "d2");
        Assert.Equal(DeviceChangeAction.RestartOnNewDefault, a);
        Assert.Equal("d2", n);
    }
}
