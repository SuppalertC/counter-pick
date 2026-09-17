namespace DotaComboBoard.Models;

public sealed class ComboPack
{
    public int FormatVersion { get; set; } = 1;
    public string DotaPatch { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public string GenerationPrompt { get; set; } = string.Empty;
    public List<ComboPackEntry> Combos { get; set; } = [];
}

public sealed class ComboPackEntry
{
    public List<ComboPackPick> Picks { get; set; } = [];
    public string Specialty { get; set; } = string.Empty;
    public string SpecialtyTh { get; set; } = string.Empty;
    public string WinCondition { get; set; } = string.Empty;
    public string WinConditionTh { get; set; } = string.Empty;
}

public sealed class ComboPackPick
{
    public string Hero { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed record ComboImportResult(int ImportedCount, int AddedCount, int SkippedDuplicates, bool Replaced);
