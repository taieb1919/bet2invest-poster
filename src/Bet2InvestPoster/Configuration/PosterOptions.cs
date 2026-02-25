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

    /// <summary>
    /// Nombre de fichiers de log à conserver. Correspond à <c>retainedFileCountLimit</c> de Serilog,
    /// utilisé conjointement avec <c>RollingInterval.Day</c> (1 fichier = 1 jour).
    /// Doit être > 0 ; la valeur 0 provoque une <see cref="InvalidOperationException"/> au démarrage.
    /// </summary>
    public int LogRetentionDays { get; set; } = 30;

    public decimal? MinOdds { get; set; }      // null = pas de filtrage
    public decimal? MaxOdds { get; set; }      // null = pas de filtrage
    public int? EventHorizonHours { get; set; } // null = pas de filtrage

    /// <summary>Mode de sélection des pronostics : "random" (défaut) ou "intelligent" (scoring multi-critères).</summary>
    public string SelectionMode { get; set; } = "random";

    public int CircuitBreakerFailureThreshold { get; set; } = 3;
    public int CircuitBreakerDurationSeconds { get; set; } = 300;
    public int HealthCheckPort { get; set; } = 8080;
}
