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

    /// <summary>Fixed-time TimeProvider for deterministic purge boundary tests (L2).</summary>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // --- 5.3: LoadPublishedIdsAsync with existing history ---

    [Fact]
    public async Task LoadPublishedIdsAsync_WithExistingHistory_ReturnsAllBetIds()
    {
        // Arrange: write 3 entries directly
        var entries = new[]
        {
            new { betId = 10, publishedAt = DateTime.UtcNow.ToString("o") },
            new { betId = 20, publishedAt = DateTime.UtcNow.ToString("o") },
            new { betId = 30, publishedAt = DateTime.UtcNow.ToString("o") }
        };
        await File.WriteAllTextAsync(HistoryPath,
            System.Text.Json.JsonSerializer.Serialize(entries));

        var manager = CreateManager();

        // Act
        var ids = await manager.LoadPublishedIdsAsync();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains(10, ids);
        Assert.Contains(20, ids);
        Assert.Contains(30, ids);
    }

    // --- 5.4: LoadPublishedIdsAsync when file absent ---

    [Fact]
    public async Task LoadPublishedIdsAsync_WhenFileAbsent_ReturnsEmptySet()
    {
        var manager = CreateManager();

        var ids = await manager.LoadPublishedIdsAsync();

        Assert.NotNull(ids);
        Assert.Empty(ids);
        Assert.False(File.Exists(HistoryPath));
    }

    // --- 5.5: RecordAsync creates file and persists entry ---

    [Fact]
    public async Task RecordAsync_AddsEntryAndWritesAtomically()
    {
        var manager = CreateManager();
        var entry = new HistoryEntry { BetId = 42, PublishedAt = DateTime.UtcNow };

        await manager.RecordAsync(entry);

        Assert.True(File.Exists(HistoryPath));
        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Contains(42, ids);
    }

    // --- 5.6: RecordAsync appends to previous history ---

    [Fact]
    public async Task RecordAsync_AppendsToPreviousHistory()
    {
        var manager = CreateManager();
        var entry1 = new HistoryEntry { BetId = 1, PublishedAt = DateTime.UtcNow };
        var entry2 = new HistoryEntry { BetId = 2, PublishedAt = DateTime.UtcNow };

        await manager.RecordAsync(entry1);
        await manager.RecordAsync(entry2);

        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Equal(2, ids.Count);
        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
    }

    // --- 5.7: PurgeOldEntriesAsync removes entries older than 30 days ---

    [Fact]
    public async Task PurgeOldEntriesAsync_RemovesEntriesOlderThan30Days()
    {
        var manager = CreateManager();
        var recent = new HistoryEntry { BetId = 1, PublishedAt = DateTime.UtcNow };
        var old = new HistoryEntry { BetId = 2, PublishedAt = DateTime.UtcNow.AddDays(-31) };

        await manager.RecordAsync(recent);
        await manager.RecordAsync(old);

        await manager.PurgeOldEntriesAsync();

        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }

    // --- 5.8: PurgeOldEntriesAsync preserves all when none expired ---

    [Fact]
    public async Task PurgeOldEntriesAsync_WhenNoExpiredEntries_PreservesAll()
    {
        var manager = CreateManager();
        var e1 = new HistoryEntry { BetId = 1, PublishedAt = DateTime.UtcNow };
        var e2 = new HistoryEntry { BetId = 2, PublishedAt = DateTime.UtcNow.AddDays(-15) };

        await manager.RecordAsync(e1);
        await manager.RecordAsync(e2);

        await manager.PurgeOldEntriesAsync();

        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Equal(2, ids.Count);
        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
    }

    // --- 5.9: PurgeOldEntriesAsync does not throw when file absent ---

    [Fact]
    public async Task PurgeOldEntriesAsync_WhenFileAbsent_DoesNotThrow()
    {
        var manager = CreateManager();

        // Should complete without exception
        var ex = await Record.ExceptionAsync(() => manager.PurgeOldEntriesAsync());

        Assert.Null(ex);
        Assert.False(File.Exists(HistoryPath));
    }

    // --- 5.10: DI Scoped registration ---

    [Fact]
    public void HistoryManager_RegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddScoped<IHistoryManager, HistoryManager>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IHistoryManager>();

        Assert.NotNull(manager);
        Assert.IsType<HistoryManager>(manager);
    }

    // --- M2: DI descriptor verifies Scoped lifetime and correct implementation type ---

    [Fact]
    public void HistoryManager_DiDescriptor_HasScopedLifetimeAndCorrectImplementation()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddScoped<IHistoryManager, HistoryManager>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IHistoryManager));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(HistoryManager), descriptor.ImplementationType);
    }

    // --- L3: RecordAsync ignores duplicate betId ---

    [Fact]
    public async Task RecordAsync_DuplicateBetId_IsIgnored()
    {
        var manager = CreateManager();
        var entry1 = new HistoryEntry { BetId = 42, PublishedAt = DateTime.UtcNow };
        var entry2 = new HistoryEntry { BetId = 42, PublishedAt = DateTime.UtcNow };

        await manager.RecordAsync(entry1);
        await manager.RecordAsync(entry2);

        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Single(ids);
        Assert.Contains(42, ids);
    }

    // --- L2: PurgeOldEntriesAsync boundary test with fixed TimeProvider ---

    [Fact]
    public async Task PurgeOldEntriesAsync_ExactlyAtBoundary_PreservesEntry()
    {
        // Fixed time: 2026-03-01 00:00:00 UTC → cutoff = 2026-01-30 00:00:00 UTC
        var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var manager = CreateManager(fixedTime);

        // Entry exactly at cutoff (== cutoff, not < cutoff) → should be preserved
        var atBoundary = new HistoryEntry
        {
            BetId = 1,
            PublishedAt = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc)
        };
        // Entry 1 second before cutoff → should be purged
        var justBefore = new HistoryEntry
        {
            BetId = 2,
            PublishedAt = new DateTime(2026, 1, 29, 23, 59, 59, DateTimeKind.Utc)
        };

        await manager.RecordAsync(atBoundary);
        await manager.RecordAsync(justBefore);

        await manager.PurgeOldEntriesAsync();

        var ids = await manager.LoadPublishedIdsAsync();
        Assert.Contains(1, ids);
        Assert.DoesNotContain(2, ids);
    }
}
