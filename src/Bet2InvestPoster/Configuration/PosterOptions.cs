namespace Bet2InvestPoster.Configuration;

public class StakeRule
{
    public decimal? MaxOdds { get; set; }
    public decimal Units { get; set; }
}

public class PosterOptions
{
    public const string SectionName = "Poster";

    public string ScheduleTime { get; set; } = "08:00";
    public string[]? ScheduleTimes { get; set; }

    private static readonly string[] DefaultScheduleTimes = ["08:00", "13:00", "19:00"];

    public string[] GetEffectiveScheduleTimes()
    {
        if (ScheduleTimes is { Length: > 0 })
            return ScheduleTimes;
        if (!string.IsNullOrWhiteSpace(ScheduleTime))
            return [ScheduleTime];
        return DefaultScheduleTimes;
    }
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

    public decimal? MinOdds { get; set; }
    public decimal? MaxOdds { get; set; }
    public int? EventHorizonHours { get; set; }

    /// <summary>Mode de sélection des pronostics : "random" (défaut) ou "intelligent" (scoring multi-critères).</summary>
    public string SelectionMode { get; set; } = "random";

    public List<StakeRule> StakeRules { get; set; } = [];

    public decimal ResolveStake(decimal odds)
    {
        if (StakeRules is not { Count: > 0 })
            return 1m;

        var sorted = StakeRules
            .OrderBy(r => r.MaxOdds ?? decimal.MaxValue)
            .ToList();

        foreach (var rule in sorted)
        {
            if (rule.MaxOdds.HasValue && odds < rule.MaxOdds.Value)
                return rule.Units;
        }

        return sorted.Last().Units;
    }

    public int CircuitBreakerFailureThreshold { get; set; } = 3;
    public int CircuitBreakerDurationSeconds { get; set; } = 300;
    public int HealthCheckPort { get; set; } = 8080;
}
