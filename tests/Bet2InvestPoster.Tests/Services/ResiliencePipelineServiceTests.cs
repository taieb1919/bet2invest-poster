using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class ResiliencePipelineServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────

    private static ResiliencePipelineService CreateService(int maxRetryCount = 3, int retryDelayMs = 0)
    {
        var options = Options.Create(new PosterOptions
        {
            MaxRetryCount = maxRetryCount,
            RetryDelayMs = retryDelayMs
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
}
