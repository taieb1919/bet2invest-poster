using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class HistoryManagerTests : IDisposable
{
    private readonly string _tempDir;

    public HistoryManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private HistoryManager CreateManager(TimeProvider? timeProvider = null) =>
        new(Options.Create(new PosterOptions { DataPath = _tempDir }),
            NullLogger<HistoryManager>.Instance,
            timeProvider);

    private string HistoryPath => Path.Combine(_tempDir, "history.json");

    private static HistoryEntry MakeEntry(int betId, string matchupId = "100", string marketKey = "s;0;m", string designation = "home", DateTime? publishedAt = null)
        => new()
        {
            BetId = betId,
            MatchupId = matchupId,
            MarketKey = marketKey,
            Designation = designation,
            PublishedAt = publishedAt ?? DateTime.UtcNow
        };

    /// <summary>Fixed-time TimeProvider for deterministic purge boundary tests (L2).</summary>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task LoadPublishedKeysAsync_WithExistingHistory_ReturnsAllKeys()
    {
        var manager = CreateManager();
        await manager.RecordAsync(MakeEntry(10, "100", "s;0;m", "home"));
        await manager.RecordAsync(MakeEntry(20, "200", "s;0;ou;5.5", "over"));
        await manager.RecordAsync(MakeEntry(30, "300", "s;0;s;3.0", "away"));

        var keys = await manager.LoadPublishedKeysAsync();

        Assert.Equal(3, keys.Count);
        Assert.Contains("100|s;0;m|home", keys);
        Assert.Contains("200|s;0;ou;5.5|over", keys);
        Assert.Contains("300|s;0;s;3.0|away", keys);
    }

    [Fact]
    public async Task LoadPublishedKeysAsync_WhenFileAbsent_ReturnsEmptySet()
    {
        var manager = CreateManager();

        var keys = await manager.LoadPublishedKeysAsync();

        Assert.NotNull(keys);
        Assert.Empty(keys);
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public async Task RecordAsync_AddsEntryAndWritesAtomically()
    {
        var manager = CreateManager();

        await manager.RecordAsync(MakeEntry(42, "999", "s;0;m", "home"));

        Assert.True(File.Exists(HistoryPath));
        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Contains("999|s;0;m|home", keys);
    }

    [Fact]
    public async Task RecordAsync_AppendsToPreviousHistory()
    {
        var manager = CreateManager();

        await manager.RecordAsync(MakeEntry(1, "100", "s;0;m", "home"));
        await manager.RecordAsync(MakeEntry(2, "200", "s;0;ou;5.5", "over"));

        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_RemovesEntriesOlderThan30Days()
    {
        var manager = CreateManager();
        await manager.RecordAsync(MakeEntry(1, publishedAt: DateTime.UtcNow));
        await manager.RecordAsync(MakeEntry(2, matchupId: "200", publishedAt: DateTime.UtcNow.AddDays(-31)));

        await manager.PurgeOldEntriesAsync();

        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Contains("100|s;0;m|home", keys);
        Assert.DoesNotContain("200|s;0;m|home", keys);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_WhenNoExpiredEntries_PreservesAll()
    {
        var manager = CreateManager();
        await manager.RecordAsync(MakeEntry(1, publishedAt: DateTime.UtcNow));
        await manager.RecordAsync(MakeEntry(2, matchupId: "200", publishedAt: DateTime.UtcNow.AddDays(-15)));

        await manager.PurgeOldEntriesAsync();

        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_WhenFileAbsent_DoesNotThrow()
    {
        var manager = CreateManager();

        var ex = await Record.ExceptionAsync(() => manager.PurgeOldEntriesAsync());

        Assert.Null(ex);
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public void HistoryManager_RegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddSingleton<IHistoryManager, HistoryManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IHistoryManager>();

        Assert.NotNull(manager);
        Assert.IsType<HistoryManager>(manager);
    }

    [Fact]
    public void HistoryManager_DiDescriptor_HasSingletonLifetimeAndCorrectImplementation()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddSingleton<IHistoryManager, HistoryManager>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IHistoryManager));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(HistoryManager), descriptor.ImplementationType);
    }

    [Fact]
    public async Task RecordAsync_DuplicateKey_IsIgnored()
    {
        var manager = CreateManager();
        // Same match+market+designation = duplicate, even different betId
        await manager.RecordAsync(MakeEntry(42, "100", "s;0;m", "home"));
        await manager.RecordAsync(MakeEntry(99, "100", "s;0;m", "home"));

        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Single(keys);
    }

    [Fact]
    public async Task GetRecentEntriesAsync_WithMoreThanCount_ReturnsCountMostRecent()
    {
        var manager = CreateManager();
        for (int i = 0; i < 10; i++)
        {
            await manager.RecordAsync(MakeEntry(i, matchupId: i.ToString(), publishedAt: DateTime.UtcNow.AddHours(-i)));
        }

        var result = await manager.GetRecentEntriesAsync(7);

        Assert.Equal(7, result.Count);
    }

    [Fact]
    public async Task GetRecentEntriesAsync_WhenEmpty_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var result = await manager.GetRecentEntriesAsync(7);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentEntriesAsync_WithLessThanCount_ReturnsAllEntries()
    {
        var manager = CreateManager();
        await manager.RecordAsync(MakeEntry(1, matchupId: "1", publishedAt: DateTime.UtcNow));
        await manager.RecordAsync(MakeEntry(2, matchupId: "2", publishedAt: DateTime.UtcNow.AddHours(-1)));

        var result = await manager.GetRecentEntriesAsync(7);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetRecentEntriesAsync_ReturnsSortedByPublishedAtDescending()
    {
        var manager = CreateManager();
        var oldest = DateTime.UtcNow.AddHours(-5);
        var newest = DateTime.UtcNow;
        await manager.RecordAsync(MakeEntry(1, matchupId: "1", publishedAt: oldest));
        await manager.RecordAsync(MakeEntry(2, matchupId: "2", publishedAt: newest));

        var result = await manager.GetRecentEntriesAsync(7);

        Assert.Equal(2, result[0].BetId); // newest first
        Assert.Equal(1, result[1].BetId);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_ExactlyAtBoundary_PreservesEntry()
    {
        var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var manager = CreateManager(fixedTime);

        // Entry exactly at cutoff → should be preserved
        await manager.RecordAsync(MakeEntry(1, "100", publishedAt: new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc)));
        // Entry 1 second before cutoff → should be purged
        await manager.RecordAsync(MakeEntry(2, "200", publishedAt: new DateTime(2026, 1, 29, 23, 59, 59, DateTimeKind.Utc)));

        await manager.PurgeOldEntriesAsync();

        var keys = await manager.LoadPublishedKeysAsync();
        Assert.Contains("100|s;0;m|home", keys);
        Assert.DoesNotContain("200|s;0;m|home", keys);
    }
}
