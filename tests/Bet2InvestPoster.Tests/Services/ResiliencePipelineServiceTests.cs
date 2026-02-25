using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace Bet2InvestPoster.Tests.Services;

public class ResiliencePipelineServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────

    private static ResiliencePipelineService CreateService(
        int maxRetryCount = 3,
        int retryDelayMs = 0,
        int circuitBreakerFailureThreshold = 100,  // seuil élevé par défaut pour ne pas interférer avec tests existants
        int circuitBreakerDurationSeconds = 300)
    {
        var options = Options.Create(new PosterOptions
        {
            MaxRetryCount = maxRetryCount,
            RetryDelayMs = retryDelayMs,
            CircuitBreakerFailureThreshold = circuitBreakerFailureThreshold,
            CircuitBreakerDurationSeconds = circuitBreakerDurationSeconds
        });
        return new ResiliencePipelineService(options, NullLogger<ResiliencePipelineService>.Instance);
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SuccessOnFirstAttempt_NeverRetries()
    {
        var svc = CreateService(maxRetryCount: 3);
        var callCount = 0;

        await svc.ExecuteCycleWithRetryAsync(_ =>
        {
            callCount++;
            return Task.CompletedTask;
        });

        // MaxRetryCount=3 → 3 tentatives totales → 2 retries. Only 1 call needed.
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Execute_FailsThenSucceeds_RetriesAndSucceeds()
    {
        var svc = CreateService(maxRetryCount: 3);
        var callCount = 0;

        await svc.ExecuteCycleWithRetryAsync(_ =>
        {
            callCount++;
            if (callCount < 2)
                throw new InvalidOperationException("Simulated failure");
            return Task.CompletedTask;
        });

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Execute_AllAttemptsExhausted_ThrowsAfterMaxAttempts()
    {
        // MaxRetryCount=3 → 3 tentatives totales → 2 retries → 3 calls then throw
        var svc = CreateService(maxRetryCount: 3);
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await svc.ExecuteCycleWithRetryAsync(_ =>
            {
                callCount++;
                throw new InvalidOperationException("Always fails");
            });
        });

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task Execute_OperationCanceledException_NotRetried()
    {
        // Use a fresh non-cancelled token so the action is actually called.
        // The action throws OperationCanceledException — Polly must NOT retry it.
        var svc = CreateService(maxRetryCount: 3);
        var callCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await svc.ExecuteCycleWithRetryAsync(_ =>
            {
                callCount++;
                throw new OperationCanceledException("Cancelled inside action");
            }, CancellationToken.None);
        });

        // OperationCanceledException must NOT be retried — exactly 1 call
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Execute_MaxRetryCount1_NoRetry()
    {
        // MaxRetryCount=1 → 1 tentative totale → 0 retries
        var svc = CreateService(maxRetryCount: 1);
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await svc.ExecuteCycleWithRetryAsync(_ =>
            {
                callCount++;
                throw new InvalidOperationException("Fails");
            });
        });

        Assert.Equal(1, callCount);
    }

    // ─── Tests circuit breaker ──────────────────────────────────────────

    [Fact]
    public void GetCircuitBreakerState_InitialState_IsClosed()
    {
        var svc = CreateService();
        Assert.Equal(CircuitBreakerState.Closed, svc.GetCircuitBreakerState());
    }

    [Fact]
    public void GetCircuitBreakerRemainingDuration_WhenClosed_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.GetCircuitBreakerRemainingDuration());
    }

    [Fact]
    public async Task GetCircuitBreakerState_AfterEnoughFailures_BecomesOpen()
    {
        // Seuil = 2 : après 2 exécutions échouées, le circuit s'ouvre
        var svc = CreateService(maxRetryCount: 1, circuitBreakerFailureThreshold: 2);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await svc.ExecuteCycleWithRetryAsync(_ =>
                    throw new InvalidOperationException("fail"));
            });
        }

        Assert.Equal(CircuitBreakerState.Open, svc.GetCircuitBreakerState());
    }

    [Fact]
    public async Task Execute_WhenCircuitOpen_ThrowsBrokenCircuitException()
    {
        // Ouvrir le circuit avec 2 échecs
        var svc = CreateService(maxRetryCount: 1, circuitBreakerFailureThreshold: 2);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await svc.ExecuteCycleWithRetryAsync(_ =>
                    throw new InvalidOperationException("fail"));
            });
        }

        // Circuit est ouvert — la prochaine exécution doit lever BrokenCircuitException
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(async () =>
        {
            await svc.ExecuteCycleWithRetryAsync(_ => Task.CompletedTask);
        });
    }

    [Fact]
    public async Task GetCircuitBreakerRemainingDuration_WhenOpen_ReturnsPositiveDuration()
    {
        var svc = CreateService(maxRetryCount: 1, circuitBreakerFailureThreshold: 2, circuitBreakerDurationSeconds: 300);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await svc.ExecuteCycleWithRetryAsync(_ =>
                    throw new InvalidOperationException("fail"));
            });
        }

        var remaining = svc.GetCircuitBreakerRemainingDuration();
        Assert.NotNull(remaining);
        Assert.True(remaining.Value > TimeSpan.Zero);
        Assert.True(remaining.Value <= TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task Execute_OperationCanceledException_NotCountedByCircuitBreaker()
    {
        // Seuil = 2 : si OperationCanceledException était comptabilisée, après 2 appels le circuit s'ouvrirait.
        // Elle ne doit PAS être comptabilisée → le circuit reste Closed.
        var svc = CreateService(maxRetryCount: 1, circuitBreakerFailureThreshold: 2);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await svc.ExecuteCycleWithRetryAsync(_ =>
                    throw new OperationCanceledException("Cancelled"), CancellationToken.None);
            });
        }

        // OperationCanceledException ne doit pas ouvrir le circuit breaker
        Assert.Equal(CircuitBreakerState.Closed, svc.GetCircuitBreakerState());
    }

    // ─── Tests PosterOptions valeurs par défaut ─────────────────────────

    [Fact]
    public void PosterOptions_DefaultCircuitBreakerValues_AreCorrect()
    {
        var opts = new PosterOptions();
        Assert.Equal(3, opts.CircuitBreakerFailureThreshold);
        Assert.Equal(300, opts.CircuitBreakerDurationSeconds);
        Assert.Equal(8080, opts.HealthCheckPort);
    }
}
