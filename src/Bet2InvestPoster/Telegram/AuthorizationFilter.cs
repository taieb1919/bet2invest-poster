using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Telegram;

public class AuthorizationFilter
{
    private readonly long _authorizedChatId;
    private readonly ILogger<AuthorizationFilter> _logger;

    public AuthorizationFilter(IOptions<TelegramOptions> options, ILogger<AuthorizationFilter> logger)
    {
        _authorizedChatId = options.Value.AuthorizedChatId;
        _logger = logger;
    }

    public bool IsAuthorized(long chatId)
    {
        if (chatId == _authorizedChatId)
            return true;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogDebug("Message ignoré — chat ID {ChatId} non autorisé", chatId);
        }

        return false;
    }
}
