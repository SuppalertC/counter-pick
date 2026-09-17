using System.IO;
using System.Net.Http;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class HeroMetaService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim CatalogGate = new(1, 1);
    private static IReadOnlyList<HeroDirectoryEntry>? _catalog;
    private static DateTimeOffset _catalogLoadedAt;
    private readonly string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotaComboBoard",
        "meta-cache");

    public async Task<IReadOnlyList<HeroDirectoryEntry>> GetCatalogAsync(bool forceRefresh = false)
    {
        await CatalogGate.WaitAsync();
        try
        {
            if (!forceRefresh && _catalog is not null && DateTimeOffset.Now - _catalogLoadedAt < TimeSpan.FromHours(6))
            {
                return _catalog;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var cachePath = Path.Combine(_cacheDirectory, "hero-stats.json");
            string json;
            if (!forceRefresh
                && File.Exists(cachePath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(6))
            {
                json = await File.ReadAllTextAsync(cachePath);
            }
            else
            {
                try
                {
                    json = await GetStringWithRetryAsync("https://api.opendota.com/api/heroStats");
                    await File.WriteAllTextAsync(cachePath, json);
                }
                catch (InvalidOperationException) when (File.Exists(cachePath))
                {
                    json = await File.ReadAllTextAsync(cachePath);
                }
            }

            _catalog = ParseCatalog(json);
            _catalogLoadedAt = DateTimeOffset.Now;
            return _catalog;
        }
        finally
        {
            CatalogGate.Release();
        }
    }

    public async Task<HeroMatchupResult> GetMatchupsAsync(HeroDirectoryEntry selectedHero, bool forceRefresh = false)
    {
        var catalog = await GetCatalogAsync();
        var rawMatchups = await GetMatchupRatesAsync(selectedHero, forceRefresh);
        var byId = catalog.ToDictionary(hero => hero.Id);
        var matchups = rawMatchups.Values
            .Where(matchup => matchup.GamesPlayed >= 20 && byId.ContainsKey(matchup.HeroId))
            .Select(matchup =>
            {
                var hero = byId[matchup.HeroId];
                return new HeroMatchupDisplay
                {
                    HeroId = matchup.HeroId,
                    Hero = hero.Name,
                    GamesPlayed = matchup.GamesPlayed,
                    SelectedHeroWinRate = matchup.SelectedHeroWinRate,
                    IconPath = hero.IconPath
                };
            })
            .ToList();

        var counters = matchups.OrderBy(matchup => matchup.SelectedHeroWinRate).Take(8).ToList();
        var advantages = matchups.OrderByDescending(matchup => matchup.SelectedHeroWinRate).Take(8).ToList();
        return new HeroMatchupResult(selectedHero, counters, advantages);
    }

    public async Task<IReadOnlyDictionary<int, HeroMatchupStat>> GetMatchupRatesAsync(
        HeroDirectoryEntry selectedHero,
        bool forceRefresh = false)
    {
        var cachePath = Path.Combine(_cacheDirectory, $"matchups-{selectedHero.Id}.json");
        string json;
        if (!forceRefresh
            && File.Exists(cachePath)
            && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(6))
        {
            json = await File.ReadAllTextAsync(cachePath);
        }
        else
        {
            Directory.CreateDirectory(_cacheDirectory);
            try
            {
                json = await GetStringWithRetryAsync($"https://api.opendota.com/api/heroes/{selectedHero.Id}/matchups");
                await File.WriteAllTextAsync(cachePath, json);
            }
            catch (InvalidOperationException) when (File.Exists(cachePath))
            {
                json = await File.ReadAllTextAsync(cachePath);
            }
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(element => new HeroMatchupStat(
                element.GetProperty("hero_id").GetInt32(),
                element.GetProperty("games_played").GetInt32(),
                GetWinRate(element)))
            .Where(matchup => matchup.GamesPlayed > 0)
            .ToDictionary(matchup => matchup.HeroId);
    }

    private static double GetWinRate(JsonElement element)
    {
        var games = element.GetProperty("games_played").GetInt32();
        var wins = element.GetProperty("wins").GetInt32();
        return games > 0 ? (double)wins / games * 100 : 0;
    }

    private static IReadOnlyList<HeroDirectoryEntry> ParseCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var heroes = document.RootElement.EnumerateArray()
            .Select(element =>
            {
                var key = element.GetProperty("name").GetString()?.Replace("npc_dota_hero_", string.Empty) ?? string.Empty;
                return new HeroDirectoryEntry
                {
                    Id = element.GetProperty("id").GetInt32(),
                    Key = key,
                    Name = element.GetProperty("localized_name").GetString() ?? key,
                    PrimaryAttribute = element.GetProperty("primary_attr").GetString() ?? string.Empty,
                    AttackType = element.GetProperty("attack_type").GetString() ?? string.Empty,
                    Roles = element.GetProperty("roles").EnumerateArray().Select(role => role.GetString() ?? string.Empty).ToList(),
                    PublicPicks = element.TryGetProperty("pub_pick", out var picks) ? picks.GetInt64() : 0,
                    PublicWins = element.TryGetProperty("pub_win", out var wins) ? wins.GetInt64() : 0,
                    IconPath = AppPaths.Resolve(Path.Combine("assets", "heroes", $"{key}.png"))
                };
            })
            .OrderBy(hero => hero.Name)
            .ToList();
        var totalPicks = Math.Max(1, heroes.Sum(hero => hero.PublicPicks));
        foreach (var hero in heroes)
        {
            hero.PickRate = (double)hero.PublicPicks / totalPicks * 100;
        }

        return heroes;
    }

    private static async Task<string> GetStringWithRetryAsync(string uri)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await HttpClient.GetStringAsync(uri);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
                }
            }
        }

        throw new InvalidOperationException("Could not retrieve current hero meta data.", lastException);
    }
}
