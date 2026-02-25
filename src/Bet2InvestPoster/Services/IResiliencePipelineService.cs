using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IResiliencePipelineService
{
    Task ExecuteCycleWithRetryAsync(
        Func<CancellationToken, Task> cycleAction,
        CancellationToken ct = default);

    CircuitBreakerState GetCircuitBreakerState();
    TimeSpan? GetCircuitBreakerRemainingDuration();
}
