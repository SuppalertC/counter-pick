using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class DotaPatchContextService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim BaseContextGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, HeroPatchContext> HeroCache = new();
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotaComboBoard",
        "patch-context-cache");
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

            var patchListJson = await GetCachedRemoteAsync(
                "patch-list.json",
                "https://www.dota2.com/datafeed/patchnoteslist?language=english",
                forceRefresh,
                required: true,
                maxAttempts: 2);
            using var patchListDocument = JsonDocument.Parse(patchListJson);
            var patches = patchListDocument.RootElement.GetProperty("patches");
            var patchNumber = patches[patches.GetArrayLength() - 1].GetProperty("patch_number").GetString()
                ?? throw new InvalidOperationException("Valve returned an invalid patch number.");
            var patchNotesJson = await GetCachedRemoteAsync(
                $"patch-notes-{patchNumber}.json",
                $"https://www.dota2.com/datafeed/patchnotes?version={Uri.EscapeDataString(patchNumber)}&language=english",
                forceRefresh,
                required: false,
                maxAttempts: 2,
                fallbackJson: "{}");
            var itemCatalogJson = await GetCachedRemoteAsync(
                "item-catalog.json",
                "https://api.opendota.com/api/constants/items",
                forceRefresh,
                required: false,
                maxAttempts: 1,
                fallbackJson: "{}");
            var heroMetadata = await LoadHeroMetadataAsync();
            var itemCatalog = ParseItemCatalog(itemCatalogJson);

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

        if (baseContext.ItemCatalog.Count == 0)
        {
            var offlineContext = new HeroPatchContext(
                metadata.Id,
                metadata.Key,
                metadata.Name,
                new Dictionary<string, IReadOnlyList<PopularItem>>());
            HeroCache[cacheKey] = offlineContext;
            return offlineContext;
        }

        try
        {
            var popularityJson = await GetCachedRemoteAsync(
                $"item-popularity-{metadata.Id}.json",
                $"https://api.opendota.com/api/heroes/{metadata.Id}/itemPopularity",
                forceRefresh,
                required: false,
                maxAttempts: 1,
                fallbackJson: "{}");
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

    private static async Task<string> GetCachedRemoteAsync(
        string cacheFileName,
        string uri,
        bool forceRefresh,
        bool required,
        int maxAttempts,
        string? fallbackJson = null)
    {
        Directory.CreateDirectory(CacheDirectory);
        var cachePath = Path.Combine(CacheDirectory, cacheFileName);
        if (!forceRefresh && File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath);
        }

        try
        {
            var json = await GetStringWithRetryAsync(uri, maxAttempts, required ? 12 : 4);
            await File.WriteAllTextAsync(cachePath, json);
            return json;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (File.Exists(cachePath))
            {
                return await File.ReadAllTextAsync(cachePath);
            }

            if (!required && fallbackJson is not null)
            {
                await File.WriteAllTextAsync(cachePath, fallbackJson);
                return fallbackJson;
            }

            throw;
        }
    }

    private static async Task<string> GetStringWithRetryAsync(string uri, int maxAttempts, int timeoutSeconds)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                return await HttpClient.GetStringAsync(uri, timeout.Token);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                if (attempt < maxAttempts - 1)
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
