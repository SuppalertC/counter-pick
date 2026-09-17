using System.IO;

namespace DotaComboBoard.Services;

public static class EnvFile
{
    public static string? ReadValue(string path, string variableName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 1 || !line[..separatorIndex].Trim().Equals(variableName, StringComparison.Ordinal))
            {
                continue;
            }

            return line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }
}
