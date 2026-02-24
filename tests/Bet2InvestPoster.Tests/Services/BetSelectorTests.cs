using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class BetSelectorTests
{
    private static PendingBet MakeBet(int id) => new PendingBet
    {
        Id = id,
        Team = "TEAM1",
        Market = new PendingBetMarket { MatchupId = $"{id}", Key = "s;0;m" }
    };

    private static BetSelector CreateSelector(IEnumerable<string>? publishedKeys = null)
        => new(new FakeHistoryManager(publishedKeys), NullLogger<BetSelector>.Instance);

    [Fact]
    public async Task SelectAsync_ExcludesAlreadyPublishedBets()
    {
        var selector = CreateSelector(publishedKeys: ["3|s;0;m|home", "5|s;0;m|home"]);
        var candidates = Enumerable.Range(1, 20).Select(MakeBet).ToList(); // 20 candidats, 2 exclus = 18 disponibles

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 5);
        Assert.All(result, b => Assert.Contains(b.Id, candidates.Select(c => c.Id)));
    }

    [Fact]
    public async Task SelectAsync_TargetIsOneOf_5_10_Or_15()
    {
        var selector = CreateSelector();
        var candidates = Enumerable.Range(1, 20).Select(MakeBet).ToList();
        var validCounts = new HashSet<int> { 5, 10, 15 };
        var observedCounts = new HashSet<int>();

        for (int i = 0; i < 50; i++)
        {
            var result = await selector.SelectAsync(candidates);
            observedCounts.Add(result.Count);
            Assert.Contains(result.Count, validCounts);
        }

        // Après 50 runs, on doit voir au moins 2 valeurs différentes
        Assert.True(observedCounts.Count > 1, "La sélection doit varier aléatoirement");

        // Vérifier que les éléments sélectionnés varient (pas toujours les mêmes)
        var allSelectedIds = new List<HashSet<int>>();
        for (int j = 0; j < 10; j++)
        {
            var r = await selector.SelectAsync(candidates);
            allSelectedIds.Add(r.Select(b => b.Id).ToHashSet());
        }
        // Au moins 2 sélections différentes parmi 10 runs
        Assert.True(allSelectedIds.Distinct(HashSet<int>.CreateSetComparer()).Count() > 1,
            "Les éléments sélectionnés doivent varier aléatoirement");
    }

    [Fact]
    public async Task SelectAsync_WhenFewerCandidatesThanTarget_ReturnsAll()
    {
        var selector = CreateSelector();
        var candidates = Enumerable.Range(1, 3).Select(MakeBet).ToList(); // 3 < 5 (min cible)

        var result = await selector.SelectAsync(candidates);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task SelectAsync_WithEmptyCandidates_ReturnsEmptyList()
    {
        var selector = CreateSelector();

        var result = await selector.SelectAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SelectAsync_AllCandidatesAlreadyPublished_ReturnsEmptyList()
    {
        var selector = CreateSelector(publishedKeys: ["1|s;0;m|home", "2|s;0;m|home", "3|s;0;m|home", "4|s;0;m|home", "5|s;0;m|home"]);
        var candidates = Enumerable.Range(1, 5).Select(MakeBet).ToList();

        var result = await selector.SelectAsync(candidates);

        Assert.Empty(result);
    }

    [Fact]
    public void BetSelector_RegisteredAsScoped()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.Configure<PosterOptions>(o => o.DataPath = tempDir);
            services.AddScoped<IHistoryManager, HistoryManager>();
            services.AddScoped<IBetSelector, BetSelector>();

            using var provider = services.BuildServiceProvider();

            // Même scope → même instance (vérifie Scoped, pas Transient)
            using var scope1 = provider.CreateScope();
            var a = scope1.ServiceProvider.GetRequiredService<IBetSelector>();
            var b = scope1.ServiceProvider.GetRequiredService<IBetSelector>();
            Assert.Same(a, b);

            // Scope différent → instance différente (vérifie Scoped, pas Singleton)
            using var scope2 = provider.CreateScope();
            var c = scope2.ServiceProvider.GetRequiredService<IBetSelector>();
            Assert.NotSame(a, c);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Stub minimal — pas de framework de mocking ─────────────────────────────
    private sealed class FakeHistoryManager : IHistoryManager
    {
        private readonly HashSet<string> _publishedKeys;

        public FakeHistoryManager(IEnumerable<string>? publishedKeys = null)
        {
            _publishedKeys = publishedKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default)
            => Task.FromResult(_publishedKeys);

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
    }
}
