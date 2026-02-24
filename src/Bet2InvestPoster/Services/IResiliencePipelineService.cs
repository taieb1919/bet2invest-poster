namespace Bet2InvestPoster.Services;

public interface IResiliencePipelineService
{
    Task ExecuteCycleWithRetryAsync(
        Func<CancellationToken, Task> cycleAction,
        CancellationToken ct = default);
}
