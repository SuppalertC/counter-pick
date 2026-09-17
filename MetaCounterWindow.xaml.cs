using System.Windows;
using DotaComboBoard.Models;
using DotaComboBoard.Services;

namespace DotaComboBoard;

public partial class MetaCounterWindow : Window
{
    private readonly MetaCounterService _metaCounterService = new();
    private readonly IReadOnlyList<RowConfig> _rows;
    private readonly bool _isThai;
    private bool _isLoading;

    public MetaCounterWindow(IReadOnlyList<RowConfig> rows, bool isThai)
    {
        InitializeComponent();
        _rows = rows;
        _isThai = isThai;
        ApplyLanguage();
        Loaded += async (_, _) => await LoadAsync(false);
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            var result = await _metaCounterService.AnalyzeAsync(_rows, _isThai, forceRefresh);
            foreach (var recommendation in result.Recommendations)
            {
                recommendation.PickRateText = _isThai
                    ? $"สัดส่วน Pick {recommendation.PickRate:0.00}%"
                    : $"Pick share {recommendation.PickRate:0.00}%";
                recommendation.WinRateText = $"Win {recommendation.WinRate:0.0}%";
                recommendation.MatchupScoreText = _isThai
                    ? $"ต้านได้ {recommendation.MatchupScore:0.0}%"
                    : $"{recommendation.MatchupScore:0.0}% vs";
                recommendation.PlanLabel = _isThai
                    ? $"แผนหลัก #{recommendation.PlanNumber}"
                    : $"PRIMARY PLAN #{recommendation.PlanNumber}";
                recommendation.BackupPlanLabel = _isThai
                    ? $"แผนสำรอง #{recommendation.BackupPlanNumber}"
                    : $"BACKUP #{recommendation.BackupPlanNumber}";
            }

            RecommendationItems.ItemsSource = result.Recommendations;
            FooterText.Text = _isThai
                ? $"แพตช์ {result.PatchNumber} • อัปเดต {result.RetrievedAt:dd/MM/yyyy HH:mm} • OpenDota matchup • Valve patch"
                : $"Patch {result.PatchNumber} • Updated {result.RetrievedAt:dd/MM/yyyy HH:mm} • OpenDota matchup • Valve patch";
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultScroll.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = _isThai ? $"โหลดข้อมูลไม่สำเร็จ: {exception.Message}" : exception.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _isLoading = false;
        }
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "แผนสู้เมตา" : "Meta Counter";
        TitleText.Text = _isThai ? "แผนสู้ฮีโร่เมตา" : "META COUNTER";
        SubtitleText.Text = _isThai
            ? "เลือกแผนใน Team Board ที่รับมือฮีโร่ยอดนิยมได้ดีที่สุดจาก matchup ปัจจุบัน"
            : "Best Team Board plan for each popular hero using current matchup data";
        RefreshButton.Content = _isThai ? "อัปเดตข้อมูล" : "REFRESH DATA";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        LoadingText.Text = _isThai ? "กำลังอ่านเมตาปัจจุบัน..." : "READING CURRENT META...";
        ErrorTitleText.Text = _isThai ? "โหลดข้อมูลเมตาไม่ได้" : "META DATA UNAVAILABLE";
        RetryButton.Content = _isThai ? "ลองอีกครั้ง" : "TRY AGAIN";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs eventArgs) => await LoadAsync(true);

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
