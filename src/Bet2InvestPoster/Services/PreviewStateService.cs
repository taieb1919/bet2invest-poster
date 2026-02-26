using System.Collections.Concurrent;
using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public class PreviewStateService
{
    private readonly ConcurrentDictionary<long, PreviewSession> _sessions = new();

    public void Set(long chatId, PreviewSession session) => _sessions[chatId] = session;

    public PreviewSession? Get(long chatId)
    {
        if (_sessions.TryGetValue(chatId, out var session))
        {
            if (session.IsExpired)
            {
                _sessions.TryRemove(chatId, out _);
                return null;
            }
            return session;
        }
        return null;
    }

    public PreviewSession? GetBySessionId(string sessionId)
    {
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.Id == sessionId && !kvp.Value.IsExpired)
                return kvp.Value;
        }
        return null;
    }

    public bool Remove(long chatId) => _sessions.TryRemove(chatId, out _);
}
