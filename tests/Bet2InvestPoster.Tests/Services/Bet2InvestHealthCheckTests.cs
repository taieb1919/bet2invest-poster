using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests.Services;

public class Bet2InvestHealthCheckTests
{
    // ─── Fakes ──────────────────────────────────────────────────────────

    private class FakeExecutionStateService : IExecutionStateService
    {
        private ExecutionState _state;

        public FakeExecutionStateService(ExecutionState? state = null)
        {
            _state = state ?? new ExecutionState(null, null, null, null, null);
        }

        public ExecutionState GetState() => _state;
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => true;
        public void SetSchedulingEnabled(bool enabled) { }
        public string GetScheduleTime() => "08:00";
        public void SetScheduleTime(string time) { }
    }

    private class FakeResiliencePipelineService : IResiliencePipelineService
    {
        private readonly CircuitBreakerState _state;

        public FakeResiliencePipelineService(CircuitBreakerState state = CircuitBreakerState.Closed)
        {
            _state = state;
        }

        public Task ExecuteCycleWithRetryAsync(Func<CancellationToken, Task> cycleAction, CancellationToken ct = default)
            => cycleAction(ct);

        public CircuitBreakerState GetCircuitBreakerState() => _state;
        public TimeSpan? GetCircuitBreakerRemainingDuration()
            => _state == CircuitBreakerState.Open ? TimeSpan.FromMinutes(5) : null;
    }

    // ─── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_WhenCircuitClosed_ReturnsHealthy()
    {
        var stateService = new FakeExecutionStateService();
        var resilienceService = new FakeResiliencePipelineService(CircuitBreakerState.Closed);
        var healthCheck = new Bet2InvestHealthCheck(stateService, resilienceService);

        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCircuitOpen_ReturnsUnhealthy()
    {
        var stateService = new FakeExecutionStateService();
        var resilienceService = new FakeResiliencePipelineService(CircuitBreakerState.Open);
        var healthCheck = new Bet2InvestHealthCheck(stateService, resilienceService);

        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsCorrectData()
    {
        var lastRun = new DateTimeOffset(2026, 2, 25, 10, 0, 0, TimeSpan.Zero);
        var state = new ExecutionState(lastRun, true, "5 paris publiés", null, true);
        var stateService = new FakeExecutionStateService(state);
        var resilienceService = new FakeResiliencePipelineService(CircuitBreakerState.Closed);
        var healthCheck = new Bet2InvestHealthCheck(stateService, resilienceService);

        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("circuitBreaker"));
        Assert.Equal("Closed", result.Data["circuitBreaker"].ToString());
        Assert.True(result.Data.ContainsKey("apiConnection"));
        Assert.Equal("connected", result.Data["apiConnection"].ToString());
    }

    [Fact]
    public async Task CheckHealthAsync_WithHalfOpenCircuit_ReturnsDegraded()
    {
        // HalfOpen = service en cours de rétablissement → Degraded
        var stateService = new FakeExecutionStateService();
        var resilienceService = new FakeResiliencePipelineService(CircuitBreakerState.HalfOpen);
        var healthCheck = new Bet2InvestHealthCheck(stateService, resilienceService);

        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNoLastExecution_ShowsNeverInData()
    {
        var stateService = new FakeExecutionStateService(); // LastRunAt = null
        var resilienceService = new FakeResiliencePipelineService();
        var healthCheck = new Bet2InvestHealthCheck(stateService, resilienceService);

        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        Assert.Equal("never", result.Data["lastExecution"].ToString());
    }
}
