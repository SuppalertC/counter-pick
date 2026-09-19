using System.IO;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class ComboJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public ComboPack CreatePack(BoardConfig board, string patchNumber, string prompt)
    {
        return new ComboPack
        {
            DotaPatch = patchNumber,
            GeneratedAt = DateTimeOffset.Now,
            GenerationPrompt = prompt,
            Combos = board.Rows.Select(ToPackEntry).ToList()
        };
    }

    public ComboPack CreateTemplate(string patchNumber, string prompt)
    {
        return new ComboPack
        {
            DotaPatch = patchNumber,
            GeneratedAt = DateTimeOffset.Now,
            GenerationPrompt = prompt,
            Combos =
            [
                new ComboPackEntry
                {
                    Picks =
                    [
                        new ComboPackPick { Hero = "skeleton_king", Name = "Wraith King", Role = "Carry" },
                        new ComboPackPick { Hero = "sniper", Name = "Sniper", Role = "Mid" },
                        new ComboPackPick { Hero = "vengefulspirit", Name = "Vengeful Spirit", Role = "Support" }
                    ],
                    Specialty = "Fortress / Siege",
                    SpecialtyTh = "ตั้งรับ / ตีป้อม",
                    WinCondition = "Wraith King fronts, Venge saves, and Sniper fires from the backline.",
                    WinConditionTh = "Wraith King ยืนหน้า, Venge ช่วยเซฟ และ Sniper ยิงจากแนวหลัง"
                }
            ]
        };
    }

    public string Serialize(ComboPack pack) => JsonSerializer.Serialize(pack, JsonOptions);

    public string CreateGenerationPrompt(string patchNumber)
    {
        var schema = Serialize(CreateTemplate(patchNumber, string.Empty));
        return $"""
            You are a current-patch Dota 2 draft strategist. Generate practical three-hero combo rows for patch {patchNumber}.
            Return JSON only. Follow the exact JSON structure shown below with no markdown fences or extra properties.

            Requirements:
            - Generate 15 unique combos.
            - Every combo contains exactly three picks in this order: Carry, Mid, Support.
            - hero must be the Valve internal key without npc_dota_hero_ (examples: skeleton_king, vengefulspirit).
            - name must be the canonical English hero name.
            - specialty and winCondition must be concise English.
            - specialtyTh and winConditionTh must be natural Thai.
            - Use patch {patchNumber} synergies, timings, lanes, objectives, saves, initiation, and damage profile.
            - Avoid duplicate three-hero sets and avoid inventing heroes or patch statistics.
            - Keep formatVersion at 1 and dotaPatch at "{patchNumber}".

            Exact JSON format:
            {schema}
            """;
    }

    public async Task<ComboPack> LoadPackAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return ParsePack(json);
    }

    public ComboPack ParsePack(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        ComboPack pack;
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            pack = new ComboPack
            {
                Combos = JsonSerializer.Deserialize<List<ComboPackEntry>>(root.GetRawText(), JsonOptions) ?? []
            };
        }
        else if (root.TryGetProperty("combos", out _))
        {
            pack = JsonSerializer.Deserialize<ComboPack>(json, JsonOptions)
                ?? throw new InvalidDataException("Combo pack is empty.");
        }
        else if (root.TryGetProperty("rows", out _))
        {
            var board = JsonSerializer.Deserialize<BoardConfig>(json, JsonOptions)
                ?? throw new InvalidDataException("board.json is empty.");
            pack = CreatePack(board, string.Empty, string.Empty);
        }
        else
        {
            throw new InvalidDataException("Unsupported JSON. Expected combo pack, board.json, or a combo array.");
        }

        ValidatePack(pack);
        return pack;
    }

    public ComboImportResult ApplyImport(string boardPath, ComboPack pack, bool replace)
    {
        ValidatePack(pack);
        var board = BoardLoader.Load(boardPath);
        var importedRows = pack.Combos.Select(ToBoardRow).ToList();
        var skipped = 0;
        if (replace)
        {
            board.Rows = importedRows;
        }
        else
        {
            var existingKeys = board.Rows.Select(GetRowKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in importedRows)
            {
                if (!existingKeys.Add(GetRowKey(row)))
                {
                    skipped++;
                    continue;
                }

                board.Rows.Add(row);
            }
        }

        BackupCurrentBoard(boardPath);
        var temporaryPath = $"{boardPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(board, JsonOptions));
        File.Move(temporaryPath, boardPath, true);
        return new ComboImportResult(pack.Combos.Count, replace ? importedRows.Count : importedRows.Count - skipped, skipped, replace);
    }

    public async Task ExportAsync(string path, ComboPack pack)
    {
        await File.WriteAllTextAsync(path, Serialize(pack));
    }

    public void ValidatePack(ComboPack pack)
    {
        if (pack.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported formatVersion: {pack.FormatVersion}");
        }

        if (pack.Combos.Count == 0)
        {
            throw new InvalidDataException("The JSON does not contain any combos.");
        }

        for (var index = 0; index < pack.Combos.Count; index++)
        {
            var combo = pack.Combos[index];
            if (combo.Picks.Count != 3)
            {
                throw new InvalidDataException($"Combo {index + 1} must contain exactly three picks.");
            }

            if (combo.Picks.Any(pick => string.IsNullOrWhiteSpace(pick.Hero)
                                        || string.IsNullOrWhiteSpace(pick.Name)
                                        || string.IsNullOrWhiteSpace(pick.Role)))
            {
                throw new InvalidDataException($"Combo {index + 1} has an incomplete hero, name, or role.");
            }

            var roles = combo.Picks.Select(pick => pick.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!roles.Contains("Carry") || !roles.Contains("Mid") || !roles.Contains("Support"))
            {
                throw new InvalidDataException($"Combo {index + 1} must include Carry, Mid, and Support roles.");
            }
        }
    }

    private static ComboPackEntry ToPackEntry(RowConfig row)
    {
        if (!row.Cells.TryGetValue("pick", out var pickCell)
            || !pickCell.TryGetProperty("picks", out var picks))
        {
            throw new InvalidDataException("A board row is missing cells.pick.picks.");
        }

        row.Cells.TryGetValue("detail", out var detail);
        return new ComboPackEntry
        {
            Picks = picks.EnumerateArray().Select(pick => new ComboPackPick
            {
                Hero = GetString(pick, "hero"),
                Name = GetString(pick, "name"),
                Role = GetString(pick, "role")
            }).ToList(),
            Specialty = GetString(detail, "specialty"),
            SpecialtyTh = GetString(detail, "specialtyTh"),
            WinCondition = GetString(detail, "winCondition"),
            WinConditionTh = GetString(detail, "winConditionTh")
        };
    }

    private static RowConfig ToBoardRow(ComboPackEntry combo)
    {
        return new RowConfig
        {
            Cells = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["pick"] = JsonSerializer.SerializeToElement(new
                {
                    picks = combo.Picks.Select(pick => new { pick.Hero, pick.Name, pick.Role })
                }, JsonOptions),
                ["detail"] = JsonSerializer.SerializeToElement(new
                {
                    combo.Specialty,
                    combo.SpecialtyTh,
                    combo.WinCondition,
                    combo.WinConditionTh
                }, JsonOptions)
            }
        };
    }

    private static string GetRowKey(RowConfig row)
    {
        if (!row.Cells.TryGetValue("pick", out var pickCell)
            || !pickCell.TryGetProperty("picks", out var picks))
        {
            return Guid.NewGuid().ToString("N");
        }

        return string.Join("|", picks.EnumerateArray().Select(pick => GetString(pick, "hero").ToLowerInvariant()));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static void BackupCurrentBoard(string boardPath)
    {
        var backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotaComboBoard",
            "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"board-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(boardPath, backupPath, true);
    }
}
