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

    private async Task<string> EnsureItemIconAsync(string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemKey) || !SafeAssetKey().IsMatch(itemKey))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(_itemDirectory);
        var path = Path.Combine(_itemDirectory, $"{itemKey}.png");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            var uri = $"https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/{itemKey}.png";
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
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeAssetKey();

    private sealed class HeroMetadata
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
