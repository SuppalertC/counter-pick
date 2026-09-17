using System.Text.Json;
using System.Windows;
using DotaComboBoard.Models;
using DotaComboBoard.Services;

namespace DotaComboBoard;

public partial class AnalysisWindow : Window
{
    private readonly GeminiAnalysisService _analysisService;
    private readonly DotaAssetResolver _assetResolver = new();
    private readonly LineupAnalysisRequest _lineup;
    private readonly bool _isThai;

    public AnalysisWindow(RowConfig row, GeminiConfig config, bool isThai)
    {
        InitializeComponent();
        _isThai = isThai;
        _lineup = CreateRequest(row);
        _analysisService = new GeminiAnalysisService(config);
        foreach (var pick in _lineup.Picks)
        {
            pick.IconPath = _assetResolver.ResolveHeroPath(pick.Hero);
            pick.RoleDisplay = LocalizeRole(pick.Role);
        }

        ApplyLanguageText();
        LineupItems.ItemsSource = _lineup.Picks;
        Loaded += async (_, _) => await LoadAnalysisAsync(false);
    }

    private async Task LoadAnalysisAsync(bool forceRefresh)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        AnalysisTabs.Visibility = Visibility.Collapsed;
        ReanalyzeButton.Visibility = Visibility.Collapsed;

        try
        {
            var analysis = await _analysisService.AnalyzeAsync(_lineup, _isThai ? "Thai" : "English", forceRefresh);
            await _assetResolver.PopulateAnalysisAssetsAsync(analysis, _lineup.Picks);
            ApplyResultLanguage(analysis);
            PatchText.Text = _isThai ? $"แพตช์ {analysis.PatchNumber}" : $"PATCH {analysis.PatchNumber}";
            AnalysisPatchTagText.Text = $"DOTA {analysis.PatchNumber}";
            OverviewText.Text = analysis.Overview;
            LanePhaseText.Text = analysis.PhasePlan.LanePhase;
            TeamFightText.Text = analysis.PhasePlan.TeamFight;
            PickOffText.Text = analysis.PhasePlan.PickOff;
            LosingGameText.Text = analysis.PhasePlan.LosingGame;
            CounterItems.ItemsSource = analysis.CounterHeroes;
            CriticalItems.ItemsSource = analysis.CriticalStages;
            ReplacementItems.ItemsSource = analysis.Replacements;
            Pos1Content.Content = FindRolePlan(analysis, "1", 0);
            Pos2Content.Content = FindRolePlan(analysis, "2", 1);
            Pos5Content.Content = FindRolePlan(analysis, "5", 2);
            LoadingPanel.Visibility = Visibility.Collapsed;
            AnalysisTabs.Visibility = Visibility.Visible;
            ReanalyzeButton.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            AnalysisPatchTagText.Text = "DOTA PATCH ERROR";
            ErrorText.Text = _isThai ? $"วิเคราะห์ไม่สำเร็จ: {exception.Message}" : exception.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void ApplyLanguageText()
    {
        Title = _isThai ? "วิเคราะห์ชุดฮีโร่" : "Lineup Analysis";
        HeaderTitleText.Text = _isThai ? "วิเคราะห์ทีม" : "LINEUP INTELLIGENCE";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        LoadingTitleText.Text = _isThai ? "กำลังอ่านแพตช์ล่าสุดและข้อมูลไอเทม..." : "READING CURRENT PATCH + BUILD DATA...";
        LoadingDetailText.Text = _isThai ? "แพตช์ Valve • ไอเทม OpenDota • วิเคราะห์ด้วย Gemini" : "Valve patch notes • OpenDota items • Gemini analysis";
        ErrorTitleText.Text = _isThai ? "วิเคราะห์ไม่สำเร็จ" : "ANALYSIS FAILED";
        RetryButton.Content = _isThai ? "ลองอีกครั้ง" : "TRY AGAIN";
        OverviewHeaderText.Text = _isThai ? "ภาพรวม" : "OVERVIEW";
        HowToPlayHeaderText.Text = _isThai ? "แผนการเล่น" : "HOW TO PLAY";
        LanePhaseHeaderText.Text = _isThai ? "ช่วงยืนเลน" : "LANE PHASE";
        TeamFightHeaderText.Text = _isThai ? "ทีมไฟต์" : "TEAM FIGHT";
        PickOffHeaderText.Text = _isThai ? "จับแยก" : "PICK OFF";
        LosingGameHeaderText.Text = _isThai ? "เกมตาม" : "LOSING GAME";
        CountersHeaderText.Text = _isThai ? "ตัวแก้ทาง" : "COUNTERS";
        ReplacementHeaderText.Text = _isThai ? "ตัวเลือกทดแทน" : "REPLACEMENTS";
        CriticalHeaderText.Text = _isThai ? "จุดสำคัญ — ห้ามพลาด" : "CRITICAL — DO NOT MISS";
        OverviewTab.Header = _isThai ? "ภาพรวม" : "OVERVIEW";
        Pos1Tab.Header = BuildHeroTabHeader("carry", "1", 0);
        Pos2Tab.Header = BuildHeroTabHeader("mid", "2", 1);
        Pos5Tab.Header = BuildHeroTabHeader("support", "5", 2);
        DisclaimerText.Text = _isThai
            ? "แนวทางออกไอเทมอ้างอิงแพตช์ Valve ล่าสุดและความนิยมจาก OpenDota — AI อาจผิดพลาดได้"
            : "Builds use current Valve patch + OpenDota popularity. AI can still be wrong.";
        ReanalyzeButton.Content = _isThai ? "อัปเดตแพตช์ + วิเคราะห์ใหม่" : "REFRESH PATCH + ANALYZE";
        ModelText.Text = _isThai
            ? "GEMINI FLASH • JSON คงรูปแบบ • บริบทแพตช์ล่าสุด"
            : "GEMINI FLASH • FIXED SCHEMA • LIVE PATCH CONTEXT";
        AnalysisAppVersionTagText.Text = $"APP v{AppVersionInfo.Current}";
        AnalysisPatchTagText.Text = "DOTA PATCH …";
    }

    private static RoleExecutionPlan? FindRolePlan(ComboAnalysis analysis, string position, int fallbackIndex)
    {
        return analysis.RolePlans.FirstOrDefault(plan => plan.Position.Equals(position, StringComparison.OrdinalIgnoreCase))
            ?? analysis.RolePlans.ElementAtOrDefault(fallbackIndex);
    }

    private string BuildHeroTabHeader(string role, string position, int fallbackIndex)
    {
        var pick = _lineup.Picks.FirstOrDefault(value => value.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            ?? _lineup.Picks.ElementAtOrDefault(fallbackIndex);
        if (pick is null)
        {
            return $"POS {position}";
        }

        return _isThai
            ? $"{pick.Name} • {LocalizeRole(pick.Role)}"
            : $"{pick.Name} • POS {position}";
    }

    private void ApplyResultLanguage(ComboAnalysis analysis)
    {
        foreach (var heroBuild in analysis.HeroBuilds)
        {
            heroBuild.RoleDisplay = _isThai
                ? $"ตำแหน่ง: {LocalizeRole(heroBuild.Role)}"
                : $"ROLE: {heroBuild.Role}";
            heroBuild.KeyItemDisplay = _isThai
                ? $"ไอเทมหลัก: {heroBuild.KeyItem}"
                : $"KEY ITEM: {heroBuild.KeyItem}";
            heroBuild.CriticalRuleDisplay = _isThai
                ? $"สำคัญ: {heroBuild.CriticalRule}"
                : $"CRITICAL: {heroBuild.CriticalRule}";
        }

        foreach (var criticalStage in analysis.CriticalStages)
        {
            criticalStage.FailureImpactDisplay = _isThai
                ? $"พลาดแล้ว: {criticalStage.FailureImpact}"
                : $"FAILURE: {criticalStage.FailureImpact}";
        }

        foreach (var rolePlan in analysis.RolePlans)
        {
            ApplyMapGuide(rolePlan);
        }
    }

    private void ApplyMapGuide(RoleExecutionPlan rolePlan)
    {
        var routes = rolePlan.Position switch
        {
            "1" => _isThai
                ? ("1 เวฟเลนปลอดภัย → 2 แคมป์ใหญ่ใกล้เลน → 3 แคมป์ใหญ่ชั้นใน → 4 แคมป์กลาง → 5 Ancient",
                   "ฟาร์มเป็นวงสั้น: เก็บเวฟก่อน แล้วต่อแคมป์ใหญ่/Ancient เฉพาะเมื่อเห็นฮีโร่ศัตรูตัวเปิดไฟต์")
                : ("1 Safe-lane wave → 2 outer hard camp → 3 inner hard camp → 4 medium camp → 5 Ancient",
                   "Farm a short loop: clear the wave first, then hard/Ancient camps only when enemy initiators are visible."),
            "2" => _isThai
                ? ("1 เวฟกลาง → 2 แคมป์ใหญ่ใกล้มิด → 3 Ancient → 4 แคมป์กลาง → 5 แคมป์ใหญ่ฝั่งปลอดภัย",
                   "ดันเวฟกลางให้ชนก่อนหายเข้าป่า เช็ก rune/TP แล้วกลับมารับเวฟถัดไป ไม่เดินลึกโดยไม่มีข้อมูล")
                : ("1 Mid wave → 2 nearby hard camp → 3 Ancient → 4 medium camp → 5 safe-side hard camp",
                   "Push mid before disappearing into the jungle, check rune/TP, then return for the next wave. Do not go deep without vision."),
            "5" => _isThai
                ? ("1 ยืนเลนเซฟ → 2 แคมป์ดึงใกล้เลน → 3 แคมป์ซ้อน/ดึง → 4 จุด ward ปลอดภัย → 5 Stack Ancient",
                   "หน้าที่หลักคือดึงและ stack ไม่แย่งเวฟแคร์รี่ เดินเข้าป่าเมื่อมีเพื่อนใกล้หรือมี ward คุมทางเข้า")
                : ("1 Hold safe lane → 2 pull camp → 3 stack/pull camp → 4 safe ward area → 5 stack Ancient",
                   "Prioritize pulling and stacking without taking the carry's wave. Enter jungle only with an ally nearby or warded entrances."),
            _ => _isThai
                ? ("1 เวฟปลอดภัย → 2 แคมป์ใกล้สุด → 3 แคมป์ชั้นใน → 4 จุดรวมทีม → 5 Ancient",
                   "เลือกเส้นทางสั้นที่มี vision และตัดเส้นทางทันทีเมื่อฮีโร่ศัตรูหายจากแมพ")
                : ("1 Safe wave → 2 nearest camp → 3 inner camp → 4 team area → 5 Ancient",
                   "Use the shortest warded route and abort immediately when enemy heroes disappear from the map.")
        };

        rolePlan.RadiantMapRoute = (_isThai ? "Radiant: " : "RADIANT: ") + routes.Item1;
        rolePlan.DireMapRoute = (_isThai ? "Dire: ใช้เส้นสีส้มแบบกลับด้าน — " : "DIRE: Follow the mirrored orange route — ") + routes.Item1;
        rolePlan.MapFarmRule = routes.Item2;
    }

    private string LocalizeRole(string role)
    {
        if (!_isThai)
        {
            return role;
        }

        return role.ToLowerInvariant() switch
        {
            "carry" => "แคร์รี่",
            "mid" => "มิด",
            "support" => "ซัพพอร์ต",
            _ => role
        };
    }

    private static LineupAnalysisRequest CreateRequest(RowConfig row)
    {
        var picksElement = row.Cells["pick"].GetProperty("picks");
        var picks = picksElement.EnumerateArray()
            .Select(pick => new LineupPick(
                GetString(pick, "hero"),
                GetString(pick, "name"),
                GetString(pick, "role")))
            .ToList();
        var detail = row.Cells["detail"];
        return new LineupAnalysisRequest(
            picks,
            GetString(detail, "specialty"),
            GetString(detail, "winCondition"));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() ?? string.Empty : string.Empty;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadAnalysisAsync(false);

    private async void ReanalyzeButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadAnalysisAsync(true);

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
