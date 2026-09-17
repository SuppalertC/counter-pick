using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class DotaPatchContextService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim BaseContextGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, HeroPatchContext> HeroCache = new();
    private static BaseContext? _baseContext;

    public async Task<string> GetCurrentPatchNumberAsync(bool forceRefresh = false)
    {
        return (await GetBaseContextAsync(forceRefresh)).PatchNumber;
    }

    public async Task<DotaPatchContext> GetAsync(LineupAnalysisRequest lineup, bool forceRefresh)
    {
        var baseContext = await GetBaseContextAsync(forceRefresh);
        var heroes = await Task.WhenAll(lineup.Picks.Select(pick => GetHeroContextAsync(pick, baseContext, forceRefresh)));
        var heroIds = heroes.Select(hero => hero.HeroId).ToHashSet();
        var itemIds = heroes
            .SelectMany(hero => hero.PopularItems.Values)
            .SelectMany(items => items)
            .Select(item => item.ItemId)
            .ToHashSet();

        return new DotaPatchContext(
            baseContext.PatchNumber,
            DateTimeOffset.Now,
            heroes,
            BuildRelevantPatchNotes(baseContext.PatchNotesJson, heroIds, itemIds));
    }

    private static async Task<BaseContext> GetBaseContextAsync(bool forceRefresh)
    {
        await BaseContextGate.WaitAsync();
        try
        {
            if (!forceRefresh
                && _baseContext is not null
                && DateTimeOffset.Now - _baseContext.RetrievedAt < TimeSpan.FromMinutes(5))
            {
                return _baseContext;
            }

            var patchListTask = GetStringWithRetryAsync("https://www.dota2.com/datafeed/patchnoteslist?language=english");
            var itemCatalogTask = GetStringWithRetryAsync("https://api.opendota.com/api/constants/items");
            await Task.WhenAll(patchListTask, itemCatalogTask);

            using var patchListDocument = JsonDocument.Parse(await patchListTask);
            var patches = patchListDocument.RootElement.GetProperty("patches");
            var patchNumber = patches[patches.GetArrayLength() - 1].GetProperty("patch_number").GetString()
                ?? throw new InvalidOperationException("Valve returned an invalid patch number.");
            var patchNotesJson = await GetStringWithRetryAsync(
                $"https://www.dota2.com/datafeed/patchnotes?version={Uri.EscapeDataString(patchNumber)}&language=english");
            var heroMetadata = await LoadHeroMetadataAsync();
            var itemCatalog = ParseItemCatalog(await itemCatalogTask);

            if (_baseContext is null || !_baseContext.PatchNumber.Equals(patchNumber, StringComparison.Ordinal))
            {
                HeroCache.Clear();
            }

            _baseContext = new BaseContext(
                patchNumber,
                DateTimeOffset.Now,
                patchNotesJson,
                heroMetadata,
                itemCatalog);
            return _baseContext;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Could not retrieve the latest Dota 2 patch context. Check your connection and try again.", exception);
        }
        finally
        {
            BaseContextGate.Release();
        }
    }

    private static async Task<HeroPatchContext> GetHeroContextAsync(
        LineupPick pick,
        BaseContext baseContext,
        bool forceRefresh)
    {
        var cacheKey = $"{baseContext.PatchNumber}:{pick.Hero}";
        if (!forceRefresh && HeroCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (!baseContext.HeroMetadata.TryGetValue(pick.Hero, out var metadata))
        {
            throw new InvalidOperationException($"Hero metadata was not found for {pick.Name}.");
        }

        try
        {
            var popularityJson = await GetStringWithRetryAsync(
                $"https://api.opendota.com/api/heroes/{metadata.Id}/itemPopularity");
            var popularity = ParsePopularity(popularityJson, baseContext.ItemCatalog);
            var context = new HeroPatchContext(metadata.Id, metadata.Key, metadata.Name, popularity);
            HeroCache[cacheKey] = context;
            return context;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Could not retrieve current item data for {pick.Name}.", exception);
        }
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

        throw lastException ?? new HttpRequestException("Remote data request failed.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<PopularItem>> ParsePopularity(
        string json,
        IReadOnlyDictionary<int, ItemCatalogEntry> itemCatalog)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, IReadOnlyList<PopularItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in document.RootElement.EnumerateObject())
        {
            var items = group.Value.EnumerateObject()
                .Select(property => new
                {
                    Id = int.TryParse(property.Name, out var id) ? id : 0,
                    Score = property.Value.GetDouble()
                })
                .Where(item => item.Id > 0 && itemCatalog.ContainsKey(item.Id))
                .OrderByDescending(item => item.Score)
                .Take(8)
                .Select(item =>
                {
                    var catalogItem = itemCatalog[item.Id];
                    return new PopularItem(item.Id, catalogItem.Key, catalogItem.Name, item.Score);
                })
                .ToList();
            result[group.Name] = items;
        }

        return result;
    }

    private static Dictionary<int, ItemCatalogEntry> ParseItemCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<int, ItemCatalogEntry>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Value.TryGetProperty("id", out var idElement)
                || !property.Value.TryGetProperty("dname", out var nameElement))
            {
                continue;
            }

            result[idElement.GetInt32()] = new ItemCatalogEntry(
                property.Name,
                nameElement.GetString() ?? property.Name);
        }

        return result;
    }

    private static async Task<Dictionary<string, HeroMetadata>> LoadHeroMetadataAsync()
    {
        var path = AppPaths.Resolve(Path.Combine("assets", "heroes.json"));
        await using var stream = File.OpenRead(path);
        var heroes = await JsonSerializer.DeserializeAsync<List<HeroMetadata>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        return heroes.ToDictionary(hero => hero.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildRelevantPatchNotes(string patchNotesJson, HashSet<int> heroIds, HashSet<int> itemIds)
    {
        using var document = JsonDocument.Parse(patchNotesJson);
        var root = document.RootElement;
        var heroes = root.TryGetProperty("heroes", out var heroNotes)
            ? heroNotes.EnumerateArray()
                .Where(hero => hero.TryGetProperty("hero_id", out var id) && heroIds.Contains(id.GetInt32()))
                .Select(hero => hero.Clone())
                .ToList()
            : [];
        var items = root.TryGetProperty("items", out var itemNotes)
            ? itemNotes.EnumerateArray()
                .Where(item => item.TryGetProperty("ability_id", out var id) && itemIds.Contains(id.GetInt32()))
                .Select(item => item.Clone())
                .ToList()
            : [];
        return JsonSerializer.Serialize(new { heroes, items });
    }

    private sealed record BaseContext(
        string PatchNumber,
        DateTimeOffset RetrievedAt,
        string PatchNotesJson,
        IReadOnlyDictionary<string, HeroMetadata> HeroMetadata,
        IReadOnlyDictionary<int, ItemCatalogEntry> ItemCatalog);

    private sealed record ItemCatalogEntry(string Key, string Name);

    private sealed class HeroMetadata
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
