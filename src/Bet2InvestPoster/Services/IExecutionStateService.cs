namespace Bet2InvestPoster.Services;

public interface IExecutionStateService
{
    ExecutionState GetState();
    void RecordSuccess(int publishedCount);
    void RecordFailure(string reason);
    void SetNextRun(DateTimeOffset nextRunAt);
    void SetApiConnectionStatus(bool connected);
    bool GetSchedulingEnabled();
    void SetSchedulingEnabled(bool enabled);
    string GetScheduleTime();
    void SetScheduleTime(string time);
}

public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt,
    bool? ApiConnected,
    bool SchedulingEnabled = true,
    string ScheduleTime = "08:00"
);
