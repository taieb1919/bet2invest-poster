using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Tests.Helpers;

/// <summary>
/// Implémentation de test partagée de IExecutionStateService.
/// Configurable via le constructeur et expose des propriétés de tracking pour les assertions.
/// </summary>
public class FakeExecutionStateService : IExecutionStateService
{
    private string[] _scheduleTimes;
    private bool _schedulingEnabled;
    private readonly ExecutionState? _fixedState;

    // Tracking SetNextRun
    public DateTimeOffset? NextRunSet { get; private set; }
    public int SetNextRunCallCount { get; private set; }

    // Tracking SetSchedulingEnabled (setter public pour permettre le reset dans les tests)
    public bool SetSchedulingEnabledCalled { get; set; }
    public bool? LastSetSchedulingEnabledValue { get; private set; }

    // Tracking RecordSuccess / RecordFailure
    public int? LastSuccessCount { get; private set; }
    public string? LastFailureReason { get; private set; }
    public bool RecordSuccessCalled { get; private set; }
    public bool RecordFailureCalled { get; private set; }

    public FakeExecutionStateService(
        string[]? scheduleTimes = null,
        bool schedulingEnabled = true,
        ExecutionState? fixedState = null)
    {
        _scheduleTimes = scheduleTimes ?? ["08:00"];
        _schedulingEnabled = schedulingEnabled;
        _fixedState = fixedState;
    }

    public ExecutionState GetState() =>
        _fixedState ?? new(null, null, null, NextRunSet, null, _schedulingEnabled, _scheduleTimes);

    public void RecordSuccess(int publishedCount)
    {
        RecordSuccessCalled = true;
        LastSuccessCount = publishedCount;
    }

    public void RecordFailure(string reason)
    {
        RecordFailureCalled = true;
        LastFailureReason = reason;
    }

    public void SetNextRun(DateTimeOffset nextRunAt)
    {
        NextRunSet = nextRunAt;
        SetNextRunCallCount++;
    }

    public void SetApiConnectionStatus(bool connected) { }

    public bool GetSchedulingEnabled() => _schedulingEnabled;

    public void SetSchedulingEnabled(bool enabled)
    {
        _schedulingEnabled = enabled;
        SetSchedulingEnabledCalled = true;
        LastSetSchedulingEnabledValue = enabled;
    }

    public string[] GetScheduleTimes() => _scheduleTimes;
    public void SetScheduleTimes(string[] times) => _scheduleTimes = times;
    public string GetScheduleTime() => _scheduleTimes.Length > 0 ? _scheduleTimes[0] : "08:00";
    public void SetScheduleTime(string time) => _scheduleTimes = [time];
}
