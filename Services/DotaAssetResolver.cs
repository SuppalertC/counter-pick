using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed partial class DotaAssetResolver
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<string, HeroMetadata> _heroes;
    private readonly string _heroDirectory = Path.GetDirectoryName(
        AppPaths.Resolve(Path.Combine("assets", "heroes", "axe.png")))!;
    private readonly string _itemDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotaComboBoard",
        "item-icons");

    public DotaAssetResolver()
    {
        _heroes = LoadHeroes();
    }

    public string ResolveHeroPath(string nameOrKey)
    {
        var directPath = Path.Combine(_heroDirectory, $"{nameOrKey}.png");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        return _heroes.TryGetValue(Normalize(nameOrKey), out var hero)
            ? Path.Combine(_heroDirectory, $"{hero.Key}.png")
            : string.Empty;
    }

    public string ResolveHeroName(string nameOrKey)
    {
        return _heroes.TryGetValue(Normalize(nameOrKey), out var hero) ? hero.Name : nameOrKey;
    }

    public async Task PopulateAnalysisAssetsAsync(ComboAnalysis analysis, IReadOnlyList<LineupPick> lineup)
    {
        foreach (var pick in lineup)
        {
            pick.IconPath = ResolveHeroPath(pick.Hero);
        }

        foreach (var heroBuild in analysis.HeroBuilds)
        {
            heroBuild.IconPath = ResolveHeroPath(heroBuild.Hero);
            heroBuild.Hero = ResolveHeroName(heroBuild.Hero);
            foreach (var option in heroBuild.Options)
            {
                foreach (var timing in option.Timings)
                {
                    timing.IconPath = await EnsureItemIconAsync(timing.ItemKey);
                }

                foreach (var item in option.FinalItems)
                {
                    item.IconPath = await EnsureItemIconAsync(item.ItemKey);
                }
            }

            var keyItem = heroBuild.Options
                .SelectMany(option => option.Timings.Cast<object>().Concat(option.FinalItems))
                .Select(value => value switch
                {
                    ItemTiming timing => new { timing.ItemKey, timing.ItemName },
                    BuildItem item => new { item.ItemKey, item.ItemName },
                    _ => null
                })
                .FirstOrDefault(item => item is not null
                    && item.ItemKey.Equals(heroBuild.KeyItem, StringComparison.OrdinalIgnoreCase));
            if (keyItem is not null)
            {
                heroBuild.KeyItem = keyItem.ItemName;
            }
        }

        foreach (var counter in analysis.CounterHeroes)
        {
            counter.IconPath = ResolveHeroPath(counter.Hero);
            counter.Hero = ResolveHeroName(counter.Hero);
        }

        foreach (var criticalStage in analysis.CriticalStages)
        {
            criticalStage.Owner = ResolveHeroName(criticalStage.Owner);
        }

        foreach (var rolePlan in analysis.RolePlans)
        {
            rolePlan.IconPath = ResolveHeroPath(rolePlan.Hero);
            rolePlan.Hero = ResolveHeroName(rolePlan.Hero);
            rolePlan.MapImagePath = AppPaths.Resolve(Path.Combine("assets", "map", "dota-map-current.png"));
            rolePlan.Build = analysis.HeroBuilds.FirstOrDefault(build =>
                build.Hero.Equals(rolePlan.Hero, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var replacement in analysis.Replacements)
        {
            replacement.OriginalHeroIconPath = ResolveHeroPath(replacement.OriginalHero);
            replacement.OriginalHero = ResolveHeroName(replacement.OriginalHero);
            foreach (var alternative in replacement.Alternatives)
            {
                alternative.IconPath = ResolveHeroPath(alternative.Hero);
                alternative.Hero = ResolveHeroName(alternative.Hero);
            }
        }
    }

    public async Task PopulateOverviewAssetsAsync(ComboAnalysis analysis, IReadOnlyList<LineupPick> lineup)
    {
        foreach (var pick in lineup)
        {
            pick.IconPath = ResolveHeroPath(pick.Hero);
        }

        var itemIcons = await EnsureItemIconsAsync(analysis.HeroItems
            .SelectMany(heroItems => heroItems.Items)
            .Select(item => item.ItemKey));
        foreach (var heroItems in analysis.HeroItems)
        {
            heroItems.IconPath = ResolveHeroPath(heroItems.Hero);
            heroItems.Hero = ResolveHeroName(heroItems.Hero);
            foreach (var item in heroItems.Items)
            {
                item.IconPath = itemIcons.GetValueOrDefault(item.ItemKey, string.Empty);
            }
        }

        foreach (var draftStep in analysis.DraftOrder)
        {
            draftStep.IconPath = ResolveHeroPath(draftStep.Hero);
            draftStep.Hero = ResolveHeroName(draftStep.Hero);
            draftStep.EnemyCounterIconPath = ResolveHeroPath(draftStep.EnemyCounter);
            draftStep.EnemyCounter = ResolveHeroName(draftStep.EnemyCounter);
            draftStep.ResponseHeroIconPath = ResolveHeroPath(draftStep.ResponseHero);
            draftStep.ResponseHero = ResolveHeroName(draftStep.ResponseHero);
        }

        foreach (var counter in analysis.CounterHeroes)
        {
            counter.IconPath = ResolveHeroPath(counter.Hero);
            counter.Hero = ResolveHeroName(counter.Hero);
        }

        foreach (var criticalStage in analysis.CriticalStages)
        {
            criticalStage.Owner = ResolveHeroName(criticalStage.Owner);
        }

        foreach (var replacement in analysis.Replacements)
        {
            replacement.OriginalHeroIconPath = ResolveHeroPath(replacement.OriginalHero);
            replacement.OriginalHero = ResolveHeroName(replacement.OriginalHero);
            foreach (var alternative in replacement.Alternatives)
            {
                alternative.IconPath = ResolveHeroPath(alternative.Hero);
                alternative.Hero = ResolveHeroName(alternative.Hero);
            }
        }
    }

    public async Task PopulateHeroTabAssetsAsync(HeroTabAnalysis analysis)
    {
        var build = analysis.Build;
        build.IconPath = ResolveHeroPath(build.Hero);
        build.Hero = ResolveHeroName(build.Hero);
        var itemIcons = await EnsureItemIconsAsync(build.Options
            .SelectMany(option => option.Timings.Select(timing => timing.ItemKey)
                .Concat(option.FinalItems.Select(item => item.ItemKey))));
        foreach (var option in build.Options)
        {
            foreach (var timing in option.Timings)
            {
                timing.IconPath = itemIcons.GetValueOrDefault(timing.ItemKey, string.Empty);
            }

            foreach (var item in option.FinalItems)
            {
                item.IconPath = itemIcons.GetValueOrDefault(item.ItemKey, string.Empty);
            }
        }

        var keyItem = build.Options
            .SelectMany(option => option.Timings.Cast<object>().Concat(option.FinalItems))
            .Select(value => value switch
            {
                ItemTiming timing => new { timing.ItemKey, timing.ItemName },
                BuildItem item => new { item.ItemKey, item.ItemName },
                _ => null
            })
            .FirstOrDefault(item => item is not null
                && item.ItemKey.Equals(build.KeyItem, StringComparison.OrdinalIgnoreCase));
        if (keyItem is not null)
        {
            build.KeyItem = keyItem.ItemName;
        }

        analysis.RolePlan.IconPath = ResolveHeroPath(analysis.RolePlan.Hero);
        analysis.RolePlan.Hero = ResolveHeroName(analysis.RolePlan.Hero);
        analysis.RolePlan.MapImagePath = AppPaths.Resolve(Path.Combine("assets", "map", "dota-map-current.png"));
        analysis.RolePlan.Build = build;
    }

    private async Task<IReadOnlyDictionary<string, string>> EnsureItemIconsAsync(IEnumerable<string> itemKeys)
    {
        var uniqueKeys = itemKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var iconTasks = uniqueKeys.ToDictionary(
            key => key,
            EnsureItemIconAsync,
            StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(iconTasks.Values);
        return iconTasks.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Result,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> EnsureItemIconAsync(string itemKey)
    {
        var assetKey = itemKey.ToLowerInvariant() switch
        {
            "daedalus" => "greater_crit",
            "aghanims_scepter" => "ultimate_scepter",
            _ => itemKey
        };
        if (string.IsNullOrWhiteSpace(assetKey) || !SafeAssetKey().IsMatch(assetKey))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(_itemDirectory);
        var path = Path.Combine(_itemDirectory, $"{assetKey}.png");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            var uri = $"https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/{assetKey}.png";
            var bytes = await HttpClient.GetByteArrayAsync(uri);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, HeroMetadata> LoadHeroes()
    {
        var path = AppPaths.Resolve(Path.Combine("assets", "heroes.json"));
        using var stream = File.OpenRead(path);
        var heroes = JsonSerializer.Deserialize<List<HeroMetadata>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var result = new Dictionary<string, HeroMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var hero in heroes)
        {
            result[Normalize(hero.Name)] = hero;
            result[Normalize(hero.Key)] = hero;
        }

        return result;
    }

    private static string Normalize(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized switch
        {
            "shaker" => "earthshaker",
            "venge" => "vengefulspirit",
            "od" => "outworlddestroyer",
            _ => normalized
        };
    }

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeAssetKey();

    private sealed class HeroMetadata
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
