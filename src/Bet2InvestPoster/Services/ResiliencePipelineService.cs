using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class ResiliencePipelineService : IResiliencePipelineService
{
    private readonly ResiliencePipeline _pipeline;
    private readonly int _circuitBreakerDurationSeconds;
    private readonly object _circuitLock = new();
    private CircuitBreakerState _circuitState = CircuitBreakerState.Closed;
    private DateTimeOffset? _circuitOpenedAt;

    public ResiliencePipelineService(
        IOptions<PosterOptions> options,
        ILogger<ResiliencePipelineService> logger)
    {
        var opts = options.Value;
        // MaxRetryCount = nombre de tentatives totales → MaxRetryAttempts = tentatives - 1 (retries)
        var maxRetries = Math.Max(0, opts.MaxRetryCount - 1);
        var delay = TimeSpan.FromMilliseconds(opts.RetryDelayMs);
        _circuitBreakerDurationSeconds = opts.CircuitBreakerDurationSeconds;

        var builder = new ResiliencePipelineBuilder();

        // Circuit breaker AVANT le retry (pour que le retry ne contourne pas un circuit ouvert)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = opts.CircuitBreakerFailureThreshold,
            SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds * 2),
            BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds),
            ShouldHandle = new PredicateBuilder()
                .Handle<Exception>(ex => ex is not OperationCanceledException),
            OnOpened = args =>
            {
                lock (_circuitLock)
                {
                    _circuitState = CircuitBreakerState.Open;
                    _circuitOpenedAt = DateTimeOffset.UtcNow;
                }
                using (LogContext.PushProperty("Step", "Cycle"))
                {
                    logger.LogError(
                        "Circuit breaker OUVERT — {FailureCount} échec(s) consécutif(s). Pause de {DurationSeconds}s.",
                        opts.CircuitBreakerFailureThreshold,
                        opts.CircuitBreakerDurationSeconds);
                }
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                lock (_circuitLock)
                {
                    _circuitState = CircuitBreakerState.Closed;
                    _circuitOpenedAt = null;
                }
                using (LogContext.PushProperty("Step", "Cycle"))
                {
                    logger.LogInformation("Circuit breaker FERMÉ — service rétabli.");
                }
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                lock (_circuitLock) { _circuitState = CircuitBreakerState.HalfOpen; }
                using (LogContext.PushProperty("Step", "Cycle"))
                {
                    logger.LogInformation("Circuit breaker HALF-OPEN — tentative de rétablissement.");
                }
                return ValueTask.CompletedTask;
            }
        });

        // Retry APRÈS le circuit breaker
        if (maxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = delay,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    using (LogContext.PushProperty("Step", "Cycle"))
                    {
                        logger.LogWarning(
                            "Tentative {Attempt}/{MaxAttempts} échouée — {ExceptionType}: retente dans {DelaySeconds}s",
                            args.AttemptNumber + 1,
                            opts.MaxRetryCount,
                            args.Outcome.Exception?.GetType().Name ?? "Unknown",
                            args.RetryDelay.TotalSeconds);
                    }
                    return ValueTask.CompletedTask;
                }
            });
        }

        _pipeline = builder.Build();
    }

    public async Task ExecuteCycleWithRetryAsync(
        Func<CancellationToken, Task> cycleAction,
        CancellationToken ct = default)
    {
        await _pipeline.ExecuteAsync(
            async token => await cycleAction(token),
            ct);
    }

    public CircuitBreakerState GetCircuitBreakerState() => _circuitState;

    public TimeSpan? GetCircuitBreakerRemainingDuration()
    {
        lock (_circuitLock)
        {
            if (_circuitState != CircuitBreakerState.Open || _circuitOpenedAt is null)
                return null;

            var elapsed = DateTimeOffset.UtcNow - _circuitOpenedAt.Value;
            var total = TimeSpan.FromSeconds(_circuitBreakerDurationSeconds);
            var remaining = total - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
