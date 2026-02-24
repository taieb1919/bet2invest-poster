using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Tests.Services;

public class ExecutionStateServiceTests
{
    [Fact]
    public void InitialState_AllPropertiesAreNull()
    {
        var service = new ExecutionStateService();
        var state = service.GetState();

        Assert.Null(state.LastRunAt);
        Assert.Null(state.LastRunSuccess);
        Assert.Null(state.LastRunResult);
        Assert.Null(state.NextRunAt);
    }

    [Fact]
    public void RecordSuccess_SetsLastRunAtAndResult()
    {
        var service = new ExecutionStateService();
        var before = DateTimeOffset.UtcNow;

        service.RecordSuccess(7);

        var state = service.GetState();
        Assert.NotNull(state.LastRunAt);
        Assert.True(state.LastRunAt >= before);
        Assert.True(state.LastRunSuccess);
        Assert.Contains("7", state.LastRunResult);
    }

    [Fact]
    public void RecordFailure_SetsLastRunSuccess_False()
    {
        var service = new ExecutionStateService();

        service.RecordFailure("erreur réseau");

        var state = service.GetState();
        Assert.NotNull(state.LastRunAt);
        Assert.False(state.LastRunSuccess);
        Assert.Equal("erreur réseau", state.LastRunResult);
    }

    [Fact]
    public void SetNextRun_UpdatesNextRunAt()
    {
        var service = new ExecutionStateService();
        var next = DateTimeOffset.UtcNow.AddDays(1);

        service.SetNextRun(next);

        Assert.Equal(next, service.GetState().NextRunAt);
    }

    [Fact]
    public void RecordSuccess_AfterFailure_OverwritesState()
    {
        var service = new ExecutionStateService();
        service.RecordFailure("erreur");
        service.RecordSuccess(3);

        var state = service.GetState();
        Assert.True(state.LastRunSuccess);
        Assert.Contains("3", state.LastRunResult);
    }
}
