using Bet2InvestPoster.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bet2InvestPoster.Services;

public class Bet2InvestHealthCheck : IHealthCheck
{
    private readonly IExecutionStateService _stateService;
    private readonly IResiliencePipelineService _resilienceService;

    public Bet2InvestHealthCheck(
        IExecutionStateService stateService,
        IResiliencePipelineService resilienceService)
    {
        _stateService = stateService;
        _resilienceService = resilienceService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _stateService.GetState();
        var circuitState = _resilienceService.GetCircuitBreakerState();

        var data = new Dictionary<string, object>
        {
            ["service"] = "running",
            ["lastExecution"] = state.LastRunAt?.ToString("o") ?? "never",
            ["lastResult"] = state.LastRunResult ?? "none",
            ["circuitBreaker"] = circuitState.ToString(),
            ["apiConnection"] = state.ApiConnected == true ? "connected" : "disconnected"
        };

        if (circuitState == CircuitBreakerState.Open)
            return Task.FromResult(HealthCheckResult.Unhealthy("Circuit breaker ouvert", data: data));
        if (circuitState == CircuitBreakerState.HalfOpen)
            return Task.FromResult(HealthCheckResult.Degraded("Circuit breaker en rétablissement", data: data));

        return Task.FromResult(HealthCheckResult.Healthy("Service opérationnel", data: data));
    }
}
