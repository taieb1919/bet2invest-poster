namespace Bet2InvestPoster.Configuration;

public class Bet2InvestOptions
{
    public const string SectionName = "Bet2Invest";

    public string ApiBase { get; set; } = "https://api.bet2invest.com";
    public string Identifier { get; set; } = "";
    public string Password { get; set; } = "";
    public int RequestDelayMs { get; set; } = 500;

    // Prevents accidental credential exposure via logger.LogInformation("opts: {Opts}", this)
    public override string ToString() =>
        $"Bet2InvestOptions {{ ApiBase={ApiBase}, Identifier={Identifier}, Password=[REDACTED], RequestDelayMs={RequestDelayMs} }}";
}
