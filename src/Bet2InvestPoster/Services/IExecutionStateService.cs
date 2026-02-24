namespace Bet2InvestPoster.Services;

public interface IExecutionStateService
{
    ExecutionState GetState();
    void RecordSuccess(int publishedCount);
    void RecordFailure(string reason);
    void SetNextRun(DateTimeOffset nextRunAt);
}

public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt
);
