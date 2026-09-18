using System.Net.Http;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public static class GeminiApiKeyService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string? Resolve(GeminiConfig config)
    {
        var savedKey = Normalize(AppSettingsService.Load().GeminiApiKey);
        if (!string.IsNullOrWhiteSpace(savedKey))
        {
            return savedKey;
        }

        var fallbackKey = Normalize(Environment.GetEnvironmentVariable(config.ApiKeyVariable)
            ?? EnvFile.ReadValue(config.EnvPath, config.ApiKeyVariable));
        return string.IsNullOrWhiteSpace(fallbackKey) ? null : fallbackKey;
    }

    public static string Normalize(string? apiKey)
    {
        var normalized = apiKey?.Trim() ?? string.Empty;
        if (normalized.StartsWith("GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = normalized.IndexOf('=');
            if (separatorIndex >= 0)
            {
                normalized = normalized[(separatorIndex + 1)..].Trim();
            }
        }

        if (normalized.Length >= 2
            && ((normalized[0] == '"' && normalized[^1] == '"')
                || (normalized[0] == '\'' && normalized[^1] == '\'')))
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    public static async Task<GeminiKeyTestResult> TestAsync(string apiKey, string preferredModel)
    {
        var normalizedKey = Normalize(apiKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new InvalidOperationException("Gemini API key is empty.");
        }

        var endpoint = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("x-goog-api-key", normalizedKey);
        using var response = await HttpClient.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(GetErrorMessage(responseJson, (int)response.StatusCode));
        }

        using var document = JsonDocument.Parse(responseJson);
        var generateModels = document.RootElement.GetProperty("models")
            .EnumerateArray()
            .Where(model => SupportsGenerateContent(model))
            .Select(model => model.GetProperty("name").GetString()?.Replace("models/", string.Empty) ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (generateModels.Count == 0)
        {
            throw new InvalidOperationException("The key is valid, but no generateContent model is available.");
        }

        var preferredAvailable = generateModels.Contains(preferredModel, StringComparer.OrdinalIgnoreCase);
        var recommendedModel = preferredAvailable
            ? preferredModel
            : generateModels.FirstOrDefault(name => name.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
              ?? generateModels.FirstOrDefault(name => name.Contains("flash", StringComparison.OrdinalIgnoreCase))
              ?? generateModels[0];
        return new GeminiKeyTestResult(normalizedKey, generateModels.Count, recommendedModel, preferredAvailable);
    }

    private static bool SupportsGenerateContent(JsonElement model)
    {
        if (!model.TryGetProperty("supportedGenerationMethods", out var methods)
            && !model.TryGetProperty("supportedActions", out methods))
        {
            return false;
        }

        return methods.EnumerateArray().Any(method =>
            string.Equals(method.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetErrorMessage(string responseJson, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
            return $"Gemini rejected the key ({statusCode}): {message}";
        }
        catch
        {
            return $"Gemini rejected the key ({statusCode}).";
        }
    }
}

public sealed record GeminiKeyTestResult(
    string NormalizedKey,
    int GenerateModelCount,
    string RecommendedModel,
    bool PreferredModelAvailable);
