using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DotaComboBoard.Models;
using DotaComboBoard.Services;

namespace DotaComboBoard;

public partial class AnalysisWindow : Window
{
    private readonly GeminiAnalysisService _analysisService;
    private readonly DotaAssetResolver _assetResolver = new();
    private readonly LineupAnalysisRequest _lineup;
    private readonly bool _isThai;
    private readonly HashSet<int> _loadedHeroTabs = [];
    private readonly HashSet<int> _loadingHeroTabs = [];
    private bool _initialized;

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
        _initialized = true;
        Loaded += async (_, _) => await LoadOverviewAsync(false);
    }

    private async Task LoadOverviewAsync(bool forceRefresh)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        AnalysisTabs.Visibility = Visibility.Collapsed;
        ReanalyzeButton.Visibility = Visibility.Collapsed;

        if (forceRefresh)
        {
            ResetHeroTabs();
        }

        try
        {
            var analysis = await _analysisService.AnalyzeOverviewAsync(
                _lineup,
                _isThai ? "Thai" : "English",
                forceRefresh);
            await _assetResolver.PopulateOverviewAssetsAsync(analysis, _lineup.Picks);
            ApplyOverviewLanguage(analysis);
            PatchText.Text = _isThai ? $"แพตช์ {analysis.PatchNumber}" : $"PATCH {analysis.PatchNumber}";
            AnalysisPatchTagText.Text = $"DOTA {analysis.PatchNumber}";
            OverviewText.Text = analysis.Overview;
            HeroItemItems.ItemsSource = analysis.HeroItems;
            DraftOrderItems.ItemsSource = analysis.DraftOrder.OrderBy(step => step.Order);
            DraftCautionItems.ItemsSource = analysis.DraftCautions;
            AnalysisTabs.SelectedItem = OverviewTab;
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

    private async Task LoadHeroTabAsync(int tabIndex, bool forceRefresh)
    {
        if (_loadedHeroTabs.Contains(tabIndex) && !forceRefresh || !_loadingHeroTabs.Add(tabIndex))
        {
            return;
        }

        var pick = GetPickForTab(tabIndex);
        if (pick is null)
        {
            ShowHeroTabError(tabIndex, _isThai ? "ไม่พบฮีโร่ของแท็บนี้" : "No hero is assigned to this tab.");
            _loadingHeroTabs.Remove(tabIndex);
            return;
        }

        ShowHeroTabLoading(tabIndex);
        try
        {
            var analysis = await _analysisService.AnalyzeHeroAsync(
                _lineup,
                pick,
                _isThai ? "Thai" : "English",
                forceRefresh);
            await _assetResolver.PopulateHeroTabAssetsAsync(analysis);
            ApplyHeroTabLanguage(analysis);
            GetHeroContent(tabIndex).Content = analysis.RolePlan;
            GetHeroContent(tabIndex).Visibility = Visibility.Visible;
            GetHeroLoadingPanel(tabIndex).Visibility = Visibility.Collapsed;
            GetHeroErrorPanel(tabIndex).Visibility = Visibility.Collapsed;
            _loadedHeroTabs.Add(tabIndex);
        }
        catch (Exception exception)
        {
            ShowHeroTabError(
                tabIndex,
                _isThai ? $"โหลดข้อมูลฮีโร่ไม่สำเร็จ: {exception.Message}" : exception.Message);
        }
        finally
        {
            _loadingHeroTabs.Remove(tabIndex);
        }
    }

    private void ApplyLanguageText()
    {
        Title = _isThai ? "วิเคราะห์ชุดฮีโร่" : "Lineup Analysis";
        HeaderTitleText.Text = _isThai ? "วิเคราะห์ทีม" : "LINEUP INTELLIGENCE";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        LoadingTitleText.Text = _isThai ? "กำลังโหลด Overview..." : "LOADING OVERVIEW...";
        LoadingDetailText.Text = _isThai
            ? "โหลดเฉพาะไอเทมและลำดับดราฟต์ • แท็บฮีโร่ยังไม่โหลด"
            : "Items and draft order only • Hero tabs stay unloaded";
        ErrorTitleText.Text = _isThai ? "วิเคราะห์ไม่สำเร็จ" : "ANALYSIS FAILED";
        RetryButton.Content = _isThai ? "ลองอีกครั้ง" : "TRY AGAIN";
        OverviewHeaderText.Text = _isThai ? "ภาพรวม" : "OVERVIEW";
        ItemsHeaderText.Text = _isThai ? "ไอเทมแนะนำของแต่ละฮีโร่" : "HERO ITEMS";
        DraftHeaderText.Text = _isThai ? "ลำดับการหยิบ" : "DRAFT PICK ORDER";
        CautionsHeaderText.Text = _isThai ? "จุดที่ต้องระวัง" : "DRAFT CAUTIONS";
        OverviewTab.Header = _isThai ? "ภาพรวม" : "OVERVIEW";
        Pos1Tab.Header = BuildHeroTabHeader("carry", "1", 0);
        Pos2Tab.Header = BuildHeroTabHeader("mid", "2", 1);
        Pos5Tab.Header = BuildHeroTabHeader("support", "5", 2);
        var heroLoadingText = _isThai ? "กำลังโหลดเฉพาะฮีโร่แท็บนี้..." : "Loading this hero tab only...";
        Pos1LoadingText.Text = heroLoadingText;
        Pos2LoadingText.Text = heroLoadingText;
        Pos5LoadingText.Text = heroLoadingText;
        DisclaimerText.Text = _isThai
            ? "Overview โหลดก่อน • ข้อมูลละเอียดของฮีโร่จะโหลดเมื่อกดแท็บนั้นเท่านั้น • AI อาจผิดพลาดได้"
            : "Overview loads first. Detailed hero data loads only when its tab is opened. AI can be wrong.";
        ReanalyzeButton.Content = _isThai ? "อัปเดต Overview" : "REFRESH OVERVIEW";
        ModelText.Text = _isThai
            ? "GEMINI FLASH • LAZY LOAD รายแท็บ • บริบทแพตช์ล่าสุด"
            : "GEMINI FLASH • LAZY TAB LOADING • LIVE PATCH CONTEXT";
        AnalysisAppVersionTagText.Text = $"APP v{AppVersionInfo.Current}";
        AnalysisPatchTagText.Text = "DOTA PATCH …";
    }

    private void ApplyOverviewLanguage(ComboAnalysis analysis)
    {
        foreach (var heroItems in analysis.HeroItems)
        {
            heroItems.RoleDisplay = _isThai
                ? $"ตำแหน่ง: {LocalizeRole(heroItems.Role)}"
                : $"ROLE: {heroItems.Role}";
        }

        foreach (var draftStep in analysis.DraftOrder)
        {
            draftStep.OrderDisplay = draftStep.Order.ToString();
        }
    }

    private void ApplyHeroTabLanguage(HeroTabAnalysis analysis)
    {
        analysis.Build.RoleDisplay = _isThai
            ? $"ตำแหน่ง: {LocalizeRole(analysis.Build.Role)}"
            : $"ROLE: {analysis.Build.Role}";
        analysis.Build.KeyItemDisplay = _isThai
            ? $"ไอเทมหลัก: {analysis.Build.KeyItem}"
            : $"KEY ITEM: {analysis.Build.KeyItem}";
        analysis.Build.CriticalRuleDisplay = _isThai
            ? $"สำคัญ: {analysis.Build.CriticalRule}"
            : $"CRITICAL: {analysis.Build.CriticalRule}";
        ApplyMapGuide(analysis.RolePlan);
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

    private LineupPick? GetPickForTab(int tabIndex)
    {
        var role = tabIndex switch { 1 => "carry", 2 => "mid", 3 => "support", _ => string.Empty };
        return _lineup.Picks.FirstOrDefault(pick => pick.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            ?? _lineup.Picks.ElementAtOrDefault(tabIndex - 1);
    }

    private ContentControl GetHeroContent(int tabIndex) => tabIndex switch
    {
        1 => Pos1Content,
        2 => Pos2Content,
        3 => Pos5Content,
        _ => throw new ArgumentOutOfRangeException(nameof(tabIndex))
    };

    private StackPanel GetHeroLoadingPanel(int tabIndex) => tabIndex switch
    {
        1 => Pos1LoadingPanel,
        2 => Pos2LoadingPanel,
        3 => Pos5LoadingPanel,
        _ => throw new ArgumentOutOfRangeException(nameof(tabIndex))
    };

    private StackPanel GetHeroErrorPanel(int tabIndex) => tabIndex switch
    {
        1 => Pos1ErrorPanel,
        2 => Pos2ErrorPanel,
        3 => Pos5ErrorPanel,
        _ => throw new ArgumentOutOfRangeException(nameof(tabIndex))
    };

    private TextBlock GetHeroErrorText(int tabIndex) => tabIndex switch
    {
        1 => Pos1ErrorText,
        2 => Pos2ErrorText,
        3 => Pos5ErrorText,
        _ => throw new ArgumentOutOfRangeException(nameof(tabIndex))
    };

    private void ShowHeroTabLoading(int tabIndex)
    {
        GetHeroContent(tabIndex).Visibility = Visibility.Collapsed;
        GetHeroErrorPanel(tabIndex).Visibility = Visibility.Collapsed;
        GetHeroLoadingPanel(tabIndex).Visibility = Visibility.Visible;
    }

    private void ShowHeroTabError(int tabIndex, string message)
    {
        GetHeroContent(tabIndex).Visibility = Visibility.Collapsed;
        GetHeroLoadingPanel(tabIndex).Visibility = Visibility.Collapsed;
        GetHeroErrorText(tabIndex).Text = message;
        GetHeroErrorPanel(tabIndex).Visibility = Visibility.Visible;
    }

    private void ResetHeroTabs()
    {
        _loadedHeroTabs.Clear();
        _loadingHeroTabs.Clear();
        foreach (var tabIndex in new[] { 1, 2, 3 })
        {
            GetHeroContent(tabIndex).Content = null;
            GetHeroContent(tabIndex).Visibility = Visibility.Collapsed;
            GetHeroErrorPanel(tabIndex).Visibility = Visibility.Collapsed;
            GetHeroLoadingPanel(tabIndex).Visibility = Visibility.Visible;
        }
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

    private async void AnalysisTabs_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || !ReferenceEquals(sender, AnalysisTabs) || AnalysisTabs.SelectedIndex <= 0)
        {
            return;
        }

        await LoadHeroTabAsync(AnalysisTabs.SelectedIndex, false);
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadOverviewAsync(false);

    private async void ReanalyzeButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadOverviewAsync(true);

    private async void Pos1RetryButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadHeroTabAsync(1, true);

    private async void Pos2RetryButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadHeroTabAsync(2, true);

    private async void Pos5RetryButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadHeroTabAsync(3, true);

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
