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
    private static PendingBet MakeBet(int id, decimal price = 2.0m, DateTime? starts = null) => new PendingBet
    {
        Id = id,
        Team = "TEAM1",
        Price = price,
        Event = new BetEvent { Starts = starts ?? DateTime.UtcNow.AddHours(12) },
        Market = new PendingBetMarket { MatchupId = $"{id}", Key = "s;0;m" }
    };

    private static BetSelector CreateSelector(
        IEnumerable<string>? publishedKeys = null,
        PosterOptions? options = null)
        => new(
            new FakeHistoryManager(publishedKeys),
            Options.Create(options ?? new PosterOptions()),
            NullLogger<BetSelector>.Instance);

    // ── Tests existants ────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAsync_ExcludesAlreadyPublishedBets()
    {
        var selector = CreateSelector(publishedKeys: ["3|s;0;m|home", "5|s;0;m|home"]);
        var candidates = Enumerable.Range(1, 20).Select(id => MakeBet(id)).ToList(); // 20 candidats, 2 exclus = 18 disponibles

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 5);
        Assert.All(result, b => Assert.Contains(b.Id, candidates.Select(c => c.Id)));
    }

    [Fact]
    public async Task SelectAsync_TargetIsOneOf_5_10_Or_15()
    {
        var selector = CreateSelector();
        var candidates = Enumerable.Range(1, 20).Select(id => MakeBet(id)).ToList();
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
        var candidates = Enumerable.Range(1, 3).Select(id => MakeBet(id)).ToList(); // 3 < 5 (min cible)

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
        var candidates = Enumerable.Range(1, 5).Select(id => MakeBet(id)).ToList();

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

    // ── Tests filtrage par cotes (Story 9.1 AC#1, AC#2) ───────────────────

    [Fact]
    public async Task SelectAsync_MinOddsFilter_ExcludesBelowMinimum()
    {
        var options = new PosterOptions { MinOdds = 1.50m };
        var selector = CreateSelector(options: options);
        var candidates = new List<PendingBet>
        {
            MakeBet(1, price: 1.20m), // exclu
            MakeBet(2, price: 1.49m), // exclu
            MakeBet(3, price: 1.50m), // inclus (égal à min)
            MakeBet(4, price: 2.00m), // inclus
            MakeBet(5, price: 3.50m), // inclus
        };

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 1);
        Assert.DoesNotContain(result, b => b.Id == 2);
        Assert.All(result, b => Assert.True(b.Price >= 1.50m));
    }

    [Fact]
    public async Task SelectAsync_MaxOddsFilter_ExcludesAboveMaximum()
    {
        var options = new PosterOptions { MaxOdds = 3.50m };
        var selector = CreateSelector(options: options);
        var candidates = new List<PendingBet>
        {
            MakeBet(1, price: 1.20m), // inclus
            MakeBet(2, price: 3.50m), // inclus (égal à max)
            MakeBet(3, price: 3.51m), // exclu
            MakeBet(4, price: 5.00m), // exclu
        };

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 4);
        Assert.All(result, b => Assert.True(b.Price <= 3.50m));
    }

    [Fact]
    public async Task SelectAsync_MinAndMaxOddsFilter_ExcludesOutOfRange()
    {
        var options = new PosterOptions { MinOdds = 1.20m, MaxOdds = 3.50m };
        var selector = CreateSelector(options: options);
        // Créer assez de candidats pour satisfaire la sélection aléatoire (cible 5/10/15)
        var candidates = new List<PendingBet>
        {
            MakeBet(1, price: 1.10m),  // exclu (< min)
            MakeBet(2, price: 1.20m),  // inclus
            MakeBet(3, price: 2.50m),  // inclus
            MakeBet(4, price: 3.50m),  // inclus
            MakeBet(5, price: 3.51m),  // exclu (> max)
            MakeBet(6, price: 10.00m), // exclu (> max)
        };

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 1);
        Assert.DoesNotContain(result, b => b.Id == 5);
        Assert.DoesNotContain(result, b => b.Id == 6);
        Assert.All(result, b => Assert.True(b.Price >= 1.20m && b.Price <= 3.50m));
    }

    // ── Tests filtrage par plage horaire (Story 9.1 AC#1, AC#3) ──────────

    [Fact]
    public async Task SelectAsync_EventHorizonFilter_ExcludesEventsAfterHorizon()
    {
        var options = new PosterOptions { EventHorizonHours = 24 };
        var selector = CreateSelector(options: options);
        var now = DateTime.UtcNow;
        var candidates = new List<PendingBet>
        {
            MakeBet(1, starts: now.AddHours(1)),   // inclus (< 24h)
            MakeBet(2, starts: now.AddHours(23)),  // inclus (bien dans le horizon de 24h)
            MakeBet(3, starts: now.AddHours(25)),  // exclu (> 24h)
            MakeBet(4, starts: now.AddHours(48)),  // exclu (> 24h)
        };

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 4);
    }

    // ── Tests rétrocompatibilité (Story 9.1 AC#2, AC#3) ───────────────────

    [Fact]
    public async Task SelectAsync_NoOddsFilter_WhenOptionsAreNull_AllCandidatesPassed()
    {
        var options = new PosterOptions(); // MinOdds = null, MaxOdds = null
        var selector = CreateSelector(options: options);
        var candidates = new List<PendingBet>
        {
            MakeBet(1, price: 1.01m),  // très faible cote — inclus sans filtre
            MakeBet(2, price: 100.0m), // très haute cote — inclus sans filtre
        };

        var result = await selector.SelectAsync(candidates);

        // Tous les candidats doivent être disponibles (rétrocompatibilité)
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SelectAsync_NoHorizonFilter_WhenEventHorizonHoursIsNull_AllCandidatesPassed()
    {
        var options = new PosterOptions(); // EventHorizonHours = null
        var selector = CreateSelector(options: options);
        var now = DateTime.UtcNow;
        var candidates = new List<PendingBet>
        {
            MakeBet(1, starts: now.AddHours(1000)), // très loin — inclus sans filtre
            MakeBet(2, starts: now.AddDays(365)),   // un an dans le futur — inclus sans filtre
        };

        var result = await selector.SelectAsync(candidates);

        Assert.Equal(2, result.Count);
    }

    // ── Test zéro candidats après filtrage (Story 9.1 AC#4) ──────────────

    [Fact]
    public async Task SelectAsync_ZeroCandidatesAfterFilter_ReturnsEmptyList()
    {
        var options = new PosterOptions { MinOdds = 5.00m }; // filtre très restrictif
        var selector = CreateSelector(options: options);
        var candidates = new List<PendingBet>
        {
            MakeBet(1, price: 1.50m), // exclu
            MakeBet(2, price: 2.00m), // exclu
            MakeBet(3, price: 3.50m), // exclu
        };

        var result = await selector.SelectAsync(candidates);

        Assert.Empty(result);
    }

    // ── Test filtrage AVANT sélection aléatoire (Story 9.1 AC#1) ─────────

    [Fact]
    public async Task SelectAsync_FilterAppliedBeforeRandomSelection_OnlyFilteredCandidatesSelected()
    {
        var options = new PosterOptions { MaxOdds = 2.00m };
        var selector = CreateSelector(options: options);
        // 20 candidats : 10 avec cote <= 2.00 (id 1-10) et 10 avec cote > 2.00 (id 11-20)
        var candidates = Enumerable.Range(1, 10).Select(id => MakeBet(id, price: 1.50m))
            .Concat(Enumerable.Range(11, 10).Select(id => MakeBet(id, price: 3.00m)))
            .ToList();

        for (int i = 0; i < 20; i++)
        {
            var result = await selector.SelectAsync(candidates);
            // Aucun élément avec id > 10 ne doit être sélectionné
            Assert.All(result, b => Assert.True(b.Id <= 10, $"Id {b.Id} a une cote > 2.00 et ne devrait pas être sélectionné"));
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
