using System.Diagnostics;
using System.Windows;
using DotaComboBoard.Models;
using DotaComboBoard.Services;

namespace DotaComboBoard;

public partial class SettingsWindow : Window
{
    private const string AiStudioUrl = "https://aistudio.google.com/api-keys";
    private readonly bool _isThai;
    private readonly AppSettings _settings;
    private readonly GeminiConfig _geminiConfig;

    public SettingsWindow(bool isThai, GeminiConfig geminiConfig)
    {
        InitializeComponent();
        _isThai = isThai;
        _geminiConfig = geminiConfig;
        _settings = AppSettingsService.Load();
        ApiKeyBox.Password = _settings.GeminiApiKey;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "ตั้งค่า" : "Settings";
        TitleText.Text = _isThai ? "ตั้งค่า" : "SETTINGS";
        SubtitleText.Text = _isThai ? "ตั้งค่า Gemini API สำหรับวิเคราะห์ทีม 5v5 แบบเร็ว" : "Gemini API for fast 5v5 counter analysis";
        ApiHelpText.Text = _isThai ? "สร้าง API key ฟรีที่ Google AI Studio แล้วนำมาวางที่นี่" : "Create a free key in Google AI Studio, then paste it here.";
        StorageNoticeText.Text = _isThai ? "บันทึกเฉพาะบัญชี Windows นี้ใน LocalAppData" : "Saved only on this Windows account in LocalAppData.";
        OpenAiStudioButton.Content = _isThai ? "เปิด GOOGLE AI STUDIO" : "OPEN GOOGLE AI STUDIO";
        TestKeyButton.Content = _isThai ? "ทดสอบ KEY" : "TEST KEY";
        ClearKeyButton.Content = _isThai ? "ล้าง" : "CLEAR";
        CancelButton.Content = _isThai ? "ยกเลิก" : "CANCEL";
        SaveButton.Content = _isThai ? "บันทึก" : "SAVE SETTINGS";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
    }

    private void OpenAiStudioButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AiStudioUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai ? $"เปิดเบราว์เซอร์ไม่สำเร็จ: {exception.Message}" : $"Could not open browser: {exception.Message}";
        }
    }

    private async void TestKeyButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        TestKeyButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        TestProgressBar.Visibility = Visibility.Visible;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText");
        StatusText.Text = _isThai ? "กำลังทดสอบ Gemini API key..." : "Testing Gemini API key...";
        try
        {
            var result = await GeminiApiKeyService.TestAsync(ApiKeyBox.Password, _geminiConfig.Model);
            ApiKeyBox.Password = result.NormalizedKey;
            _settings.GeminiApiKey = result.NormalizedKey;
            AppSettingsService.Save(_settings);
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 208, 155));
            StatusText.Text = _isThai
                ? $"✓ KEY ใช้งานได้และบันทึกแล้ว • พบ {result.GenerateModelCount} โมเดล • ใช้ {result.RecommendedModel}"
                : $"✓ Key works and was saved • {result.GenerateModelCount} models • Using {result.RecommendedModel}";
        }
        catch (Exception exception)
        {
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 154, 145));
            StatusText.Text = _isThai
                ? $"✕ ทดสอบไม่สำเร็จ: {exception.Message}\nหากเป็น key เก่า ให้สร้าง Auth key ใหม่จาก Google AI Studio"
                : $"✕ Test failed: {exception.Message}\nCreate a new auth key in Google AI Studio if this is an old key.";
        }
        finally
        {
            TestKeyButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
            TestProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearKeyButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        ApiKeyBox.Clear();
        StatusText.Text = _isThai ? "ล้าง key แล้ว — กดบันทึกเพื่อยืนยัน" : "Key cleared. Save to confirm.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _settings.GeminiApiKey = GeminiApiKeyService.Normalize(ApiKeyBox.Password);
        AppSettingsService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
