using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Services;

public class ExecutionStateService : IExecutionStateService
{
    private readonly object _lock = new();
    private ExecutionState _state;
    private readonly string? _schedulingStateFile;
    private readonly string _defaultScheduleTime;
    private readonly ILogger<ExecutionStateService> _logger;

    public ExecutionStateService(
        string? dataPath = null,
        string defaultScheduleTime = "08:00",
        ILogger<ExecutionStateService>? logger = null)
    {
        _defaultScheduleTime = defaultScheduleTime;
        _logger = logger ?? NullLogger<ExecutionStateService>.Instance;

        _schedulingStateFile = dataPath is not null
            ? Path.Combine(dataPath, "scheduling-state.json")
            : null;

        var (schedulingEnabled, scheduleTime) = LoadSchedulingState();
        _state = new ExecutionState(null, null, null, null, null, schedulingEnabled, scheduleTime);
    }

    private (bool enabled, string scheduleTime) LoadSchedulingState()
    {
        if (_schedulingStateFile is null || !File.Exists(_schedulingStateFile))
            return (true, _defaultScheduleTime);

        try
        {
            var json = File.ReadAllText(_schedulingStateFile);
            using var doc = JsonDocument.Parse(json);

            var enabled = true;
            if (doc.RootElement.TryGetProperty("schedulingEnabled", out var enabledProp))
                enabled = enabledProp.GetBoolean();

            var scheduleTime = _defaultScheduleTime;
            if (doc.RootElement.TryGetProperty("scheduleTime", out var timeProp))
                scheduleTime = timeProp.GetString() ?? _defaultScheduleTime;

            return (enabled, scheduleTime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de lire scheduling-state.json — valeurs par défaut utilisées");
        }

        return (true, _defaultScheduleTime);
    }

    private void PersistSchedulingState(bool enabled, string scheduleTime)
    {
        if (_schedulingStateFile is null) return;

        try
        {
            var dir = Path.GetDirectoryName(_schedulingStateFile);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var tmpFile = _schedulingStateFile + ".tmp";
            var json = JsonSerializer.Serialize(new { schedulingEnabled = enabled, scheduleTime });
            File.WriteAllText(tmpFile, json);
            File.Move(tmpFile, _schedulingStateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de persister scheduling-state.json");
        }
    }

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

    public bool GetSchedulingEnabled()
    {
        lock (_lock) return _state.SchedulingEnabled;
    }

    public void SetSchedulingEnabled(bool enabled)
    {
        string scheduleTime;
        lock (_lock)
        {
            _state = _state with { SchedulingEnabled = enabled };
            scheduleTime = _state.ScheduleTime;
        }
        PersistSchedulingState(enabled, scheduleTime);
    }

    public string GetScheduleTime()
    {
        lock (_lock) return _state.ScheduleTime;
    }

    public void SetScheduleTime(string time)
    {
        bool enabled;
        lock (_lock)
        {
            _state = _state with { ScheduleTime = time };
            enabled = _state.SchedulingEnabled;
        }
        PersistSchedulingState(enabled, time);
    }
}
