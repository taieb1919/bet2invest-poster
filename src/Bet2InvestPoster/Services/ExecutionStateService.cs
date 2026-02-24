namespace Bet2InvestPoster.Services;

public class ExecutionStateService : IExecutionStateService
{
    private readonly object _lock = new();
    private ExecutionState _state = new(null, null, null, null, null);

    public ExecutionState GetState()
    {
        lock (_lock) return _state;
    }

    public void RecordSuccess(int publishedCount)
    {
        lock (_lock)
        {
            _state = _state with
            {
                LastRunAt = DateTimeOffset.UtcNow,
                LastRunSuccess = true,
                LastRunResult = $"{publishedCount} pronostic(s) publiés"
            };
        }
    }

    public void RecordFailure(string reason)
    {
        lock (_lock)
        {
            _state = _state with
            {
                LastRunAt = DateTimeOffset.UtcNow,
                LastRunSuccess = false,
                LastRunResult = reason
            };
        }
    }

    public void SetNextRun(DateTimeOffset nextRunAt)
    {
        lock (_lock)
        {
            _state = _state with { NextRunAt = nextRunAt };
        }
    }

    public void SetApiConnectionStatus(bool connected)
    {
        lock (_lock)
        {
            _state = _state with { ApiConnected = connected };
        }
    }
}
