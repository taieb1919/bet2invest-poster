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

    // Static semaphore protects the file across Scoped instances (Option A from dev notes).
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
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

                if (!tipster.TryExtractSlug(out _))
                {
                    _logger.LogWarning("Entrée ignorée : slug non extractible depuis {Url}", tipster.Url);
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

    public async Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default)
    {
        var candidate = new TipsterConfig { Url = url, Name = string.Empty };

        if (!candidate.TryExtractSlug(out var slug) || string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException($"URL invalide ou slug non extractible : {url}", nameof(url));

        candidate.Name = slug;

        var filePath = Path.Combine(_options.DataPath, "tipsters.json");

        await FileLock.WaitAsync(ct);
        try
        {
            var raw = await ReadRawAsync(filePath, ct);

            // Check for duplicate (by URL or slug, case-insensitive)
            var isDuplicate = raw.Any(t =>
                string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase) ||
                (t.TryExtractSlug(out var existingSlug) &&
                 string.Equals(existingSlug, slug, StringComparison.OrdinalIgnoreCase)));

            if (isDuplicate)
                throw new InvalidOperationException($"Ce tipster est déjà dans la liste : {slug}");

            raw.Add(new TipsterConfig { Url = url, Name = slug });
            await SaveAtomicAsync(raw, filePath, ct);

            _logger.LogInformation("Tipster ajouté : {Slug} ({Url})", slug, url);
            return candidate;
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_options.DataPath, "tipsters.json");

        // Pre-extract slug from the input URL for slug-based matching
        var candidateRemove = new TipsterConfig { Url = url };
        candidateRemove.TryExtractSlug(out var removeSlug);

        await FileLock.WaitAsync(ct);
        try
        {
            var raw = await ReadRawAsync(filePath, ct);

            // Match by URL or slug (case-insensitive) — symmetric with AddTipsterAsync duplicate check
            var index = raw.FindIndex(t =>
                string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase) ||
                (removeSlug != null && t.TryExtractSlug(out var existingSlug) &&
                 string.Equals(existingSlug, removeSlug, StringComparison.OrdinalIgnoreCase)));

            if (index < 0)
            {
                _logger.LogWarning("Tipster non trouvé pour suppression : {Url}", url);
                return false;
            }

            raw.RemoveAt(index);
            await SaveAtomicAsync(raw, filePath, ct);

            _logger.LogInformation("Tipster retiré : {Url}", url);
            return true;
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task ReplaceTipstersAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_options.DataPath, "tipsters.json");

        await FileLock.WaitAsync(ct);
        try
        {
            await SaveAtomicAsync(tipsters, filePath, ct);
            _logger.LogInformation("Liste de tipsters remplacée — {Count} tipsters écrits", tipsters.Count);
        }
        finally
        {
            FileLock.Release();
        }
    }

    // ReadRawAsync retourne une liste vide si le fichier n'existe pas, contrairement à LoadTipstersAsync
    // qui lève FileNotFoundException. Ce comportement est délibéré : Add/Remove créent le fichier
    // implicitement si nécessaire, sans exiger qu'il préexiste.
    private static async Task<List<TipsterConfig>> ReadRawAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            return [];

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<List<TipsterConfig>>(json, JsonOptions) ?? [];
    }

    private static async Task SaveAtomicAsync(List<TipsterConfig> tipsters, string filePath, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tipsters, WriteJsonOptions);
        var tempPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
