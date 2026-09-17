using System.Text.Json.Serialization;

namespace DotaComboBoard.Models;

public sealed class ComboAnalysis
{
    public string PatchNumber { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public List<HeroBuildRecommendation> HeroBuilds { get; set; } = [];
    public PhasePlan PhasePlan { get; set; } = new();
    public List<CounterHero> CounterHeroes { get; set; } = [];
    public List<CriticalStage> CriticalStages { get; set; } = [];
    public List<RoleExecutionPlan> RolePlans { get; set; } = [];
    public List<HeroReplacement> Replacements { get; set; } = [];
}

public sealed class HeroBuildRecommendation
{
    public string Hero { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string KeyItem { get; set; } = string.Empty;
    public string CriticalRule { get; set; } = string.Empty;
    public List<BuildOption> Options { get; set; } = [];

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string RoleDisplay { get; set; } = string.Empty;

    [JsonIgnore]
    public string KeyItemDisplay { get; set; } = string.Empty;

    [JsonIgnore]
    public string CriticalRuleDisplay { get; set; } = string.Empty;
}

public sealed class BuildOption
{
    public string Name { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<ItemTiming> Timings { get; set; } = [];
    public List<BuildItem> FinalItems { get; set; } = [];
}

public sealed class ItemTiming
{
    public string Timing { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;
}

public sealed class BuildItem
{
    public string ItemKey { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;
}

public sealed class PhasePlan
{
    public string LanePhase { get; set; } = string.Empty;
    public string TeamFight { get; set; } = string.Empty;
    public string PickOff { get; set; } = string.Empty;
    public string LosingGame { get; set; } = string.Empty;
}

public sealed class CounterHero
{
    public string Hero { get; set; } = string.Empty;
    public string Threat { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;
}

public sealed class CriticalStage
{
    public string Owner { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string FailureImpact { get; set; } = string.Empty;

    [JsonIgnore]
    public string FailureImpactDisplay { get; set; } = string.Empty;
}

public sealed class RoleExecutionPlan
{
    public string Hero { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string LanePositioning { get; set; } = string.Empty;
    public List<RoleTimelineStep> Timeline { get; set; } = [];
    public List<string> FarmRoute { get; set; } = [];
    public List<string> DecisionChecks { get; set; } = [];
    public string MapMovement { get; set; } = string.Empty;
    public string SafeFarm { get; set; } = string.Empty;
    public string AheadPlan { get; set; } = string.Empty;
    public string BehindPlan { get; set; } = string.Empty;
    public string TriangleTiming { get; set; } = string.Empty;
    public string LevelTiming { get; set; } = string.Empty;

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public HeroBuildRecommendation? Build { get; set; }

    [JsonIgnore]
    public string MapImagePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string RadiantMapRoute { get; set; } = string.Empty;

    [JsonIgnore]
    public string DireMapRoute { get; set; } = string.Empty;

    [JsonIgnore]
    public string MapFarmRule { get; set; } = string.Empty;
}

public sealed class RoleTimelineStep
{
    public string Minute { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
}

public sealed class HeroReplacement
{
    public string OriginalHero { get; set; } = string.Empty;
    public List<ReplacementOption> Alternatives { get; set; } = [];

    [JsonIgnore]
    public string OriginalHeroIconPath { get; set; } = string.Empty;
}

public sealed class ReplacementOption
{
    public string Hero { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;
}

public sealed class LineupPick
{
    public LineupPick(string hero, string name, string role)
    {
        Hero = hero;
        Name = name;
        Role = role;
    }

    public string Hero { get; }
    public string Name { get; }
    public string Role { get; }

    [JsonIgnore]
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string RoleDisplay { get; set; } = string.Empty;
}

public sealed record LineupAnalysisRequest(
    IReadOnlyList<LineupPick> Picks,
    string Specialty,
    string WinCondition);

public sealed record DotaPatchContext(
    string PatchNumber,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<HeroPatchContext> Heroes,
    string RelevantPatchNotesJson);

public sealed record HeroPatchContext(
    int HeroId,
    string HeroKey,
    string HeroName,
    IReadOnlyDictionary<string, IReadOnlyList<PopularItem>> PopularItems);

public sealed record PopularItem(int ItemId, string ItemKey, string ItemName, double Score);
