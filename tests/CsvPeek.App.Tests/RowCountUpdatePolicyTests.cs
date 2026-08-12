using CsvPeek.App;
using Xunit;

namespace CsvPeek.App.Tests;

public sealed class RowCountUpdatePolicyTests
{
    [Fact]
    public void ProgressStartsOneTimerWithoutRestartingIt()
    {
        var policy = new RowCountUpdatePolicy();

        Assert.Equal(RowCountUpdateAction.StartTimer, policy.Request(forceExact: false, indexComplete: false));
        Assert.Equal(RowCountUpdateAction.None, policy.Request(forceExact: false, indexComplete: false));
        Assert.True(policy.IsPending);
    }

    [Fact]
    public void VerticalScrollDefersOnlyAPendingUpdate()
    {
        var policy = new RowCountUpdatePolicy();
        Assert.Equal(RowCountUpdateAction.None, policy.DeferForVerticalScroll());

        policy.Request(forceExact: false, indexComplete: false);

        Assert.Equal(RowCountUpdateAction.RestartTimer, policy.DeferForVerticalScroll());
        Assert.Equal(RowCountUpdateAction.ApplyDeferred, policy.TimerElapsed());
        Assert.False(policy.IsPending);
    }

    [Fact]
    public void CompletionCancelsPendingUpdateAndAppliesExactCount()
    {
        var policy = new RowCountUpdatePolicy();
        policy.Request(forceExact: false, indexComplete: false);

        Assert.Equal(RowCountUpdateAction.ApplyExact, policy.Request(forceExact: false, indexComplete: true));
        Assert.Equal(RowCountUpdateAction.None, policy.TimerElapsed());
        Assert.False(policy.IsPending);
    }
}
