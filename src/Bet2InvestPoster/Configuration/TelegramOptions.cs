namespace Bet2InvestPoster.Configuration;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = "";
    public long AuthorizedChatId { get; set; }

    // Prevents accidental credential exposure via logger.LogInformation("opts: {Opts}", this)
    public override string ToString() =>
        $"TelegramOptions {{ BotToken=[REDACTED], AuthorizedChatId={AuthorizedChatId} }}";
}
