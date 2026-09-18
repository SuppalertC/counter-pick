using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DotaComboBoard.Models;
using DotaComboBoard.Services;

namespace DotaComboBoard;

public partial class HeroCounterWindow : Window
{
    private readonly HeroMetaService _metaService = new();
    private readonly TeamCounterService _teamCounterService;
    private readonly bool _isThai;
    private readonly List<HeroDirectoryEntry> _selectedEnemies = [];
    private IReadOnlyList<HeroDirectoryEntry> _heroes = [];

    public HeroCounterWindow(bool isThai, GeminiConfig config)
    {
        InitializeComponent();
        _isThai = isThai;
        _teamCounterService = new TeamCounterService(_metaService, config);
        ApplyLanguage();
        UpdateEnemySelection();
        Loaded += async (_, _) =>
        {
            FitToCurrentScreen();
            await LoadHeroesAsync();
        };
    }

    private void FitToCurrentScreen()
    {
        var ownerHandle = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(ownerHandle);
        var availableWidth = screen.WorkingArea.Width;
        var availableHeight = screen.WorkingArea.Height;

        MinWidth = Math.Min(820, availableWidth);
        MinHeight = Math.Min(600, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(1180, Math.Max(MinWidth, availableWidth - 32));
        Height = Math.Min(820, Math.Max(MinHeight, availableHeight - 32));
        Left = screen.WorkingArea.Left + (availableWidth - Width) / 2;
        Top = screen.WorkingArea.Top + (availableHeight - Height) / 2;
    }

    private async Task LoadHeroesAsync()
    {
        try
        {
            _heroes = await _metaService.GetCatalogAsync();
            ApplyFilter();
            FooterText.Text = _isThai
                ? $"ฮีโร่ {_heroes.Count} ตัว • ข้อมูล matchup จาก OpenDota • cache 6 ชั่วโมง"
                : $"{_heroes.Count} heroes • OpenDota matchup data • 6-hour cache";
        }
        catch (Exception exception)
        {
            EmptyResultText.Text = exception.Message;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var normalizedQuery = NormalizeSearch(query);
        var filtered = _heroes.Where(hero => string.IsNullOrWhiteSpace(query)
            || hero.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || hero.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
            || NormalizeSearch(hero.Name).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || NormalizeSearch(hero.Key).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)).ToList();
        StrengthItems.ItemsSource = filtered.Where(hero => hero.PrimaryAttribute == "str");
        AgilityItems.ItemsSource = filtered.Where(hero => hero.PrimaryAttribute == "agi");
        IntelligenceItems.ItemsSource = filtered.Where(hero => hero.PrimaryAttribute == "int");
        UniversalItems.ItemsSource = filtered.Where(hero => hero.PrimaryAttribute == "all");
        var selectedIds = _selectedEnemies.Select(hero => hero.Id).ToHashSet();
        var teamFiltered = filtered.Where(hero => !selectedIds.Contains(hero.Id)).ToList();
        TeamStrengthItems.ItemsSource = teamFiltered.Where(hero => hero.PrimaryAttribute == "str");
        TeamAgilityItems.ItemsSource = teamFiltered.Where(hero => hero.PrimaryAttribute == "agi");
        TeamIntelligenceItems.ItemsSource = teamFiltered.Where(hero => hero.PrimaryAttribute == "int");
        TeamUniversalItems.ItemsSource = teamFiltered.Where(hero => hero.PrimaryAttribute == "all");
        SearchStatusText.Text = string.IsNullOrWhiteSpace(query)
            ? _isThai ? $"พร้อมค้นหา {_heroes.Count} ฮีโร่" : $"Ready to search {_heroes.Count} heroes"
            : filtered.Count == 0
                ? _isThai ? $"ไม่พบฮีโร่ “{query}”" : $"No hero found for “{query}”"
                : _isThai ? $"พบ {filtered.Count} ฮีโร่สำหรับ “{query}”" : $"Found {filtered.Count} hero(es) for “{query}”";
    }

    private static string NormalizeSearch(string value)
    {
        return new string(value
            .Where(character => !char.IsWhiteSpace(character) && character is not '-' and not '_' and not '\'')
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private void TeamHeroButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.Button { Tag: HeroDirectoryEntry hero }
            || _selectedEnemies.Count >= 5
            || _selectedEnemies.Any(selected => selected.Id == hero.Id))
        {
            return;
        }

        _selectedEnemies.Add(hero);
        ResetTeamResult();
        UpdateEnemySelection();
        ApplyFilter();
    }

    private void RemoveEnemyButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.Button { Tag: HeroDirectoryEntry hero })
        {
            return;
        }

        _selectedEnemies.RemoveAll(selected => selected.Id == hero.Id);
        ResetTeamResult();
        UpdateEnemySelection();
        ApplyFilter();
    }

    private void ClearTeamButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _selectedEnemies.Clear();
        ResetTeamResult();
        UpdateEnemySelection();
        ApplyFilter();
    }

    private async void AnalyzeTeamButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_selectedEnemies.Count != 5)
        {
            return;
        }

        AnalyzeTeamButton.IsEnabled = false;
        ClearTeamButton.IsEnabled = false;
        TeamPickerPanel.Visibility = Visibility.Collapsed;
        TeamResultContainer.Visibility = Visibility.Visible;
        TeamEmptyResultText.Visibility = Visibility.Collapsed;
        TeamResultScroller.Visibility = Visibility.Collapsed;
        TeamLoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var result = await _teamCounterService.AnalyzeAsync(_selectedEnemies, _isThai);
            AiSummaryHeaderText.Text = result.UsedAi
                ? _isThai ? "GEMINI FAST ANALYSIS" : "GEMINI FAST ANALYSIS"
                : _isThai ? "วิเคราะห์คะแนนแบบเร็ว" : "FAST SCORE ANALYSIS";
            AiSummaryText.Text = result.AiSummary;
            RecommendedHeroItems.ItemsSource = result.RecommendedHeroes;
            RecommendedTeamItems.ItemsSource = result.Teams;
            TeamLoadingPanel.Visibility = Visibility.Collapsed;
            TeamResultScroller.Visibility = Visibility.Visible;
            TeamResultScroller.ScrollToTop();
        }
        catch (Exception exception)
        {
            TeamLoadingPanel.Visibility = Visibility.Collapsed;
            TeamEmptyResultText.Text = _isThai ? $"วิเคราะห์ 5v5 ไม่สำเร็จ: {exception.Message}" : $"5v5 analysis failed: {exception.Message}";
            TeamEmptyResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            AnalyzeTeamButton.IsEnabled = _selectedEnemies.Count == 5;
            ClearTeamButton.IsEnabled = true;
        }
    }

    private void UpdateEnemySelection()
    {
        SelectedEnemyItems.ItemsSource = null;
        SelectedEnemyItems.ItemsSource = _selectedEnemies.ToList();
        SelectedCountText.Text = _isThai
            ? $"เลือกแล้ว {_selectedEnemies.Count}/5 • กดฮีโร่ที่เลือกเพื่อลบ"
            : $"Selected {_selectedEnemies.Count}/5 • Click a selected hero to remove";
        AnalyzeTeamButton.IsEnabled = _selectedEnemies.Count == 5;
    }

    private void ResetTeamResult()
    {
        TeamPickerPanel.Visibility = Visibility.Visible;
        TeamResultContainer.Visibility = Visibility.Collapsed;
        TeamLoadingPanel.Visibility = Visibility.Collapsed;
        TeamResultScroller.Visibility = Visibility.Collapsed;
        TeamEmptyResultText.Text = _isThai
            ? "เลือกฮีโร่ศัตรู 5 ตัว แล้วกดวิเคราะห์ทั้งทีม"
            : "Select five enemy heroes, then analyze the full matchup.";
        TeamEmptyResultText.Visibility = Visibility.Visible;
    }

    private void BackToTeamPickerButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        TeamResultContainer.Visibility = Visibility.Collapsed;
        TeamPickerPanel.Visibility = Visibility.Visible;
    }

    private async void HeroButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.Button { Tag: HeroDirectoryEntry hero })
        {
            return;
        }

        EmptyResultText.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var result = await _metaService.GetMatchupsAsync(hero);
            foreach (var matchup in result.Counters.Concat(result.Advantages))
            {
                matchup.Summary = _isThai
                    ? $"ชนะ {matchup.SelectedHeroWinRate:0.0}% • {matchup.GamesPlayed:N0} เกม"
                    : $"Win {matchup.SelectedHeroWinRate:0.0}% • {matchup.GamesPlayed:N0} games";
            }

            SelectedHeroIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(hero.IconPath));
            SelectedHeroText.Text = hero.Name;
            SelectedHeroMetaText.Text = _isThai
                ? $"Pick {hero.PickRate:0.00}% • Win {hero.WinRate:0.0}%"
                : $"Pick {hero.PickRate:0.00}% • Win {hero.WinRate:0.0}%";
            CounterItems.ItemsSource = result.Counters;
            AdvantageItems.ItemsSource = result.Advantages;
            MatchupSourceText.Text = _isThai
                ? "OpenDota ไม่ตอบสนอง — ยังไม่มีข้อมูล matchup ของฮีโร่นี้ กรุณาลองใหม่ภายหลัง"
                : "OpenDota is unavailable — matchup data for this hero is not cached yet.";
            MatchupSourceText.Visibility = result.IsOfflineFallback ? Visibility.Visible : Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyResultText.Text = exception.Message;
            EmptyResultText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "ค้นหาฮีโร่แก้ทาง" : "Hero Counter Finder";
        TitleText.Text = _isThai ? "ค้นหาฮีโร่แก้ทาง" : "HERO COUNTER FINDER";
        SubtitleText.Text = _isThai ? "ค้นหาแบบตัวต่อตัว หรือเลือกศัตรู 5 ตัวเพื่อจัดทีมสวน" : "Search one hero or build a full 5v5 counter draft.";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        SearchBox.ToolTip = _isThai ? "พิมพ์ชื่อฮีโร่" : "Type a hero name";
        SearchButton.Content = _isThai ? "ค้นหา" : "SEARCH";
        ClearSearchButton.ToolTip = _isThai ? "ล้างคำค้นหา" : "Clear search";
        SingleModeTab.Header = _isThai ? "แก้ทาง 1 ตัว" : "1 HERO";
        TeamModeTab.Header = "5v5 VERSUS";
        EmptyResultText.Text = _isThai ? "เลือกฮีโร่เพื่อดูตัวแก้ทางและตัวที่เราได้เปรียบ" : "Select a hero to view counters and favorable matchups.";
        LoadingText.Text = _isThai ? "กำลังโหลด matchup..." : "LOADING MATCHUPS...";
        CounterHeaderText.Text = _isThai ? "เสียเปรียบเมื่อเจอ" : "DISADVANTAGED VS";
        AdvantageHeaderText.Text = _isThai ? "ได้เปรียบเมื่อเจอ" : "ADVANTAGED VS";
        EnemyTeamHeaderText.Text = _isThai ? "ทีมศัตรู — เลือกให้ครบ 5 ตัว" : "ENEMY TEAM — SELECT 5";
        ClearTeamButton.Content = _isThai ? "ล้าง" : "CLEAR";
        AnalyzeTeamButton.Content = _isThai ? "วิเคราะห์ 5v5" : "ANALYZE 5v5";
        TeamLoadingText.Text = _isThai ? "กำลังรวม Win Rate + Team Counter + Gemini..." : "SCORING WIN RATE + TEAM COUNTER + GEMINI...";
        ResultTitleText.Text = _isThai ? "ผลวิเคราะห์ทีมสวน 5v5" : "5v5 COUNTER RESULT";
        BackToTeamPickerButton.Content = _isThai ? "← กลับไปเลือกฮีโร่" : "← BACK TO HEROES";
        RecommendedHeroesHeaderText.Text = _isThai ? "ฮีโร่ที่ควรหยิบสวนมากที่สุด" : "BEST COUNTER PICKS";
        RecommendedTeamsHeaderText.Text = _isThai ? "3 ทีมที่เหมาะปะทะทั้งชุด" : "TOP 3 COUNTER TEAMS";
        ResetTeamResult();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == System.Windows.Input.Key.Enter)
        {
            ApplyFilter();
            eventArgs.Handled = true;
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs eventArgs) => ApplyFilter();

    private void ClearSearchButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
