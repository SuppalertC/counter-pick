using System.Net.Http;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public static class GeminiApiKeyService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string? Resolve(GeminiConfig config)
    {
        var savedKey = AppSettingsService.Load().GeminiApiKey.Trim();
        return !string.IsNullOrWhiteSpace(savedKey)
            ? savedKey
            : Environment.GetEnvironmentVariable(config.ApiKeyVariable)
              ?? EnvFile.ReadValue(config.EnvPath, config.ApiKeyVariable);
    }

    public static async Task TestAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is empty.");
        }

        var endpoint = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("x-goog-api-key", apiKey.Trim());
        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini rejected the key ({(int)response.StatusCode}).");
        }
    }
}
