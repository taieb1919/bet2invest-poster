using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Tests.Helpers;

public class FakeNotificationService : INotificationService
{
    public List<string> SentMessages { get; } = [];

    public int SuccessCallCount { get; private set; }
    public CycleResult? LastSuccessResult { get; private set; }

    public int FailureCallCount { get; private set; }
    public string? LastFailureReason { get; private set; }

    public int FinalFailureCount { get; private set; }
    public int LastFinalFailureAttempts { get; private set; }
    public string? LastFinalFailureReason { get; private set; }
    public TaskCompletionSource FinalFailureCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int NoFilteredCandidatesCallCount { get; private set; }
    public string? LastFilterDetails { get; private set; }

    public Task NotifySuccessAsync(CycleResult result, CancellationToken ct = default)
    {
        SuccessCallCount++;
        LastSuccessResult = result;
        return Task.CompletedTask;
    }

    public Task NotifyFailureAsync(string reason, CancellationToken ct = default)
    {
        FailureCallCount++;
        LastFailureReason = reason;
        return Task.CompletedTask;
    }

    public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
    {
        FinalFailureCount++;
        LastFinalFailureAttempts = attempts;
        LastFinalFailureReason = reason;
        FinalFailureCalled.TrySetResult();
        return Task.CompletedTask;
    }

    public Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default)
    {
        NoFilteredCandidatesCallCount++;
        LastFilterDetails = filterDetails;
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, CancellationToken ct = default)
    {
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
