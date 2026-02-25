namespace Bet2InvestPoster.Configuration;

public class PosterOptions
{
    public const string SectionName = "Poster";

    public string ScheduleTime { get; set; } = "08:00";
    public int RetryDelayMs { get; set; } = 60000;
    public int MaxRetryCount { get; set; } = 3;
    public string DataPath { get; set; } = ".";
    public string LogPath { get; set; } = "logs";
    public string BankrollId { get; set; } = "";

    public decimal? MinOdds { get; set; }      // null = pas de filtrage
    public decimal? MaxOdds { get; set; }      // null = pas de filtrage
    public int? EventHorizonHours { get; set; } // null = pas de filtrage
}
