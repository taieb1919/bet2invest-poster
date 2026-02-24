using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class ResiliencePipelineService : IResiliencePipelineService
{
    private readonly ResiliencePipeline _pipeline;

    public ResiliencePipelineService(
        IOptions<PosterOptions> options,
        ILogger<ResiliencePipelineService> logger)
    {
        var opts = options.Value;
        // MaxRetryCount = nombre de tentatives totales → MaxRetryAttempts = tentatives - 1 (retries)
        var maxRetries = Math.Max(0, opts.MaxRetryCount - 1);
        var delay = TimeSpan.FromMilliseconds(opts.RetryDelayMs);

        var builder = new ResiliencePipelineBuilder();

        // Only add retry strategy when there are retries to perform (MaxRetryCount > 1)
        if (maxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = delay,
                BackoffType = DelayBackoffType.Constant,
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
                            delay.TotalSeconds);
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
}
