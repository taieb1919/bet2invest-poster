using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Services;

public class ExecutionStateService : IExecutionStateService
{
    private readonly object _lock = new();
    private ExecutionState _state;
    private readonly string? _schedulingStateFile;
    private readonly string[] _defaultScheduleTimes;
    private readonly ILogger<ExecutionStateService> _logger;

    public ExecutionStateService(
        string? dataPath = null,
        string defaultScheduleTime = "08:00",
        ILogger<ExecutionStateService>? logger = null,
        string[]? defaultScheduleTimes = null)
    {
        _defaultScheduleTimes = defaultScheduleTimes is { Length: > 0 }
            ? defaultScheduleTimes
            : [defaultScheduleTime];
        _logger = logger ?? NullLogger<ExecutionStateService>.Instance;

        _schedulingStateFile = dataPath is not null
            ? Path.Combine(dataPath, "scheduling-state.json")
            : null;

        var (schedulingEnabled, scheduleTimes) = LoadSchedulingState();
        _state = new ExecutionState(null, null, null, null, null, schedulingEnabled, scheduleTimes);
    }

    private (bool enabled, string[] scheduleTimes) LoadSchedulingState()
    {
        if (_schedulingStateFile is null || !File.Exists(_schedulingStateFile))
            return (true, _defaultScheduleTimes);

        try
        {
            var json = File.ReadAllText(_schedulingStateFile);
            using var doc = JsonDocument.Parse(json);

            var enabled = true;
            if (doc.RootElement.TryGetProperty("schedulingEnabled", out var enabledProp))
                enabled = enabledProp.GetBoolean();

            // Nouveau format : array "scheduleTimes"
            if (doc.RootElement.TryGetProperty("scheduleTimes", out var timesProp)
                && timesProp.ValueKind == JsonValueKind.Array)
            {
                var times = timesProp.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => s is not null)
                    .Select(s => s!)
                    .ToArray();
                if (times.Length > 0)
                    return (enabled, times);
            }

            // Fallback rétrocompatibilité : string "scheduleTime"
            if (doc.RootElement.TryGetProperty("scheduleTime", out var timeProp))
            {
                var t = timeProp.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                    return (enabled, [t]);
            }

            return (enabled, _defaultScheduleTimes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de lire scheduling-state.json — valeurs par défaut utilisées");
        }

        return (true, _defaultScheduleTimes);
    }

    private void PersistSchedulingState(bool enabled, string[] scheduleTimes)
    {
        if (_schedulingStateFile is null) return;

        try
        {
            var dir = Path.GetDirectoryName(_schedulingStateFile);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var tmpFile = _schedulingStateFile + ".tmp";
            var json = JsonSerializer.Serialize(new { schedulingEnabled = enabled, scheduleTimes });
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
        string[] times;
        lock (_lock)
        {
            _state = _state with { SchedulingEnabled = enabled };
            times = _state.ScheduleTimes;
        }
        PersistSchedulingState(enabled, times);
    }

    public string[] GetScheduleTimes()
    {
        lock (_lock) return _state.ScheduleTimes;
    }

    public void SetScheduleTimes(string[] times)
    {
        bool enabled;
        lock (_lock)
        {
            _state = _state with { ScheduleTimes = times };
            enabled = _state.SchedulingEnabled;
        }
        PersistSchedulingState(enabled, times);
    }

    // Rétrocompatibilité
    public string GetScheduleTime()
    {
        lock (_lock) return _state.ScheduleTime;
    }

    public void SetScheduleTime(string time)
    {
        SetScheduleTimes([time]);
    }
}
