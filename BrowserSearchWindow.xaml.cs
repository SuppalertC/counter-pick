using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DotaComboBoard.Services;
using MediaColor = System.Windows.Media.Color;

namespace DotaComboBoard;

public partial class BrowserSearchWindow : Window
{
    private readonly BrowserSyncServer _syncServer;
    private readonly bool _isThai;
    private bool _isSearching;

    public BrowserSearchWindow(BrowserSyncServer syncServer, bool isThai)
    {
        InitializeComponent();
        _syncServer = syncServer;
        _isThai = isThai;
        _syncServer.StatusChanged += SyncServer_StatusChanged;
        PairingCodeText.Text = _syncServer.PairingCode;
        ApplyLanguage();
        UpdateConnectionState();
        Closed += (_, _) => _syncServer.StatusChanged -= SyncServer_StatusChanged;
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "ค้นเว็บผ่าน Chrome Extension" : "Browser Search Sync";
        HeaderText.Text = _isThai ? "ค้นเว็บผ่าน CHROME EXTENSION" : "PUBLIC WEB SEARCH SYNC";
        HeaderDetailText.Text = _isThai
            ? "ส่งคำค้นผ่าน WebSocket ไปยัง extension และรับผลเว็บสาธารณะกลับมาเป็น JSON"
            : "Send searches to the extension over WebSocket and receive public web results as JSON.";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        PairingLabelText.Text = _isThai ? "รหัสจับคู่" : "PAIRING";
        CopyCodeButton.Content = _isThai ? "คัดลอก" : "COPY";
        OpenExtensionButton.Content = _isThai ? "เปิดโฟลเดอร์ EXTENSION" : "EXTENSION FOLDER";
        QueryBox.ToolTip = _isThai ? "เช่น Dota 2 7.41f Archon high win rate carry" : "Example: Dota 2 7.41f Archon high win rate carry";
        SearchButton.Content = _isThai ? "ค้นหา" : "SEARCH";
        SourcesLabelText.Text = _isThai ? "แหล่งข้อมูล" : "SOURCES";
        GeneralCheckBox.Content = _isThai ? "เว็บทั่วไป" : "General web";
        DisclaimerText.Text = _isThai
            ? "โหมดนี้ค้นเว็บสาธารณะเท่านั้น ไม่ควบคุม chatgpt.com และไม่อ่าน cookie หรือ session ของเบราว์เซอร์"
            : "This mode searches public web sources only. It does not control chatgpt.com or read browser cookies/sessions.";
    }

    private void UpdateConnectionState()
    {
        SearchButton.IsEnabled = _syncServer.IsConnected && !_isSearching;
        if (!string.IsNullOrWhiteSpace(_syncServer.ErrorMessage) && !_syncServer.IsConnected)
        {
            ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(239, 106, 91));
            ConnectionText.Text = _isThai ? "เปิด Browser Sync ไม่สำเร็จ" : "BROWSER SYNC ERROR";
            ConnectionDetailText.Text = _syncServer.ErrorMessage;
            return;
        }

        if (_syncServer.IsConnected)
        {
            ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(95, 208, 154));
            ConnectionText.Text = _isThai ? "เชื่อมต่อ Extension แล้ว" : "EXTENSION CONNECTED";
            ConnectionDetailText.Text = $"{_syncServer.ExtensionName} • ws://127.0.0.1:{BrowserSyncServer.Port}/dota-sync";
        }
        else
        {
            ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(244, 196, 106));
            ConnectionText.Text = _isThai ? "กำลังรอ Extension" : "WAITING FOR EXTENSION";
            ConnectionDetailText.Text = _isThai
                ? "โหลด extension จากโฟลเดอร์ด้านขวา แล้วใส่รหัสจับคู่ 6 หลัก"
                : "Load the unpacked extension, then enter the six-digit pairing code.";
        }
    }

    private async Task SearchAsync()
    {
        if (_isSearching)
        {
            return;
        }

        var sources = new List<string>();
        if (ValveCheckBox.IsChecked == true) sources.Add("valve");
        if (D2PtCheckBox.IsChecked == true) sources.Add("d2pt");
        if (DotabuffCheckBox.IsChecked == true) sources.Add("dotabuff");
        if (GeneralCheckBox.IsChecked == true) sources.Add("general");
        if (string.IsNullOrWhiteSpace(QueryBox.Text) || sources.Count == 0)
        {
            SearchStatusText.Text = _isThai ? "กรอกคำค้นและเลือกอย่างน้อยหนึ่งแหล่งข้อมูล" : "Enter a query and select at least one source.";
            return;
        }

        _isSearching = true;
        SearchButton.IsEnabled = false;
        SearchStatusText.Text = _isThai ? "กำลังส่งคำค้นไปยัง Chrome Extension..." : "Sending search to Chrome Extension...";
        try
        {
            var response = await _syncServer.SearchAsync(QueryBox.Text, sources, 40);
            ResultsItems.ItemsSource = response.Results;
            SearchStatusText.Text = _isThai
                ? $"พบ {response.Results.Count} รายการ • {response.RetrievedAt.ToLocalTime():HH:mm:ss}"
                : $"{response.Results.Count} results • {response.RetrievedAt.ToLocalTime():HH:mm:ss}";
        }
        catch (Exception exception)
        {
            SearchStatusText.Text = _isThai ? $"ค้นหาไม่สำเร็จ: {exception.Message}" : $"Search failed: {exception.Message}";
        }
        finally
        {
            _isSearching = false;
            UpdateConnectionState();
        }
    }

    private void SyncServer_StatusChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.Invoke(UpdateConnectionState);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs eventArgs) => await SearchAsync();

    private async void QueryBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            eventArgs.Handled = true;
            await SearchAsync();
        }
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(_syncServer.PairingCode);
        SearchStatusText.Text = _isThai ? "คัดลอกรหัสจับคู่แล้ว" : "Pairing code copied.";
    }

    private void OpenExtensionButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var extensionDirectory = Path.Combine(AppContext.BaseDirectory, "chrome-extension");
        if (!Directory.Exists(extensionDirectory))
        {
            SearchStatusText.Text = _isThai ? "ไม่พบโฟลเดอร์ chrome-extension" : "chrome-extension folder was not found.";
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{extensionDirectory}\"") { UseShellExecute = true });
    }

    private void OpenResultButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
