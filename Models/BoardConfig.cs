using System.Text.Json;

namespace DotaComboBoard.Models;

public sealed class BoardConfig
{
    public string Title { get; set; } = "Dota 2 Team Board";
    public WindowConfig Window { get; set; } = new();
    public GeminiConfig Gemini { get; set; } = new();
    public List<ColumnConfig> Columns { get; set; } = [];
    public List<RowConfig> Rows { get; set; } = [];
}

public sealed class GeminiConfig
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gemini-3.8-flash";
    public string EnvPath { get; set; } = string.Empty;
    public string ApiKeyVariable { get; set; } = "GEMINI_API_KEY";
}

public sealed class WindowConfig
{
    public double Width { get; set; } = 820;
    public double Height { get; set; } = 460;
    public int TargetScreen { get; set; } = 1;
    public bool FitToWorkArea { get; set; }
    public bool LockSize { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool HideOnMinimize { get; set; } = true;
}

public sealed class ColumnConfig
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string LabelTh { get; set; } = string.Empty;
    public double Width { get; set; } = 160;
}

public sealed class RowConfig
{
    public Dictionary<string, JsonElement> Cells { get; set; } = [];
}
