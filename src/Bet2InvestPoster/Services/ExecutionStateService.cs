using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Services;

public class ExecutionStateService : IExecutionStateService
{
    private readonly object _lock = new();
    private ExecutionState _state;
    private readonly string? _schedulingStateFile;
    private readonly string[] _defaultScheduleTimes;
    private readonly decimal? _defaultMinOdds;
    private readonly decimal? _defaultMaxOdds;
    private readonly string _defaultSelectionMode;
    private readonly ILogger<ExecutionStateService> _logger;

    public ExecutionStateService(
        string? dataPath = null,
        string defaultScheduleTime = "08:00",
        ILogger<ExecutionStateService>? logger = null,
        string[]? defaultScheduleTimes = null,
        decimal? defaultMinOdds = null,
        decimal? defaultMaxOdds = null,
        string defaultSelectionMode = "random")
    {
        _defaultScheduleTimes = defaultScheduleTimes is { Length: > 0 }
            ? defaultScheduleTimes
            : [defaultScheduleTime];
        _defaultMinOdds = defaultMinOdds;
        _defaultMaxOdds = defaultMaxOdds;
        _defaultSelectionMode = defaultSelectionMode;
        _logger = logger ?? NullLogger<ExecutionStateService>.Instance;

        _schedulingStateFile = dataPath is not null
            ? Path.Combine(dataPath, "scheduling-state.json")
            : null;

        var persisted = LoadSchedulingState();
        _state = new ExecutionState(null, null, null, null, null,
            persisted.Enabled, persisted.ScheduleTimes,
            persisted.MinOdds, persisted.MaxOdds, persisted.SelectionMode);
    }

    private record PersistedState(bool Enabled, string[] ScheduleTimes, decimal? MinOdds, decimal? MaxOdds, string? SelectionMode);

    private PersistedState LoadSchedulingState()
    {
        if (_schedulingStateFile is null || !File.Exists(_schedulingStateFile))
            return new(true, _defaultScheduleTimes, _defaultMinOdds, _defaultMaxOdds, _defaultSelectionMode);

        try
        {
            var json = File.ReadAllText(_schedulingStateFile);
            using var doc = JsonDocument.Parse(json);

            var enabled = true;
            if (doc.RootElement.TryGetProperty("schedulingEnabled", out var enabledProp))
                enabled = enabledProp.GetBoolean();

            // Nouveau format : array "scheduleTimes"
            string[] scheduleTimes = _defaultScheduleTimes;
            if (doc.RootElement.TryGetProperty("scheduleTimes", out var timesProp)
                && timesProp.ValueKind == JsonValueKind.Array)
            {
                var times = timesProp.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => s is not null)
                    .Select(s => s!)
                    .ToArray();
                if (times.Length > 0)
                    scheduleTimes = times;
            }
            // Fallback rétrocompatibilité : string "scheduleTime"
            else if (doc.RootElement.TryGetProperty("scheduleTime", out var timeProp))
            {
                var t = timeProp.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                    scheduleTimes = [t];
            }

            // Odds filter
            decimal? minOdds = _defaultMinOdds;
            decimal? maxOdds = _defaultMaxOdds;

            if (doc.RootElement.TryGetProperty("minOdds", out var minProp) && minProp.ValueKind == JsonValueKind.Number)
                minOdds = minProp.GetDecimal();
            if (doc.RootElement.TryGetProperty("maxOdds", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number)
                maxOdds = maxProp.GetDecimal();

            // Selection mode (rétrocompat: allMode=true → "all")
            string? selectionMode = _defaultSelectionMode;
            if (doc.RootElement.TryGetProperty("selectionMode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String)
                selectionMode = modeProp.GetString();
            else if (doc.RootElement.TryGetProperty("allMode", out var allProp) && allProp.GetBoolean())
                selectionMode = "all";

            return new(enabled, scheduleTimes, minOdds, maxOdds, selectionMode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de lire scheduling-state.json — valeurs par défaut utilisées");
        }

        return new(true, _defaultScheduleTimes, _defaultMinOdds, _defaultMaxOdds, _defaultSelectionMode);
    }

    private void PersistState()
    {
        if (_schedulingStateFile is null) return;

        try
        {
            var dir = Path.GetDirectoryName(_schedulingStateFile);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            ExecutionState snapshot;
            lock (_lock) { snapshot = _state; }

            var tmpFile = _schedulingStateFile + ".tmp";
            var json = JsonSerializer.Serialize(new
            {
                schedulingEnabled = snapshot.SchedulingEnabled,
                scheduleTimes = snapshot.ScheduleTimes,
                minOdds = snapshot.MinOdds,
                maxOdds = snapshot.MaxOdds,
                selectionMode = snapshot.SelectionMode
            });
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
        lock (_lock)
        {
            _state = _state with { SchedulingEnabled = enabled };
        }
        PersistState();
    }

    public string[] GetScheduleTimes()
    {
        lock (_lock) return _state.ScheduleTimes;
    }

    public void SetScheduleTimes(string[] times)
    {
        lock (_lock)
        {
            _state = _state with { ScheduleTimes = times };
        }
        PersistState();
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

    // Filtrage par cotes et mode de sélection
    public decimal? GetMinOdds()
    {
        lock (_lock) return _state.MinOdds;
    }

    public decimal? GetMaxOdds()
    {
        lock (_lock) return _state.MaxOdds;
    }

    public string GetSelectionMode()
    {
        lock (_lock) return _state.SelectionMode ?? _defaultSelectionMode;
    }

    public void SetOddsFilter(decimal? minOdds, decimal? maxOdds)
    {
        lock (_lock)
        {
            _state = _state with { MinOdds = minOdds, MaxOdds = maxOdds };
        }
        PersistState();
    }

    public void SetSelectionMode(string mode)
    {
        lock (_lock)
        {
            _state = _state with { SelectionMode = mode };
        }
        PersistState();
    }
}
