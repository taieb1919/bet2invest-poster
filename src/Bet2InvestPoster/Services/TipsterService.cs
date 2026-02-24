using System.Text.Json;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class TipsterService : ITipsterService
{
    private readonly PosterOptions _options;
    private readonly ILogger<TipsterService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TipsterService(IOptions<PosterOptions> options, ILogger<TipsterService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_options.DataPath, "tipsters.json");

        using (LogContext.PushProperty("Step", "Scrape"))
        {
            string json;
            try
            {
                json = await File.ReadAllTextAsync(filePath, ct);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError("tipsters.json introuvable dans {DataPath}", _options.DataPath);
                throw new FileNotFoundException("tipsters.json introuvable", filePath, ex);
            }
            catch (FileNotFoundException)
            {
                _logger.LogError("tipsters.json introuvable dans {DataPath}", _options.DataPath);
                throw;
            }

            List<TipsterConfig>? raw;
            try
            {
                raw = JsonSerializer.Deserialize<List<TipsterConfig>>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError("tipsters.json contient du JSON invalide: {Message}", ex.Message);
                throw;
            }

            if (raw is null || raw.Count == 0)
            {
                _logger.LogWarning("Aucun tipster configuré dans tipsters.json");
                return [];
            }

            var valid = new List<TipsterConfig>();
            foreach (var tipster in raw)
            {
                if (string.IsNullOrWhiteSpace(tipster.Url))
                {
                    _logger.LogWarning("Entrée ignorée : URL vide (name={Name})", tipster.Name);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tipster.Name))
                {
                    _logger.LogWarning("Entrée ignorée : nom vide (url={Url})", tipster.Url);
                    continue;
                }

                if (!tipster.TryExtractId(out _))
                {
                    _logger.LogWarning("Entrée ignorée : ID non extractible depuis {Url}", tipster.Url);
                    continue;
                }

                valid.Add(tipster);
            }

            _logger.LogInformation(
                "{Count} tipsters chargés depuis {Path} ({Total} entrées lues)",
                valid.Count, filePath, raw.Count);

            return valid;
        }
    }
}
