using System.Collections.Concurrent;
using Telegram.Bot;

namespace Bet2InvestPoster.Services;

public class ConversationStateService : IConversationStateService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private sealed record PendingConversation(
        Func<ITelegramBotClient, string, CancellationToken, Task> Callback,
        CancellationTokenSource TimeoutCts);

    private readonly ConcurrentDictionary<long, PendingConversation> _states = new();

    public void Register(long chatId, Func<ITelegramBotClient, string, CancellationToken, Task> callback, TimeSpan? timeout = null)
    {
        // Annule et supprime tout état existant pour ce chat
        Clear(chatId);

        var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        // Nettoyage automatique à l'expiration du timeout
        cts.Token.Register(() =>
        {
            if (_states.TryRemove(chatId, out var removed))
                removed.TimeoutCts.Dispose();
        });

        _states[chatId] = new PendingConversation(callback, cts);
    }

    public bool TryGet(long chatId, out Func<ITelegramBotClient, string, CancellationToken, Task>? callback)
    {
        if (_states.TryGetValue(chatId, out var state))
        {
            callback = state.Callback;
            return true;
        }

        callback = null;
        return false;
    }

    public void Clear(long chatId)
    {
        if (_states.TryRemove(chatId, out var existing))
        {
            existing.TimeoutCts.Cancel();
            existing.TimeoutCts.Dispose();
        }
    }
}
