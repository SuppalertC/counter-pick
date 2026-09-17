using System.IO;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public static class BoardLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static BoardConfig Load(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<BoardConfig>(stream, JsonOptions)
            ?? throw new InvalidDataException("board.json is empty or invalid.");
    }
}
