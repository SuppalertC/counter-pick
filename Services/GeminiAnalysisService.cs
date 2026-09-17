using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class GeminiAnalysisService
{
    private const string SchemaVersion = "v10-three-fixed-schemas";
    private const string StrategyPrompt = """
        You are a current-patch Dota 2 drafting coach. Use only the supplied patch context and lineup. Return clear,
        compact prose in responseLanguage using the exact JSON schema. Thai means natural Thai explanations while
        canonical hero, item, and ability names remain English. Return four short counter callouts, three critical
        rules, and three replacement records with three alternatives each. Explain who does what in lane, teamfight,
        pickoff, and a losing game. Never invent win rates, patch changes, or numeric guarantees.
        """;
    private const string BuildPrompt = """
        You are a current-patch Dota 2 item coach. Use only supplied item-popularity and patch context. Return exact
        JSON in responseLanguage. Return three hero builds in lineup order. Each hero has exactly two practical
        options, each option has three realistic timing steps and six final items. Use only supplied itemKey values.
        Keep canonical hero/item names in English and explanations in the requested language. Keep each goal,
        reason, and critical rule under 80 Thai characters or 16 English words. Never invent stats.
        """;
    private const string RolePrompt = """
        You are a current-patch Dota 2 execution coach. Return exact JSON in responseLanguage for positions 1, 2,
        and 5 in lineup order. Each hero needs six chronological timeline steps, five farmRoute strings formatted
        "minute | camp or wave | action | leave when ...", and four decisionChecks strings formatted
        "check ... | go when ... | abort when ...". Name useful wave/camp types without inventing fixed Radiant/Dire
        geometry. Position 5 prioritizes pulls, stacks, wards, camp blocks, rotations, body positioning, and core
        protection rather than taking core farm. Cover ahead/behind play, triangle timing, levels, and safe farm.
        Keep every timeline action, objective, farm route, decision check, and plan field under 120 Thai characters
        or 24 English words.
        """;

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly GeminiConfig _config;
    private readonly DotaPatchContextService _patchContextService = new();

    public GeminiAnalysisService(GeminiConfig config)
    {
        _config = config;
    }

    public async Task<ComboAnalysis> AnalyzeAsync(LineupAnalysisRequest lineup, string responseLanguage, bool forceRefresh = false)
    {
        var patchContext = await _patchContextService.GetAsync(lineup, forceRefresh);
        var cachePath = GetCachePath(lineup, patchContext.PatchNumber, responseLanguage);
        if (!forceRefresh && File.Exists(cachePath))
        {
            var cachedJson = await File.ReadAllTextAsync(cachePath);
            var cached = JsonSerializer.Deserialize<ComboAnalysis>(cachedJson, JsonOptions);
            if (cached is not null)
            {
                return cached;
            }
        }

        var apiKey = GeminiApiKeyService.Resolve(_config)
            ?? throw new InvalidOperationException($"{_config.ApiKeyVariable} was not found in the environment or configured .env file.");

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var analysis = await GetComponentAsync(
            GetComponentCachePath(cachePath, "strategy"),
            forceRefresh,
            () => SendRequestAsync(apiKey, CreateRequestBody(lineup, patchContext, responseLanguage)),
            ParseResponse);
        var builds = await GetComponentAsync(
            GetComponentCachePath(cachePath, "builds"),
            forceRefresh,
            () => SendRequestAsync(apiKey, CreateBuildRequestBody(lineup, patchContext, responseLanguage)),
            ParseBuildResponse);
        var roles = await GetComponentAsync(
            GetComponentCachePath(cachePath, "roles"),
            forceRefresh,
            () => SendRequestAsync(apiKey, CreateRoleExecutionRequestBody(lineup, patchContext, responseLanguage), true),
            ParseRoleExecutionResponse);
        analysis.HeroBuilds = builds.HeroBuilds;
        analysis.RolePlans = roles.RolePlans;
        analysis.PatchNumber = patchContext.PatchNumber;
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(analysis, JsonOptions));
        return analysis;
    }

    private static async Task<T> GetComponentAsync<T>(
        string cachePath,
        bool forceRefresh,
        Func<Task<string>> request,
        Func<string, T> parseResponse)
    {
        if (!forceRefresh && File.Exists(cachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(cachePath), JsonOptions);
                if (cached is not null)
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
            }
        }

        var result = parseResponse(await request());
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private async Task<string> SendRequestAsync(string apiKey, object requestBody, bool preferLite = false)
    {
        var payload = JsonSerializer.Serialize(requestBody);
        var models = (preferLite
                ? new[] { "gemini-3.5-flash-lite", _config.Model }
                : new[] { _config.Model, "gemini-3.5-flash-lite" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string? lastError = null;

        for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
        {
            var model = models[modelIndex];
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
            const int maxAttempts = 4;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.Add("x-goog-api-key", apiKey);
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    using var response = await HttpClient.SendAsync(request);
                    var responseJson = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return responseJson;
                    }

                    lastError = GetApiError(responseJson, (int)response.StatusCode);
                    var canUseFallback = modelIndex < models.Length - 1
                        && (IsTransient(response.StatusCode) || response.StatusCode == HttpStatusCode.NotFound);
                    if (!IsTransient(response.StatusCode) || attempt == maxAttempts - 1)
                    {
                        if (canUseFallback)
                        {
                            break;
                        }

                        throw new InvalidOperationException(lastError);
                    }

                    var retryDelay = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Min(20, 3 * Math.Pow(2, attempt)));
                    await Task.Delay(retryDelay);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastError = $"Gemini request timed out for {model}: {exception.Message}";
                    if (attempt == maxAttempts - 1)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(20, 3 * Math.Pow(2, attempt))));
                }
            }
        }

        throw new InvalidOperationException(lastError ?? "Gemini request failed after automatic retries.");
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.InternalServerError
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static object CreateRequestBody(LineupAnalysisRequest lineup, DotaPatchContext patchContext, string responseLanguage)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            latestPatch = patchContext.PatchNumber,
            retrievedAt = patchContext.RetrievedAt,
            responseLanguage,
            lineup = lineup.Picks.Select(pick => new { pick.Hero, pick.Name, pick.Role }),
            intendedSpecialty = lineup.Specialty,
            intendedWinCondition = lineup.WinCondition,
            currentItemPopularity = patchContext.Heroes,
            relevantOfficialPatchNotes = JsonSerializer.Deserialize<JsonElement>(patchContext.RelevantPatchNotesJson)
        });

        return new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = StrategyPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = $"Analyze this validated current-patch input JSON:\n{inputJson}" } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = CreateResponseSchema(),
                maxOutputTokens = 3000
            }
        };
    }

    private static object CreateBuildRequestBody(
        LineupAnalysisRequest lineup,
        DotaPatchContext patchContext,
        string responseLanguage)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            latestPatch = patchContext.PatchNumber,
            responseLanguage,
            lineup = lineup.Picks.Select(pick => new { pick.Hero, pick.Name, pick.Role }),
            currentItemPopularity = patchContext.Heroes,
            relevantOfficialPatchNotes = JsonSerializer.Deserialize<JsonElement>(patchContext.RelevantPatchNotesJson)
        });
        return CreateStructuredRequest(BuildPrompt, $"Build items for this input JSON:\n{inputJson}", CreateBuildSchema(), 8000);
    }

    private static object CreateRoleExecutionRequestBody(
        LineupAnalysisRequest lineup,
        DotaPatchContext patchContext,
        string responseLanguage)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            latestPatch = patchContext.PatchNumber,
            responseLanguage,
            lineup = lineup.Picks.Select(pick => new { pick.Hero, pick.Name, pick.Role }),
            lineup.Specialty,
            lineup.WinCondition
        });
        return CreateStructuredRequest(RolePrompt, $"Build role execution for this input JSON:\n{inputJson}", CreateRoleExecutionSchema(), 8500);
    }

    private static object CreateStructuredRequest(string systemPrompt, string userPrompt, object schema, int maxOutputTokens)
    {
        return new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = schema,
                maxOutputTokens
            }
        };
    }

    private static object CreateResponseSchema()
    {
        var replacementOption = new
        {
            type = "OBJECT",
            properties = new { hero = new { type = "STRING" }, reason = new { type = "STRING" } },
            required = new[] { "hero", "reason" }
        };
        return new
        {
            type = "OBJECT",
            properties = new
            {
                overview = new { type = "STRING" },
                phasePlan = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        lanePhase = new { type = "STRING" }, teamFight = new { type = "STRING" },
                        pickOff = new { type = "STRING" }, losingGame = new { type = "STRING" }
                    },
                    required = new[] { "lanePhase", "teamFight", "pickOff", "losingGame" }
                },
                counterHeroes = new
                {
                    type = "ARRAY", minItems = 4, maxItems = 4,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            hero = new { type = "STRING" }, threat = new { type = "STRING" }, response = new { type = "STRING" }
                        },
                        required = new[] { "hero", "threat", "response" }
                    }
                },
                criticalStages = new
                {
                    type = "ARRAY", minItems = 3, maxItems = 3,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            owner = new { type = "STRING" }, rule = new { type = "STRING" }, failureImpact = new { type = "STRING" }
                        },
                        required = new[] { "owner", "rule", "failureImpact" }
                    }
                },
                replacements = new
                {
                    type = "ARRAY", minItems = 3, maxItems = 3,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            originalHero = new { type = "STRING" },
                            alternatives = new { type = "ARRAY", minItems = 3, maxItems = 3, items = replacementOption }
                        },
                        required = new[] { "originalHero", "alternatives" }
                    }
                }
            },
            required = new[] { "overview", "phasePlan", "counterHeroes", "criticalStages", "replacements" }
        };
    }

    private static object CreateBuildSchema()
    {
        var item = new
        {
            type = "OBJECT",
            properties = new { itemKey = new { type = "STRING" }, itemName = new { type = "STRING" } },
            required = new[] { "itemKey", "itemName" }
        };
        var timing = new
        {
            type = "OBJECT",
            properties = new
            {
                timing = new { type = "STRING" }, itemKey = new { type = "STRING" },
                itemName = new { type = "STRING" }, reason = new { type = "STRING" }
            },
            required = new[] { "timing", "itemKey", "itemName", "reason" }
        };
        var option = new
        {
            type = "OBJECT",
            properties = new
            {
                name = new { type = "STRING" }, goal = new { type = "STRING" },
                timings = new { type = "ARRAY", minItems = 3, maxItems = 3, items = timing },
                finalItems = new { type = "ARRAY", minItems = 6, maxItems = 6, items = item }
            },
            required = new[] { "name", "goal", "timings", "finalItems" }
        };
        return new
        {
            type = "OBJECT",
            properties = new
            {
                heroBuilds = new
                {
                    type = "ARRAY", minItems = 3, maxItems = 3,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            hero = new { type = "STRING" }, role = new { type = "STRING" },
                            keyItem = new { type = "STRING" }, criticalRule = new { type = "STRING" },
                            options = new { type = "ARRAY", minItems = 2, maxItems = 2, items = option }
                        },
                        required = new[] { "hero", "role", "keyItem", "criticalRule", "options" }
                    }
                }
            },
            required = new[] { "heroBuilds" }
        };
    }

    private static object CreateRoleExecutionSchema()
    {
        var timelineStep = new
        {
            type = "OBJECT",
            properties = new
            {
                minute = new { type = "STRING" }, action = new { type = "STRING" }, objective = new { type = "STRING" }
            },
            required = new[] { "minute", "action", "objective" }
        };
        return new
        {
            type = "OBJECT",
            properties = new
            {
                rolePlans = new
                {
                    type = "ARRAY", minItems = 3, maxItems = 3,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            hero = new { type = "STRING" }, position = new { type = "STRING" }, lanePositioning = new { type = "STRING" },
                            timeline = new { type = "ARRAY", minItems = 6, maxItems = 6, items = timelineStep },
                            farmRoute = new { type = "ARRAY", minItems = 5, maxItems = 5, items = new { type = "STRING" } },
                            decisionChecks = new { type = "ARRAY", minItems = 4, maxItems = 4, items = new { type = "STRING" } },
                            mapMovement = new { type = "STRING" }, safeFarm = new { type = "STRING" },
                            aheadPlan = new { type = "STRING" }, behindPlan = new { type = "STRING" },
                            triangleTiming = new { type = "STRING" }, levelTiming = new { type = "STRING" }
                        },
                        required = new[]
                        {
                            "hero", "position", "lanePositioning", "timeline", "farmRoute", "decisionChecks",
                            "mapMovement", "safeFarm", "aheadPlan", "behindPlan", "triangleTiming", "levelTiming"
                        }
                    }
                }
            },
            required = new[] { "rolePlans" }
        };
    }

    private static ComboAnalysis ParseResponse(string responseJson)
    {
        return JsonSerializer.Deserialize<ComboAnalysis>(ExtractResponseText(responseJson), JsonOptions)
            ?? throw new InvalidOperationException("Gemini returned invalid analysis JSON.");
    }

    private static BuildResponse ParseBuildResponse(string responseJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildResponse>(ExtractResponseText(responseJson), JsonOptions)
                ?? throw new InvalidOperationException("Gemini returned empty build JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Gemini returned incomplete build JSON.", exception);
        }
    }

    private static RoleExecutionResponse ParseRoleExecutionResponse(string responseJson)
    {
        try
        {
            return JsonSerializer.Deserialize<RoleExecutionResponse>(ExtractResponseText(responseJson), JsonOptions)
                ?? throw new InvalidOperationException("Gemini returned empty role execution JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Gemini returned incomplete role execution JSON.", exception);
        }
    }

    private static string ExtractResponseText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var textElement))
        {
            throw new InvalidOperationException("Gemini returned an empty response.");
        }

        return textElement.GetString() ?? string.Empty;
    }

    private static string GetApiError(string responseJson, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
            return $"Gemini request failed ({statusCode}): {message}";
        }
        catch
        {
            return $"Gemini request failed ({statusCode}).";
        }
    }

    private string GetCachePath(LineupAnalysisRequest lineup, string patchNumber, string responseLanguage)
    {
        var cacheKey = $"{SchemaVersion}|{_config.Model}|{patchNumber}|{responseLanguage}|{string.Join('|', lineup.Picks.Select(pick => pick.Hero))}|{lineup.Specialty}|{lineup.WinCondition}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotaComboBoard",
            "analysis-cache",
            $"{hash}.json");
    }

    private static string GetComponentCachePath(string finalCachePath, string component)
    {
        return Path.Combine(
            Path.GetDirectoryName(finalCachePath)!,
            $"{Path.GetFileNameWithoutExtension(finalCachePath)}.{component}.json");
    }

    private sealed class BuildResponse
    {
        public List<HeroBuildRecommendation> HeroBuilds { get; set; } = [];
    }

    private sealed class RoleExecutionResponse
    {
        public List<RoleExecutionPlan> RolePlans { get; set; } = [];
    }
}
