using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Tests.Services;

public class ExecutionStateServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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

    // ─── SchedulingEnabled tests ──────────────────────────────────────────────

    [Fact]
    public void GetSchedulingEnabled_DefaultsToTrue()
    {
        var service = new ExecutionStateService();
        Assert.True(service.GetSchedulingEnabled());
    }

    [Fact]
    public void SetSchedulingEnabled_False_ReturnsDisabled()
    {
        var service = new ExecutionStateService();
        service.SetSchedulingEnabled(false);
        Assert.False(service.GetSchedulingEnabled());
    }

    [Fact]
    public void SetSchedulingEnabled_TrueThenFalse_TogglesBothWays()
    {
        var service = new ExecutionStateService();
        service.SetSchedulingEnabled(false);
        Assert.False(service.GetSchedulingEnabled());
        service.SetSchedulingEnabled(true);
        Assert.True(service.GetSchedulingEnabled());
    }

    [Fact]
    public void SetSchedulingEnabled_WithDataPath_PersistsToFile()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new ExecutionStateService(_tempDir);
        service.SetSchedulingEnabled(false);

        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        Assert.True(File.Exists(stateFile));
        var content = File.ReadAllText(stateFile);
        Assert.Contains("false", content);
    }

    [Fact]
    public void Constructor_WithDataPath_LoadsPersistedState()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(stateFile, "{\"schedulingEnabled\":false}");

        var service = new ExecutionStateService(_tempDir);
        Assert.False(service.GetSchedulingEnabled());
    }

    [Fact]
    public void Constructor_WithMissingFile_DefaultsToTrue()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new ExecutionStateService(_tempDir);
        Assert.True(service.GetSchedulingEnabled());
    }

    [Fact]
    public void Constructor_WithCorruptFile_DefaultsToTrue()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(stateFile, "not valid json {{{{");

        var service = new ExecutionStateService(_tempDir);
        Assert.True(service.GetSchedulingEnabled());
    }

    [Fact]
    public void SetSchedulingEnabled_WithoutDataPath_DoesNotThrow()
    {
        var service = new ExecutionStateService(); // pas de dataPath
        var ex = Record.Exception(() => service.SetSchedulingEnabled(false));
        Assert.Null(ex);
        Assert.False(service.GetSchedulingEnabled());
    }

    // ─── ScheduleTime tests ───────────────────────────────────────────────────

    [Fact]
    public void GetScheduleTime_DefaultsTo0800()
    {
        var service = new ExecutionStateService();
        Assert.Equal("08:00", service.GetScheduleTime());
    }

    [Fact]
    public void Constructor_WithCustomDefaultScheduleTime_UsesIt()
    {
        var service = new ExecutionStateService(defaultScheduleTime: "10:30");
        Assert.Equal("10:30", service.GetScheduleTime());
    }

    [Fact]
    public void Constructor_WithDataPath_PersistedScheduleTimeOverridesDefault()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(stateFile, "{\"schedulingEnabled\":true,\"scheduleTime\":\"15:00\"}");

        var service = new ExecutionStateService(_tempDir, defaultScheduleTime: "10:30");
        Assert.Equal("15:00", service.GetScheduleTime()); // persisté prime sur le défaut
    }

    [Fact]
    public void SetScheduleTime_UpdatesTime()
    {
        var service = new ExecutionStateService();
        service.SetScheduleTime("10:30");
        Assert.Equal("10:30", service.GetScheduleTime());
    }

    [Fact]
    public void SetScheduleTime_WithDataPath_PersistsToFile()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new ExecutionStateService(_tempDir);
        service.SetScheduleTime("14:00");

        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        Assert.True(File.Exists(stateFile));
        var content = File.ReadAllText(stateFile);
        Assert.Contains("14:00", content);
    }

    [Fact]
    public void Constructor_WithDataPath_LoadsPersistedScheduleTime()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(stateFile, "{\"schedulingEnabled\":true,\"scheduleTime\":\"15:45\"}");

        var service = new ExecutionStateService(_tempDir);
        Assert.Equal("15:45", service.GetScheduleTime());
    }

    [Fact]
    public void Constructor_WithOldFormatFile_FallsBackToDefault()
    {
        // Fichier ancien format sans scheduleTime (compatibilité ascendante)
        Directory.CreateDirectory(_tempDir);
        var stateFile = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(stateFile, "{\"schedulingEnabled\":false}");

        var service = new ExecutionStateService(_tempDir);
        Assert.Equal("08:00", service.GetScheduleTime());
        Assert.False(service.GetSchedulingEnabled()); // préserve l'ancien état
    }

    [Fact]
    public void SetScheduleTime_PreservesSchedulingEnabled()
    {
        var service = new ExecutionStateService();
        service.SetSchedulingEnabled(false);
        service.SetScheduleTime("09:00");

        Assert.False(service.GetSchedulingEnabled());
        Assert.Equal("09:00", service.GetScheduleTime());
    }

    [Fact]
    public void SetSchedulingEnabled_PreservesScheduleTime()
    {
        var service = new ExecutionStateService();
        service.SetScheduleTime("11:30");
        service.SetSchedulingEnabled(false);

        Assert.Equal("11:30", service.GetScheduleTime());
        Assert.False(service.GetSchedulingEnabled());
    }
}
