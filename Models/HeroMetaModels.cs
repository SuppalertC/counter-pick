using System.Text.Json.Serialization;

namespace DotaComboBoard.Models;

public sealed class HeroDirectoryEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PrimaryAttribute { get; set; } = string.Empty;
    public string AttackType { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public long PublicPicks { get; set; }
    public long PublicWins { get; set; }

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public double PickRate { get; set; }

    [JsonIgnore]
    public double WinRate => PublicPicks > 0 ? (double)PublicWins / PublicPicks * 100 : 0;
}

public sealed class HeroMatchupDisplay
{
    public int HeroId { get; set; }
    public string Hero { get; set; } = string.Empty;
    public int GamesPlayed { get; set; }
    public double SelectedHeroWinRate { get; set; }
    public string IconPath { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed record HeroMatchupResult(
    HeroDirectoryEntry SelectedHero,
    IReadOnlyList<HeroMatchupDisplay> Counters,
    IReadOnlyList<HeroMatchupDisplay> Advantages);

public sealed record HeroMatchupStat(int HeroId, int GamesPlayed, double SelectedHeroWinRate);

public sealed class TeamCounterResult
{
    public List<TeamCounterHeroRecommendation> RecommendedHeroes { get; set; } = [];
    public List<TeamCounterLineup> Teams { get; set; } = [];
    public string AiSummary { get; set; } = string.Empty;
    public bool UsedAi { get; set; }
}

public sealed class TeamCounterHeroRecommendation
{
    public int HeroId { get; set; }
    public string Hero { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public double WinRate { get; set; }
    public double TeamCounterWinRate { get; set; }
    public double OverallScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<TeamCounterTarget> StrongAgainst { get; set; } = [];

    [JsonIgnore]
    public string ScoreText { get; set; } = string.Empty;
}

public sealed class TeamCounterTarget
{
    public string EnemyHero { get; set; } = string.Empty;
    public double CounterWinRate { get; set; }

    [JsonIgnore]
    public string DisplayText => $"[{EnemyHero}] {CounterWinRate:0.0}%";
}

public sealed class TeamCounterLineup
{
    public int Rank { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<TeamCounterPick> Picks { get; set; } = [];
}

public sealed class TeamCounterPick
{
    public int HeroId { get; set; }
    public string Hero { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Targets { get; set; } = string.Empty;
}

public sealed class MetaCounterAnalysis
{
    public string PatchNumber { get; set; } = string.Empty;
    public DateTimeOffset RetrievedAt { get; set; }
    public List<MetaCounterRecommendation> Recommendations { get; set; } = [];
}

public sealed class MetaCounterRecommendation
{
    public string Hero { get; set; } = string.Empty;
    public string HeroKey { get; set; } = string.Empty;
    public double PickRate { get; set; }
    public double WinRate { get; set; }
    public double MatchupScore { get; set; }
    public int PlanNumber { get; set; }
    public int BackupPlanNumber { get; set; }
    public string Why { get; set; } = string.Empty;
    public string Adjustment { get; set; } = string.Empty;
    public List<MetaPlanPick> Picks { get; set; } = [];

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string PlanTitle { get; set; } = string.Empty;

    [JsonIgnore]
    public string BackupPlanTitle { get; set; } = string.Empty;

    [JsonIgnore]
    public string PlanLabel { get; set; } = string.Empty;

    [JsonIgnore]
    public string BackupPlanLabel { get; set; } = string.Empty;

    [JsonIgnore]
    public string MatchupScoreText { get; set; } = string.Empty;

    [JsonIgnore]
    public string PickRateText { get; set; } = string.Empty;

    [JsonIgnore]
    public string WinRateText { get; set; } = string.Empty;
}

public sealed class MetaPlanPick
{
    public string Hero { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
}
