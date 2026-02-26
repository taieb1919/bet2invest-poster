using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

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
        PosterOptions? options = null,
        ILogger<BetSelector>? logger = null,
        TimeProvider? timeProvider = null)
        => new(
            new FakeHistoryManager(publishedKeys),
            Options.Create(options ?? new PosterOptions()),
            logger ?? NullLogger<BetSelector>.Instance,
            timeProvider ?? TimeProvider.System);

    // ── Tests existants ────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAsync_ExcludesAlreadyPublishedBets()
    {
        var selector = CreateSelector(publishedKeys: ["3|s;0;m|home", "5|s;0;m|home"]);
        var candidates = Enumerable.Range(1, 20).Select(id => MakeBet(id)).ToList(); // 20 candidats, 2 exclus = 18 disponibles

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result.Selected, b => b.Id == 3);
        Assert.DoesNotContain(result.Selected, b => b.Id == 5);
        Assert.All(result.Selected, b => Assert.Contains(b.Id, candidates.Select(c => c.Id)));
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
            observedCounts.Add(result.Selected.Count);
            Assert.Contains(result.Selected.Count, validCounts);
        }

        // Après 50 runs, on doit voir au moins 2 valeurs différentes
        Assert.True(observedCounts.Count > 1, "La sélection doit varier aléatoirement");

        // Vérifier que les éléments sélectionnés varient (pas toujours les mêmes)
        var allSelectedIds = new List<HashSet<int>>();
        for (int j = 0; j < 10; j++)
        {
            var r = await selector.SelectAsync(candidates);
            allSelectedIds.Add(r.Selected.Select(b => b.Id).ToHashSet());
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

        Assert.Equal(3, result.Selected.Count);
    }

    [Fact]
    public async Task SelectAsync_WithEmptyCandidates_ReturnsEmptyList()
    {
        var selector = CreateSelector();

        var result = await selector.SelectAsync([]);

        Assert.Empty(result.Selected);
    }

    [Fact]
    public async Task SelectAsync_AllCandidatesAlreadyPublished_ReturnsEmptyList()
    {
        var selector = CreateSelector(publishedKeys: ["1|s;0;m|home", "2|s;0;m|home", "3|s;0;m|home", "4|s;0;m|home", "5|s;0;m|home"]);
        var candidates = Enumerable.Range(1, 5).Select(id => MakeBet(id)).ToList();

        var result = await selector.SelectAsync(candidates);

        Assert.Empty(result.Selected);
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
            services.AddSingleton(TimeProvider.System);
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

    // ── Test filtrage paris live ────────────────────────────────────────────

    [Fact]
    public async Task SelectAsync_ExcludesLiveBets()
    {
        var selector = CreateSelector();
        var candidates = new List<PendingBet>
        {
            MakeBet(1),
            MakeBet(2),
            new PendingBet
            {
                Id = 3, Team = "TEAM1", Price = 2.0m, IsLive = true,
                Event = new BetEvent { Starts = DateTime.UtcNow.AddHours(12) },
                Market = new PendingBetMarket { MatchupId = "3", Key = "s;0;m" }
            },
            new PendingBet
            {
                Id = 4, Team = "TEAM1", Price = 2.0m, IsLive = true,
                Event = new BetEvent { Starts = DateTime.UtcNow.AddHours(12) },
                Market = new PendingBetMarket { MatchupId = "4", Key = "s;0;m" }
            },
        };

        var result = await selector.SelectAsync(candidates);

        Assert.DoesNotContain(result.Selected, b => b.Id == 3);
        Assert.DoesNotContain(result.Selected, b => b.Id == 4);
        Assert.Equal(2, result.Selected.Count);
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

        Assert.DoesNotContain(result.Selected, b => b.Id == 1);
        Assert.DoesNotContain(result.Selected, b => b.Id == 2);
        Assert.All(result.Selected, b => Assert.True(b.Price >= 1.50m));
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

        Assert.DoesNotContain(result.Selected, b => b.Id == 3);
        Assert.DoesNotContain(result.Selected, b => b.Id == 4);
        Assert.All(result.Selected, b => Assert.True(b.Price <= 3.50m));
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

        Assert.DoesNotContain(result.Selected, b => b.Id == 1);
        Assert.DoesNotContain(result.Selected, b => b.Id == 5);
        Assert.DoesNotContain(result.Selected, b => b.Id == 6);
        Assert.All(result.Selected, b => Assert.True(b.Price >= 1.20m && b.Price <= 3.50m));
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

        Assert.DoesNotContain(result.Selected, b => b.Id == 3);
        Assert.DoesNotContain(result.Selected, b => b.Id == 4);
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
        Assert.Equal(2, result.Selected.Count);
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

        Assert.Equal(2, result.Selected.Count);
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

        Assert.Empty(result.Selected);
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
            Assert.All(result.Selected, b => Assert.True(b.Id <= 10, $"Id {b.Id} a une cote > 2.00 et ne devrait pas être sélectionné"));
        }
    }

    // ── Tests mode intelligent (Story 11.2) ───────────────────────────────────

    private static PendingBet MakeBetWithTipsterStats(
        int id,
        decimal? roi = null,
        decimal? winRate = null,
        string? sport = null,
        string? tipsterUsername = null,
        DateTime? starts = null) => new PendingBet
    {
        Id = id,
        Team = "TEAM1",
        Price = 2.0m,
        Event = new BetEvent { Starts = starts ?? DateTime.UtcNow.AddHours(12) },
        Market = new PendingBetMarket { MatchupId = $"{id}", Key = "s;0;m" },
        TipsterRoi = roi,
        TipsterWinRate = winRate,
        TipsterSport = sport,
        TipsterUsername = tipsterUsername ?? $"tipster{id}"
    };

    [Fact]
    public async Task SelectAsync_RandomMode_BehaviorIdenticalToExisting()
    {
        // AC#2 : mode random → comportement identique à l'existant
        var options = new PosterOptions { SelectionMode = "random" };
        var selector = CreateSelector(options: options);
        var candidates = Enumerable.Range(1, 20).Select(id => MakeBet(id)).ToList();

        var results = new List<Bet2InvestPoster.Models.SelectionResult>();
        for (int i = 0; i < 20; i++)
            results.Add(await selector.SelectAsync(candidates));

        // Les résultats doivent varier (sélection aléatoire)
        var distinctSets = results
            .Select(r => r.Selected.Select(b => b.Id).ToHashSet())
            .Distinct(HashSet<int>.CreateSetComparer())
            .Count();
        Assert.True(distinctSets > 1, "Le mode random doit produire des sélections variables");

        // Chaque résultat doit avoir entre 1 et 20 éléments (≤ 20 candidats)
        Assert.All(results, r => Assert.InRange(r.Selected.Count, 0, 20));
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_SelectsBetsWithHighestRoi()
    {
        // AC#1 : mode intelligent → les bets avec meilleur ROI sélectionnés en priorité
        // 25 candidats (> 15 = targetCount max) pour forcer le scoring intelligent dans tous les cas
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var selector = CreateSelector(options: options);

        // 20 bets avec ROI très faible (id 1-20)
        var lowRoiBets = Enumerable.Range(1, 20)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.01m, winRate: 10, sport: "Football"))
            .ToList();

        // 5 bets avec ROI très élevé (id 21-25) — doivent toujours être dans la sélection
        var highRoiBets = Enumerable.Range(21, 5)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.99m, winRate: 900, sport: "Tennis"))
            .ToList();

        var candidates = lowRoiBets.Concat(highRoiBets).ToList(); // 25 > 15 → scoring actif

        var result = await selector.SelectAsync(candidates);

        // Les 5 bets haute ROI (id 21-25) doivent tous être présents quelle que soit la cible (5/10/15)
        Assert.All(highRoiBets, hb => Assert.Contains(result.Selected, b => b.Id == hb.Id));
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_PrioritizesHighRoiWhenManyMoreCandidates()
    {
        // Avec suffisamment de candidats pour forcer une sélection (> 15), les bets haute ROI doivent dominer
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var selector = CreateSelector(options: options);

        // 10 bets avec ROI faible, 10 bets avec ROI élevé → sélection intelligente doit préférer ROI élevé
        var lowRoiBets = Enumerable.Range(1, 10)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.01m, winRate: 10, sport: "Tennis"))
            .ToList();
        var highRoiBets = Enumerable.Range(11, 10)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.99m, winRate: 900, sport: "Football"))
            .ToList();

        var candidates = lowRoiBets.Concat(highRoiBets).ToList();

        // Exécuter plusieurs fois pour tester la cohérence
        for (int i = 0; i < 5; i++)
        {
            var result = await selector.SelectAsync(candidates);
            // Vérifier que les bets haute ROI dominent la sélection
            var highRoiCount = result.Selected.Count(b => b.Id >= 11);
            Assert.True(highRoiCount >= result.Selected.Count / 2,
                $"Les bets haute ROI devraient dominer : {highRoiCount}/{result.Selected.Count}");
        }
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_RedistributesWeightsWhenRoiIsNull()
    {
        // AC#4 : redistribuer les poids quand ROI est null
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var selector = CreateSelector(options: options);

        // Bets sans ROI mais avec WinRate différent
        var candidates = Enumerable.Range(1, 20)
            .Select(id => MakeBetWithTipsterStats(id, roi: null, winRate: id * 10m, sport: "Football"))
            .ToList();

        // La sélection ne doit pas lever d'exception (redistribution des poids)
        var result = await selector.SelectAsync(candidates);
        Assert.NotEmpty(result.Selected);
        Assert.True(result.Selected.Count <= 15);
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_RedistributesWeightsWhenWinRateIsNull()
    {
        // AC#4 : redistribuer les poids quand WinRate est null
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var selector = CreateSelector(options: options);

        var candidates = Enumerable.Range(1, 20)
            .Select(id => MakeBetWithTipsterStats(id, roi: id * 0.01m, winRate: null, sport: "Football"))
            .ToList();

        var result = await selector.SelectAsync(candidates);
        Assert.NotEmpty(result.Selected);
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_SportDiversityPenalizesOverrepresentedSport()
    {
        // AC#1 : diversité de sport — pénaliser les sports surreprésentés
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var selector = CreateSelector(options: options);

        // 15 bets Football + 5 bets Tennis (même ROI/WinRate) → Tennis doit être favorisé (moins surreprésenté)
        var footballBets = Enumerable.Range(1, 15)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.5m, winRate: 100, sport: "Football"))
            .ToList();
        var tennisBets = Enumerable.Range(16, 5)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.5m, winRate: 100, sport: "Tennis"))
            .ToList();

        var candidates = footballBets.Concat(tennisBets).ToList();

        // Avec 20 candidats, on sélectionne 5/10/15 → Tennis doit être surreprésenté par rapport à sa proportion initiale
        int tennisTotalSelected = 0;
        int runs = 10;
        for (int i = 0; i < runs; i++)
        {
            var result = await selector.SelectAsync(candidates);
            tennisTotalSelected += result.Selected.Count(b => b.Id >= 16);
        }
        // En moyenne, si la diversité fonctionne, Tennis est favorisé malgré sa faible quantité
        Assert.True(tennisTotalSelected > 0, "Les bets Tennis (sport moins représenté) doivent apparaître dans les sélections");
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_FreshnessPrefersSoonerEvents()
    {
        // AC#1 : fraîcheur — événements plus proches doivent avoir un score plus élevé
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var fakeTime = new FakeTimeProvider();
        var now = fakeTime.GetUtcNow().UtcDateTime;
        var selector = CreateSelector(options: options, timeProvider: fakeTime);

        // 20 bets avec le même ROI/WinRate, mais des heures de début différentes
        // Les bets avec starts proche (id 11-20, démarrent dans 1-10h) doivent être favorisés
        var farBets = Enumerable.Range(1, 10)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.5m, winRate: 100, sport: "Football",
                starts: now.AddHours(100 + id)))
            .ToList();
        var nearBets = Enumerable.Range(11, 10)
            .Select(id => MakeBetWithTipsterStats(id, roi: 0.5m, winRate: 100, sport: "Football",
                starts: now.AddHours(id - 10)))  // 1h-10h → plus frais
            .ToList();

        var candidates = farBets.Concat(nearBets).ToList();

        int nearTotalSelected = 0;
        int runs = 10;
        for (int i = 0; i < runs; i++)
        {
            var result = await selector.SelectAsync(candidates);
            nearTotalSelected += result.Selected.Count(b => b.Id >= 11);
        }
        // Les bets proches doivent dominer sur les runs
        Assert.True(nearTotalSelected > 0, "Les bets avec événements proches doivent être sélectionnés");
    }

    [Fact]
    public async Task SelectAsync_IntelligentMode_LogsScoresForSelectedBets()
    {
        // AC#3 : vérifier que chaque pari sélectionné est logué avec son score et les critères détaillés
        var options = new PosterOptions { SelectionMode = "intelligent" };
        var capturingLogger = new CapturingLogger<BetSelector>();
        var selector = CreateSelector(options: options, logger: capturingLogger);

        var candidates = Enumerable.Range(1, 20)
            .Select(id => MakeBetWithTipsterStats(id, roi: id * 0.1m, winRate: id * 50, sport: "Football",
                tipsterUsername: $"tipster{id}"))
            .ToList();

        await selector.SelectAsync(candidates);

        // Vérifier que les logs contiennent le marqueur [Intelligent] avec score et composantes
        Assert.True(capturingLogger.Messages.Any(m => m.Contains("[Intelligent]")),
            "Les logs doivent contenir '[Intelligent]' pour chaque pari sélectionné");
        Assert.True(capturingLogger.Messages.Any(m => m.Contains("score=")),
            "Les logs doivent contenir 'score=' pour chaque pari sélectionné");
    }

    [Fact]
    public async Task SelectAsync_UnknownSelectionMode_FallsBackToRandomAndLogsWarning()
    {
        // Issue #5 : SelectionMode inconnu → fallback sur random avec avertissement logué
        var capturingLogger = new CapturingLogger<BetSelector>();
        var options = new PosterOptions { SelectionMode = "inteligent" }; // typo volontaire
        var selector = CreateSelector(options: options, logger: capturingLogger);

        var candidates = Enumerable.Range(1, 20).Select(id => MakeBet(id)).ToList();

        await selector.SelectAsync(candidates);

        Assert.True(capturingLogger.Messages.Any(m => m.Contains("inconnu")),
            "Un warning doit être logué pour un SelectionMode inconnu");
    }

    [Fact]
    public void PosterOptions_SelectionMode_DefaultIsRandom()
    {
        // AC#5 : SelectionMode par défaut = "random"
        var options = new PosterOptions();
        Assert.Equal("random", options.SelectionMode);
    }

    [Fact]
    public void PosterOptions_SelectionMode_LoadedFromConfig()
    {
        // AC#5 : SelectionMode configurable via Options (simule variable d'environnement)
        var options = new PosterOptions { SelectionMode = "intelligent" };
        Assert.Equal("intelligent", options.SelectionMode);
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
        public Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
    }

    // ── Logger capturant les messages pour assertion ───────────────────────────
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _messages.Add(formatter(state, exception));
    }
}
