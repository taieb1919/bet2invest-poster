using System.Text.Json;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class HistoryManager : IHistoryManager
{
    private readonly string _historyPath;
    private readonly ILogger<HistoryManager> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public HistoryManager(IOptions<PosterOptions> options, ILogger<HistoryManager> logger, TimeProvider? timeProvider = null)
    {
        _historyPath = Path.Combine(options.Value.DataPath, "history.json");
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // L1: Ensure parent directory exists for first-run (AC#4)
        var dir = Path.GetDirectoryName(_historyPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);
    }

    // M1: Delegate to LoadEntriesAsync to avoid duplication
    public async Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
    {
        var entries = await LoadEntriesAsync(ct);
        return entries.Select(e => e.BetId).ToHashSet();
    }

    // M3: LogContext scope wraps entire operation
    // L3: Duplicate betId guard
    public async Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Publish"))
        {
            var entries = await LoadEntriesAsync(ct);

            if (entries.Any(e => e.BetId == entry.BetId))
            {
                _logger.LogWarning(
                    "betId={BetId} déjà présent dans l'historique, enregistrement ignoré", entry.BetId);
                return;
            }

            entries.Add(entry);
            await SaveAtomicAsync(entries, ct);

            _logger.LogInformation(
                "Paris enregistré dans l'historique : betId={BetId}", entry.BetId);
        }
    }

    // L2: Use injected TimeProvider instead of DateTime.UtcNow
    public async Task PurgeOldEntriesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyPath))
            return;

        var entries = await LoadEntriesAsync(ct);
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        var purged = entries.RemoveAll(e => e.PublishedAt < cutoff);

        using (LogContext.PushProperty("Step", "Purge"))
        {
            if (purged > 0)
            {
                await SaveAtomicAsync(entries, ct);
                _logger.LogInformation(
                    "{Count} entrée(s) purgées de l'historique (> 30 jours)", purged);
            }
            else
            {
                _logger.LogInformation(
                    "Aucune entrée à purger dans l'historique (0 entrée > 30 jours)");
            }
        }
    }

    private async Task<List<HistoryEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        if (!File.Exists(_historyPath))
            return [];

        var json = await File.ReadAllTextAsync(_historyPath, ct);
        return JsonSerializer.Deserialize<List<HistoryEntry>>(json, _jsonOptions) ?? [];
    }

    private async Task SaveAtomicAsync(List<HistoryEntry> entries, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        var tempPath = _historyPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, _historyPath, overwrite: true);
    }
}
