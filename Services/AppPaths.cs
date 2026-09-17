using System.IO;

namespace DotaComboBoard.Services;

public static class AppPaths
{
    public static string Resolve(string relativePath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Environment.CurrentDirectory, relativePath)
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Required application file was not found: {relativePath}");
    }
}
