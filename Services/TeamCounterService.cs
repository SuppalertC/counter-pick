using System.Net.Http;
using System.Text;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class TeamCounterService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HeroMetaService _metaService;
    private readonly GeminiConfig _config;

    public TeamCounterService(HeroMetaService metaService, GeminiConfig config)
    {
        _metaService = metaService;
        _config = config;
    }

    public async Task<TeamCounterResult> AnalyzeAsync(IReadOnlyList<HeroDirectoryEntry> enemies, bool isThai)
    {
        if (enemies.Count != 5)
        {
            throw new InvalidOperationException(isThai ? "กรุณาเลือกฮีโร่ศัตรูให้ครบ 5 ตัว" : "Select exactly five enemy heroes.");
        }

        var catalog = await _metaService.GetCatalogAsync();
        var matchupTasks = enemies.Select(enemy => _metaService.GetMatchupRatesAsync(enemy)).ToArray();
        var matchupMaps = await Task.WhenAll(matchupTasks);
        var enemyIds = enemies.Select(enemy => enemy.Id).ToHashSet();

        var candidates = catalog
            .Where(hero => !enemyIds.Contains(hero.Id))
            .Select(hero => ScoreCandidate(hero, enemies, matchupMaps, isThai))
            .Where(candidate => candidate.MatchupCount >= 3)
            .OrderByDescending(candidate => candidate.Recommendation.OverallScore)
            .ToList();

        var result = new TeamCounterResult
        {
            RecommendedHeroes = candidates.Take(12).Select(candidate => candidate.Recommendation).ToList(),
            Teams = BuildTeams(candidates, isThai)
        };

        var apiKey = GeminiApiKeyService.Resolve(_config);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            result.AiSummary = isThai
                ? "ใช้คะแนน OpenDota แบบเร็วแล้ว • เพิ่ม Gemini API key ใน Settings เพื่อรับบทวิเคราะห์ทีม"
                : "Fast OpenDota scoring used. Add a Gemini API key in Settings for team analysis.";
            return result;
        }

        try
        {
            var aiResult = await AnalyzeWithGeminiAsync(apiKey, enemies, result, isThai);
            ApplyAiResult(result, aiResult);
            result.UsedAi = true;
        }
        catch (Exception exception)
        {
            result.AiSummary = isThai
                ? $"คำนวณทีมจาก OpenDota สำเร็จ • Gemini ไม่พร้อม: {exception.Message}"
                : $"OpenDota team scoring completed. Gemini unavailable: {exception.Message}";
        }

        return result;
    }

    private static ScoredCandidate ScoreCandidate(
        HeroDirectoryEntry hero,
        IReadOnlyList<HeroDirectoryEntry> enemies,
        IReadOnlyList<IReadOnlyDictionary<int, HeroMatchupStat>> matchupMaps,
        bool isThai)
    {
        var matchups = matchupMaps
            .Select((map, index) => new
            {
                EnemyIndex = index,
                Stat = map.TryGetValue(hero.Id, out var matchup) ? matchup : null
            })
            .Where(item => item.Stat is not null && item.Stat.GamesPlayed >= 20)
            .ToList();
        var stats = matchups.Select(item => item.Stat!).ToList();
        var totalWeight = stats.Sum(stat => Math.Sqrt(stat.GamesPlayed));
        var counterWinRate = totalWeight > 0
            ? stats.Sum(stat => (100 - stat.SelectedHeroWinRate) * Math.Sqrt(stat.GamesPlayed)) / totalWeight
            : 50;
        var overallScore = (hero.WinRate * 0.35) + (counterWinRate * 0.65);
        var strongAgainst = matchups
            .Select(item => new TeamCounterTarget
            {
                EnemyHero = EnemyName(item.EnemyIndex),
                CounterWinRate = 100 - item.Stat!.SelectedHeroWinRate
            })
            .OrderByDescending(item => item.CounterWinRate)
            .Take(3)
            .ToList();
        var recommendation = new TeamCounterHeroRecommendation
        {
            HeroId = hero.Id,
            Hero = hero.Name,
            IconPath = hero.IconPath,
            WinRate = hero.WinRate,
            TeamCounterWinRate = counterWinRate,
            OverallScore = overallScore,
            StrongAgainst = strongAgainst,
            ScoreText = isThai
                ? $"รวม {overallScore:0.0} • Win {hero.WinRate:0.0}% • สวนทีม {counterWinRate:0.0}%"
                : $"Overall {overallScore:0.0} • Win {hero.WinRate:0.0}% • Team counter {counterWinRate:0.0}%",
            Reason = BuildQuickReason(hero.Name, strongAgainst, isThai)
        };
        return new ScoredCandidate(hero, recommendation, stats.Count);

        string EnemyName(int index) => index >= 0 && index < enemies.Count
            ? enemies[index].Name
            : "Unknown";
    }

    private static string BuildQuickReason(
        string heroName,
        IReadOnlyList<TeamCounterTarget> targets,
        bool isThai)
    {
        var targetText = string.Join(" • ", targets.Take(2).Select(target => $"[{target.EnemyHero}]"));
        var action = heroName switch
        {
            "Anti-Mage" => isThai ? "เบิร์นมานา" : "Burn mana",
            "Invoker" => isThai ? "EMP เผามานา" : "Drain mana with EMP",
            "Lion" => isThai ? "ดูดมานาและล็อก" : "Drain mana and disable",
            "Ancient Apparition" => isThai ? "ตัดการฟื้นฟู" : "Stop healing",
            "Doom" => isThai ? "ปิดสกิลหลัก" : "Disable key abilities",
            "Silencer" => isThai ? "ใบ้สกิลทั้งไฟต์" : "Silence the fight",
            "Shadow Demon" => isThai ? "สร้างภาพโจมตีกลับ" : "Turn illusions against them",
            "Axe" => isThai ? "บังคับโจมตีและล็อก" : "Force attacks and lock down",
            "Nyx Assassin" => isThai ? "ล้วงและหยุดคอมโบ" : "Burst and interrupt combos",
            "Viper" => isThai ? "ปิด passive และกดเลน" : "Break passives and pressure lane",
            "Shadow Shaman" => isThai ? "ล็อกยาวแล้วปิดเป้า" : "Chain-disable the target",
            "Bane" => isThai ? "จับล็อกตัวหลัก" : "Lock down the core",
            "Disruptor" => isThai ? "ดึงกลับและปิดพื้นที่" : "Glimpse and zone",
            "Outworld Destroyer" => isThai ? "กักตัวแล้วระเบิดมานา" : "Isolate and punish mana",
            _ => isThai ? "กด matchup และคุมไฟต์" : "Pressure matchup and control fights"
        };
        return $"{action}: {targetText}";
    }

    private static List<TeamCounterLineup> BuildTeams(IReadOnlyList<ScoredCandidate> candidates, bool isThai)
    {
        var slots = new[]
        {
            new RoleSlot("Carry", new[] { "Carry" }),
            new RoleSlot("Mid", new[] { "Nuker", "Carry" }),
            new RoleSlot("Offlane", new[] { "Initiator", "Durable" }),
            new RoleSlot("Soft Support", new[] { "Support", "Disabler", "Nuker" }),
            new RoleSlot("Hard Support", new[] { "Support" })
        };
        var useCounts = new Dictionary<int, int>();
        var teams = new List<TeamCounterLineup>();

        for (var teamIndex = 0; teamIndex < 3; teamIndex++)
        {
            var used = new HashSet<int>();
            var picks = new List<TeamCounterPick>();
            foreach (var slot in slots)
            {
                var selected = candidates
                    .Where(candidate => !used.Contains(candidate.Hero.Id))
                    .OrderByDescending(candidate =>
                        candidate.Recommendation.OverallScore
                        + GetRoleFit(candidate.Hero, slot) * 3
                        - useCounts.GetValueOrDefault(candidate.Hero.Id) * 2.5
                        + ((candidate.Hero.Id + teamIndex) % 7) * 0.04)
                    .First();
                used.Add(selected.Hero.Id);
                useCounts[selected.Hero.Id] = useCounts.GetValueOrDefault(selected.Hero.Id) + 1;
                picks.Add(new TeamCounterPick
                {
                    HeroId = selected.Hero.Id,
                    Hero = selected.Hero.Name,
                    Role = slot.Name,
                    IconPath = selected.Hero.IconPath,
                    Score = selected.Recommendation.OverallScore,
                    Targets = string.Join(" • ", selected.Recommendation.StrongAgainst
                        .Take(2)
                        .Select(target => $"[{target.EnemyHero}]"))
                });
            }

            teams.Add(new TeamCounterLineup
            {
                Rank = teamIndex + 1,
                Title = isThai ? $"ทีมสวนชุดที่ {teamIndex + 1}" : $"Counter Team {teamIndex + 1}",
                Strategy = isThai
                    ? "จัดทีมจากคะแนน matchup พร้อมกระจาย Carry / Mid / Offlane / Support"
                    : "Built from matchup scores with Carry / Mid / Offlane / Support coverage.",
                Picks = picks
            });
        }

        return teams;
    }

    private static int GetRoleFit(HeroDirectoryEntry hero, RoleSlot slot)
    {
        return slot.PreferredRoles.Count(role => hero.Roles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<AiTeamCounterResponse> AnalyzeWithGeminiAsync(
        string apiKey,
        IReadOnlyList<HeroDirectoryEntry> enemies,
        TeamCounterResult result,
        bool isThai)
    {
        var language = isThai ? "Thai" : "English";
        var input = new
        {
            enemies = enemies.Select(hero => hero.Name),
            rankedCandidates = result.RecommendedHeroes.Take(12).Select(hero => new
            {
                hero.Hero,
                hero.WinRate,
                hero.TeamCounterWinRate,
                hero.OverallScore,
                strongAgainst = hero.StrongAgainst.Select(target => new
                {
                    target.EnemyHero,
                    target.CounterWinRate
                })
            }),
            teams = result.Teams.Select(team => new
            {
                team.Rank,
                picks = team.Picks.Select(pick => new { pick.Hero, pick.Role, pick.Score })
            })
        };
        var prompt = $"""
            You are a Dota 2 draft coach. Analyze only the supplied OpenDota-derived scores. Respond in {language}.
            Do not change heroes, scores, or teams. Give one concise overall summary, one very short tactical reason
            for each ranked candidate, and a title plus short strategy for each of the three teams. Every hero reason
            must name one or two supplied enemy targets in square brackets and state the mechanic/action used against
            them in at most 12 words, for example "Burn mana: [Medusa] • kite: [Huskar]". Use known Dota hero-kit
            interactions but never invent statistics. Canonical hero names stay English.
            Input: {JsonSerializer.Serialize(input)}
            """;
        var schema = new
        {
            type = "OBJECT",
            properties = new
            {
                summary = new { type = "STRING" },
                heroInsights = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new { hero = new { type = "STRING" }, reason = new { type = "STRING" } },
                        required = new[] { "hero", "reason" }
                    }
                },
                teamInsights = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            teamNumber = new { type = "INTEGER" },
                            title = new { type = "STRING" },
                            strategy = new { type = "STRING" }
                        },
                        required = new[] { "teamNumber", "title", "strategy" }
                    }
                }
            },
            required = new[] { "summary", "heroInsights", "teamInsights" }
        };
        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                temperature = 0.15,
                maxOutputTokens = 1800,
                responseMimeType = "application/json",
                responseSchema = schema
            }
        };

        Exception? lastException = null;
        foreach (var model in new[] { "gemini-3.5-flash-lite", _config.Model }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var response = await HttpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Gemini request failed ({(int)response.StatusCode}).");
                }

                using var document = JsonDocument.Parse(responseJson);
                var text = document.RootElement.GetProperty("candidates")[0]
                    .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                return JsonSerializer.Deserialize<AiTeamCounterResponse>(text ?? string.Empty, JsonOptions)
                    ?? throw new InvalidOperationException("Gemini returned empty team analysis.");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                lastException = exception;
            }
        }

        throw lastException ?? new InvalidOperationException("Gemini team analysis failed.");
    }

    private static void ApplyAiResult(TeamCounterResult result, AiTeamCounterResponse aiResult)
    {
        result.AiSummary = aiResult.Summary;
        foreach (var insight in aiResult.HeroInsights)
        {
            var hero = result.RecommendedHeroes.FirstOrDefault(candidate =>
                candidate.Hero.Equals(insight.Hero, StringComparison.OrdinalIgnoreCase));
            if (hero is not null)
            {
                hero.Reason = insight.Reason;
            }
        }

        foreach (var insight in aiResult.TeamInsights)
        {
            var team = result.Teams.FirstOrDefault(candidate => candidate.Rank == insight.TeamNumber);
            if (team is not null)
            {
                team.Title = insight.Title;
                team.Strategy = insight.Strategy;
            }
        }
    }

    private sealed record ScoredCandidate(
        HeroDirectoryEntry Hero,
        TeamCounterHeroRecommendation Recommendation,
        int MatchupCount);

    private sealed record RoleSlot(string Name, IReadOnlyList<string> PreferredRoles);

    private sealed class AiTeamCounterResponse
    {
        public string Summary { get; set; } = string.Empty;
        public List<AiHeroInsight> HeroInsights { get; set; } = [];
        public List<AiTeamInsight> TeamInsights { get; set; } = [];
    }

    private sealed class AiHeroInsight
    {
        public string Hero { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class AiTeamInsight
    {
        public int TeamNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
    }
}
