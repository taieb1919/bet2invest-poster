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
    // Multi-horaires
    string[] GetScheduleTimes();
    void SetScheduleTimes(string[] times);
    // Rétrocompatibilité (délèguent vers ScheduleTimes)
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
    string[]? ScheduleTimes = null
)
{
    private static readonly string[] DefaultTimes = ["08:00", "13:00", "19:00"];
    public string[] ScheduleTimes { get; init; } = ScheduleTimes ?? DefaultTimes;
    // Rétrocompatibilité
    public string ScheduleTime => this.ScheduleTimes.Length > 0 ? this.ScheduleTimes[0] : "08:00";
}
