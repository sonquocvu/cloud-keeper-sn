using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.Domain.Tests;

public sealed class TransferStateAndRetryTests
{
    [Theory]
    [InlineData(TransferState.Discovered, TransferState.Planned)]
    [InlineData(TransferState.Waiting, TransferState.Paused)]
    [InlineData(TransferState.Paused, TransferState.Waiting)]
    [InlineData(TransferState.Failed, TransferState.Waiting)]
    public void StateMachine_AllowsExpectedTransitions(TransferState from, TransferState to)
    {
        Assert.Equal(to, TransferStateMachine.Transition(from, to));
    }

    [Fact]
    public void StateMachine_RejectsResumeAfterCompletion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TransferStateMachine.Transition(TransferState.Completed, TransferState.Waiting));
    }

    [Fact]
    public void RetryPolicy_UsesExponentialBackoff()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

        var first = policy.Decide(0, jitterFraction: 1);
        var third = policy.Decide(2, jitterFraction: 1);

        Assert.Equal(TimeSpan.FromSeconds(2), first.Delay);
        Assert.Equal(TimeSpan.FromSeconds(8), third.Delay);
    }

    [Fact]
    public void RetryPolicy_HonorsLongerRetryAfter()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(2));

        var decision = policy.Decide(1, TimeSpan.FromSeconds(45), jitterFraction: 0);

        Assert.Equal(TimeSpan.FromSeconds(45), decision.Delay);
    }
}

