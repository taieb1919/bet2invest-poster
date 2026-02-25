namespace Bet2InvestPoster.Models;

/// <summary>
/// Tipster gratuit (free) récupéré depuis l'API bet2invest lors d'un scraping.
/// Modèle de transfert — converti en <see cref="TipsterConfig"/> avant persistance.
/// </summary>
public class ScrapedTipster
{
    public string Username { get; set; } = string.Empty;
    public decimal Roi { get; set; }
    public int BetsNumber { get; set; }
    public string MostBetSport { get; set; } = string.Empty;

    /// <summary>
    /// Convertit en <see cref="TipsterConfig"/> pour la persistance dans tipsters.json.
    /// Le slug utilisé est le <see cref="Username"/>.
    /// </summary>
    public TipsterConfig ToTipsterConfig()
    {
        var url = $"https://bet2invest.com/tipsters/performance-stats/{Username}";
        return new TipsterConfig { Url = url, Name = Username };
    }
}
