using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class MetaCounterService
{
    private static readonly string[] TrackedHeroKeys = ["underlord", "wisp"];
    private readonly HeroMetaService _metaService = new();
    private readonly DotaPatchContextService _patchService = new();
    private readonly DotaAssetResolver _assetResolver = new();

    public async Task<MetaCounterAnalysis> AnalyzeAsync(
        IReadOnlyList<RowConfig> rows,
        bool isThai,
        bool forceRefresh = false)
    {
        var catalog = await _metaService.GetCatalogAsync(forceRefresh);
        var plans = BuildPlans(rows, catalog, isThai);
        if (plans.Count == 0)
        {
            throw new InvalidOperationException("No valid team plans were found in board.json.");
        }

        var topMetaHeroes = catalog
            .OrderByDescending(hero => hero.PublicPicks)
            .Take(6)
            .Concat(TrackedHeroKeys.Select(key => FindHero(catalog, key)).OfType<HeroDirectoryEntry>())
            .DistinctBy(hero => hero.Id)
            .OrderByDescending(hero => hero.PublicPicks)
            .ToList();
        var recommendations = new List<MetaCounterRecommendation>();

        foreach (var metaHero in topMetaHeroes)
        {
            try
            {
                var matchupRates = await _metaService.GetMatchupRatesAsync(metaHero, forceRefresh);
                var rankedPlans = plans
                    .Select(plan => ScorePlan(plan, matchupRates))
                    .Where(result => result.HeroScores.Count > 0)
                    .OrderByDescending(result => result.Score)
                    .ToList();
                if (rankedPlans.Count == 0)
                {
                    continue;
                }

                var best = rankedPlans[0];
                var backup = rankedPlans.ElementAtOrDefault(1) ?? best;
                var strongest = best.HeroScores.OrderByDescending(score => score.WinRate).Take(2).ToList();
                recommendations.Add(new MetaCounterRecommendation
                {
                    Hero = metaHero.Name,
                    HeroKey = metaHero.Key,
                    IconPath = metaHero.IconPath,
                    PickRate = metaHero.PickRate,
                    WinRate = metaHero.WinRate,
                    MatchupScore = best.Score,
                    PlanNumber = best.Plan.Number,
                    PlanTitle = best.Plan.Title,
                    BackupPlanNumber = backup.Plan.Number,
                    BackupPlanTitle = backup.Plan.Title,
                    Picks = best.Plan.Picks.Select(pick => new MetaPlanPick
                    {
                        Hero = pick.Name,
                        Role = LocalizeRole(pick.Role, isThai),
                        IconPath = _assetResolver.ResolveHeroPath(pick.Key)
                    }).ToList(),
                    Why = BuildWhy(metaHero.Name, best.Score, strongest, isThai),
                    Adjustment = BuildAdjustment(metaHero.Name, strongest.FirstOrDefault()?.Name, isThai)
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (recommendations.Count == 0)
        {
            throw new InvalidOperationException("Current matchup data is unavailable. Try refreshing again in a moment.");
        }

        return new MetaCounterAnalysis
        {
            PatchNumber = await _patchService.GetCurrentPatchNumberAsync(forceRefresh),
            RetrievedAt = DateTimeOffset.Now,
            Recommendations = recommendations
        };
    }

    private static IReadOnlyList<PlanCandidate> BuildPlans(
        IReadOnlyList<RowConfig> rows,
        IReadOnlyList<HeroDirectoryEntry> catalog,
        bool isThai)
    {
        var heroesByKey = catalog.ToDictionary(hero => Normalize(hero.Key), StringComparer.OrdinalIgnoreCase);
        var heroesByName = catalog.ToDictionary(hero => Normalize(hero.Name), StringComparer.OrdinalIgnoreCase);
        var plans = new List<PlanCandidate>();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.Cells.TryGetValue("pick", out var pickCell)
                || !pickCell.TryGetProperty("picks", out var picksElement)
                || picksElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var picks = new List<PlanPick>();
            foreach (var pick in picksElement.EnumerateArray())
            {
                var key = GetString(pick, "hero");
                var name = GetString(pick, "name");
                var hero = FindHero(heroesByKey, heroesByName, key, name);
                if (hero is not null)
                {
                    picks.Add(new PlanPick(hero.Id, hero.Key, hero.Name, GetString(pick, "role")));
                }
            }

            if (picks.Count == 0)
            {
                continue;
            }

            var title = $"Plan {index + 1}";
            if (row.Cells.TryGetValue("detail", out var detail))
            {
                var propertyName = isThai && detail.TryGetProperty("specialtyTh", out _) ? "specialtyTh" : "specialty";
                var specialty = GetString(detail, propertyName);
                if (!string.IsNullOrWhiteSpace(specialty))
                {
                    title = specialty;
                }
            }

            plans.Add(new PlanCandidate(index + 1, title, picks));
        }

        return plans;
    }

    private static ScoredPlan ScorePlan(
        PlanCandidate plan,
        IReadOnlyDictionary<int, HeroMatchupStat> matchupRates)
    {
        var heroScores = plan.Picks
            .Where(pick => matchupRates.TryGetValue(pick.Id, out var matchup) && matchup.GamesPlayed >= 20)
            .Select(pick =>
            {
                var matchup = matchupRates[pick.Id];
                return new HeroScore(pick.Name, 100 - matchup.SelectedHeroWinRate);
            })
            .ToList();
        var score = heroScores.Count > 0 ? heroScores.Average(value => value.WinRate) : 0;
        return new ScoredPlan(plan, score, heroScores);
    }

    private static string BuildWhy(
        string metaHero,
        double score,
        IReadOnlyList<HeroScore> strongest,
        bool isThai)
    {
        var heroText = string.Join(", ", strongest.Select(hero => $"{hero.Name} {hero.WinRate:0.0}%"));
        return isThai
            ? $"ค่าเฉลี่ย matchup ของแผนต่อ {metaHero} อยู่ที่ {score:0.0}% • ตัวเด่น {heroText}"
            : $"Plan matchup average vs {metaHero}: {score:0.0}% • strongest: {heroText}";
    }

    private static string BuildAdjustment(string metaHero, string? strongestHero, bool isThai)
    {
        var hero = string.IsNullOrWhiteSpace(strongestHero) ? "ตัวที่ได้เปรียบ" : strongestHero;
        return isThai
            ? $"ให้ {hero} รับจังหวะปะทะกับ {metaHero} และเล่นตาม win condition ของแผน"
            : $"Let {hero} take the {metaHero} matchup and preserve the plan's win condition.";
    }

    private static HeroDirectoryEntry? FindHero(IReadOnlyList<HeroDirectoryEntry> catalog, string key)
    {
        var normalized = Normalize(key);
        return catalog.FirstOrDefault(hero => Normalize(hero.Key) == normalized || Normalize(hero.Name) == normalized);
    }

    private static HeroDirectoryEntry? FindHero(
        IReadOnlyDictionary<string, HeroDirectoryEntry> heroesByKey,
        IReadOnlyDictionary<string, HeroDirectoryEntry> heroesByName,
        string key,
        string name)
    {
        return heroesByKey.GetValueOrDefault(Normalize(key))
            ?? heroesByName.GetValueOrDefault(Normalize(name));
    }

    private static string LocalizeRole(string role, bool isThai)
    {
        if (!isThai)
        {
            return role;
        }

        return role.ToLowerInvariant() switch
        {
            "carry" => "แคร์รี่",
            "mid" => "มิด",
            "support" => "ซัพพอร์ต",
            _ => role
        };
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() ?? string.Empty : string.Empty;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private sealed record PlanCandidate(int Number, string Title, IReadOnlyList<PlanPick> Picks);
    private sealed record PlanPick(int Id, string Key, string Name, string Role);
    private sealed record HeroScore(string Name, double WinRate);
    private sealed record ScoredPlan(PlanCandidate Plan, double Score, IReadOnlyList<HeroScore> HeroScores);
}
