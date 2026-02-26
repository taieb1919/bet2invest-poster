using System.Collections.Concurrent;

namespace Bet2InvestPoster.Models;

public class PreviewSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public long ChatId { get; init; }
    public int MessageId { get; set; }
    public IReadOnlyList<PendingBet> Bets { get; init; } = [];
    public bool[] Selected { get; init; } = [];
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public CycleResult PartialCycleResult { get; init; } = new();

    /// <summary>Preview expires after this duration.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Timeout;

    public void Toggle(int index)
    {
        if (index >= 0 && index < Selected.Length)
            Selected[index] = !Selected[index];
    }

    public List<PendingBet> GetSelectedBets()
    {
        var result = new List<PendingBet>();
        for (var i = 0; i < Bets.Count; i++)
        {
            if (Selected[i])
                result.Add(Bets[i]);
        }
        return result;
    }
}
