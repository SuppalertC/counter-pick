using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DotaComboBoard.Models;
using DotaComboBoard.Services;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;

namespace DotaComboBoard;

public partial class MainWindow : Window
{
    private readonly string _boardPath = Path.Combine(AppContext.BaseDirectory, "data", "board.json");
    private readonly string _heroAssetPath = Path.Combine(AppContext.BaseDirectory, "assets", "heroes");
    private readonly DispatcherTimer _reloadTimer;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly BrowserSyncServer _browserSyncServer;
    private readonly McpServer _mcpServer;
    private Forms.ToolStripItem? _trayOpenItem;
    private Forms.ToolStripItem? _trayRefreshItem;
    private Forms.ToolStripItem? _trayStartupItem;
    private Forms.ToolStripItem? _trayExitItem;
    private FileSystemWatcher? _watcher;
    private readonly AppSettings _settings;
    private BoardConfig _config = new();
    private bool _exitRequested;
    private bool _isThai = true;
    private bool _isRefreshingData;
    private string _currentPatchNumber = string.Empty;
    private bool _patchLookupFailed;
    private RankComboMode _rankComboMode;

    private enum RankComboMode
    {
        All,
        Archon,
        Legend
    }

    public MainWindow()
    {
        InitializeComponent();

        _browserSyncServer = new BrowserSyncServer();
        _browserSyncServer.StatusChanged += BrowserSyncServer_StatusChanged;
        _mcpServer = new McpServer(_boardPath, _browserSyncServer, () => _currentPatchNumber);
        _mcpServer.StatusChanged += McpServer_StatusChanged;

        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            LoadBoard();
        };

        _settings = AppSettingsService.Load();
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshMetaDataAsync(false);

        _trayIcon = CreateTrayIcon();
        Loaded += async (_, _) =>
        {
            _browserSyncServer.Start();
            _mcpServer.Start();
            LoadBoard();
            WatchBoardFile();
            _trayIcon.Visible = true;
            ApplyAutoRefreshState();
            if (_settings.AutoRefreshData)
            {
                await RefreshMetaDataAsync(false);
            }
            else
            {
                await LoadPatchVersionAsync(false);
            }
        };
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _trayOpenItem = menu.Items.Add("Open", null, (_, _) => ShowWindow());
        _trayRefreshItem = menu.Items.Add("Refresh JSON", null, (_, _) => Dispatcher.Invoke(LoadBoard));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _trayStartupItem = menu.Items.Add("Start with Windows", null, (_, _) => Dispatcher.Invoke(ToggleStartup));
        _trayExitItem = menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var icon = new Forms.NotifyIcon
        {
            Icon = GetApplicationIcon(),
            Text = "Dota 2 Team Board",
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        return icon;
    }

    private static System.Drawing.Icon GetApplicationIcon()
    {
        return Environment.ProcessPath is { } processPath
            ? System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application
            : System.Drawing.SystemIcons.Application;
    }

    private void LoadBoard()
    {
        try
        {
            _config = BoardLoader.Load(_boardPath);
            ApplyLanguageText();
            ApplyWindowConfig();
            Topmost = _config.Window.AlwaysOnTop;
            RenderBoard();
            UpdateStartupMenuState();
            UpdateAutoUpdateButton();
            UpdateBoardStatus();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"อ่าน JSON ไม่สำเร็จ: {exception.Message}"
                : $"Could not read JSON: {exception.Message}";
        }
    }

    private void ApplyWindowConfig()
    {
        var screens = Forms.Screen.AllScreens;
        var screenIndex = Math.Clamp(_config.Window.TargetScreen - 1, 0, screens.Length - 1);
        var targetScreen = screens[screenIndex];
        var scale = GetScreenScale(targetScreen);
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = targetScreen.WorkingArea.Left / scale;
        Top = targetScreen.WorkingArea.Top / scale;

        if (_config.Window.FitToWorkArea)
        {
            Width = targetScreen.WorkingArea.Width / scale;
            Height = targetScreen.WorkingArea.Height / scale;
        }
        else
        {
            Width = Math.Max(MinWidth, _config.Window.Width);
            Height = Math.Max(MinHeight, _config.Window.Height);
        }

        ResizeMode = _config.Window.LockSize ? ResizeMode.NoResize : ResizeMode.CanResize;
    }

    private static double GetScreenScale(Forms.Screen screen)
    {
        var point = new NativePoint(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
        var monitor = MonitorFromPoint(point, 2);
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }

    private void RenderBoard()
    {
        var rows = GetActiveRows();
        BoardPanel.Children.Clear();
        BoardPanel.Children.Add(CreateRow(_config.Columns.Select(column => (column, (JsonElement?)null)), true, 0));

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            BoardPanel.Children.Add(CreateRow(
                _config.Columns.Select(column =>
                    (column, row.Cells.TryGetValue(column.Key, out var value) ? value : (JsonElement?)null)),
                false,
                rowIndex,
                row));
        }
    }

    private IReadOnlyList<RowConfig> GetActiveRows()
    {
        return _rankComboMode switch
        {
            RankComboMode.Archon when _config.RankCombos.Archon.Count > 0 => _config.RankCombos.Archon,
            RankComboMode.Legend when _config.RankCombos.Legend.Count > 0 => _config.RankCombos.Legend,
            _ => _config.Rows
        };
    }

    private void UpdateBoardStatus()
    {
        var mode = _rankComboMode switch
        {
            RankComboMode.Archon => "ARCHON",
            RankComboMode.Legend => "LEGEND",
            _ => "ALL"
        };
        StatusText.Text = _isThai
            ? $"โหลด {GetActiveRows().Count} ชุด • โหมด {mode} • {DateTime.Now:HH:mm:ss}"
            : $"Loaded {GetActiveRows().Count} lineups • {mode} mode • {DateTime.Now:HH:mm:ss}";
    }

    private FrameworkElement CreateRow(
        IEnumerable<(ColumnConfig Column, JsonElement? Value)> cells,
        bool isHeader,
        int rowIndex,
        RowConfig? sourceRow = null)
    {
        var normalBackground = isHeader
            ? new SolidColorBrush(MediaColor.FromRgb(33, 40, 49))
            : rowIndex % 2 == 0
                ? (MediaBrush)FindResource("PanelBackground")
                : (MediaBrush)FindResource("PanelAlternate");
        var grid = new Grid
        {
            Background = sourceRow is null ? normalBackground : System.Windows.Media.Brushes.Transparent
        };

        var columnIndex = 0;
        foreach (var (column, value) in cells)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1, column.Width), GridUnitType.Star)
            });
            var content = isHeader ? CreateHeaderCell(GetColumnLabel(column)) : CreateDataCell(value);
            Grid.SetColumn(content, columnIndex++);
            grid.Children.Add(content);
        }

        if (sourceRow is null)
        {
            return grid;
        }

        var rowButton = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("TableRowButtonStyle"),
            Background = normalBackground,
            Content = grid,
            ToolTip = _isThai ? "คลิกเพื่อให้ Gemini วิเคราะห์ทีม" : "Click for Gemini lineup analysis"
        };
        System.Windows.Automation.AutomationProperties.SetName(rowButton, GetLineupAutomationName(sourceRow));
        rowButton.Click += (_, _) => OpenAnalysis(sourceRow);
        return rowButton;
    }

    private static string GetLineupAutomationName(RowConfig row)
    {
        if (!row.Cells.TryGetValue("pick", out var pickCell)
            || !pickCell.TryGetProperty("picks", out var picks))
        {
            return "Open Gemini lineup analysis";
        }

        var names = picks.EnumerateArray().Select(pick => GetString(pick, "name"));
        return $"Analyze {string.Join(" + ", names)}";
    }

    private void OpenAnalysis(RowConfig row)
    {
        if (!_config.Gemini.Enabled)
        {
            StatusText.Text = _isThai
                ? "ปิดการวิเคราะห์ Gemini ไว้ใน board.json"
                : "Gemini analysis is disabled in board.json.";
            return;
        }

        try
        {
            var analysisWindow = new AnalysisWindow(row, _config.Gemini, _isThai) { Owner = this };
            analysisWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปิดหน้าวิเคราะห์ไม่สำเร็จ: {exception.Message}"
                : $"Could not open analysis: {exception.Message}";
        }
    }

    private FrameworkElement CreateHeaderCell(string label)
    {
        return new Border
        {
            BorderBrush = (MediaBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Foreground = (MediaBrush)FindResource("SecondaryText")
            }
        };
    }

    private FrameworkElement CreateDataCell(JsonElement? value)
    {
        var border = new Border
        {
            BorderBrush = (MediaBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(10, 7, 10, 7),
            MinHeight = 52
        };

        if (value is { ValueKind: JsonValueKind.Object } objectValue
            && objectValue.TryGetProperty("picks", out var picks)
            && picks.ValueKind == JsonValueKind.Array)
        {
            border.Child = CreateLineupCell(picks);
        }
        else if (value is { ValueKind: JsonValueKind.Object } detailValue
            && detailValue.TryGetProperty("specialty", out _))
        {
            border.Child = CreateDetailCell(detailValue);
        }
        else if (value is { ValueKind: JsonValueKind.Object } heroValue
            && heroValue.TryGetProperty("hero", out var hero))
        {
            border.Child = CreateHeroCell(hero.GetString() ?? string.Empty, GetString(heroValue, "text"));
        }
        else
        {
            border.Child = new TextBlock
            {
                Text = GetDisplayText(value),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (MediaBrush)FindResource("PrimaryText")
            };
        }

        return border;
    }

    private FrameworkElement CreateLineupCell(JsonElement picks)
    {
        var grid = new Grid();
        var pickIndex = 0;

        foreach (var pick in picks.EnumerateArray())
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var card = new Border
            {
                BorderBrush = (MediaBrush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(pickIndex == 0 ? 0 : 1, 0, 0, 0),
                Padding = new Thickness(6, 1, 6, 1)
            };
            var content = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = GetString(pick, "name"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (MediaBrush)FindResource("PrimaryText")
            });

            var image = CreateHeroImage(GetString(pick, "hero"), 42);
            image.Margin = new Thickness(0, 3, 0, 3);
            content.Children.Add(image);
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(37, 47, 58)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 1, 7, 1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = LocalizeRole(GetString(pick, "role")).ToUpperInvariant(),
                    FontSize = 11,
                    Foreground = (MediaBrush)FindResource("SecondaryText")
                }
            });

            card.Child = content;
            Grid.SetColumn(card, pickIndex++);
            grid.Children.Add(card);
        }

        return grid;
    }

    private FrameworkElement CreateDetailCell(JsonElement detail)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = GetLocalizedString(detail, "specialty"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (MediaBrush)FindResource("AccentBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = GetLocalizedString(detail, "winCondition"),
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (MediaBrush)FindResource("PrimaryText")
        });
        return panel;
    }

    private FrameworkElement CreateHeroImage(string hero, double size)
    {
        var imagePath = Path.Combine(_heroAssetPath, $"{hero}.png");
        if (!File.Exists(imagePath))
        {
            return new Border { Width = size, Height = size };
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        return new WpfImage
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private FrameworkElement CreateHeroCell(string hero, string text)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(CreateHeroImage(hero, 32));

        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text) ? hero : text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (MediaBrush)FindResource("PrimaryText")
        };
        Grid.SetColumn(label, 1);
        panel.Children.Add(label);
        return panel;
    }

    private static string GetString(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property) ? property.GetString() ?? string.Empty : string.Empty;
    }

    private string GetLocalizedString(JsonElement value, string propertyName)
    {
        if (_isThai && value.TryGetProperty($"{propertyName}Th", out var thaiProperty))
        {
            return thaiProperty.GetString() ?? string.Empty;
        }

        return GetString(value, propertyName);
    }

    private string GetColumnLabel(ColumnConfig column)
    {
        return _isThai && !string.IsNullOrWhiteSpace(column.LabelTh) ? column.LabelTh : column.Label;
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

    private static string GetDisplayText(JsonElement? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => value.Value.ToString()
        };
    }

    private void WatchBoardFile()
    {
        var directory = Path.GetDirectoryName(_boardPath);
        if (directory is null)
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_boardPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += QueueReload;
        _watcher.Created += QueueReload;
        _watcher.Renamed += QueueReload;
    }

    private void QueueReload(object sender, FileSystemEventArgs eventArgs)
    {
        Dispatcher.Invoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void ToggleStartup()
    {
        try
        {
            StartupService.SetEnabled(!StartupService.IsEnabled());
            UpdateStartupMenuState();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปลี่ยนค่าเริ่มพร้อม Windows ไม่สำเร็จ: {exception.Message}"
                : $"Could not change startup setting: {exception.Message}";
        }
    }

    private void UpdateStartupMenuState()
    {
        var enabled = StartupService.IsEnabled();
        StartupMenuButton.Content = _isThai
            ? enabled ? "เปิดพร้อม Windows: เปิด" : "เปิดพร้อม Windows: ปิด"
            : enabled ? "Start with Windows: ON" : "Start with Windows: OFF";
        if (_trayStartupItem is not null)
        {
            _trayStartupItem.Text = StartupMenuButton.Content.ToString();
        }
    }

    private void ApplyAutoRefreshState()
    {
        if (_settings.AutoRefreshData)
        {
            _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }

        UpdateAutoUpdateButton();
    }

    private void UpdateAutoUpdateButton()
    {
        AutoUpdateButton.Content = _isThai
            ? _settings.AutoRefreshData ? "AUTO DATA: เปิด" : "AUTO DATA: ปิด"
            : _settings.AutoRefreshData ? "AUTO DATA: ON" : "AUTO DATA: OFF";
        AutoUpdateButton.ToolTip = _isThai
            ? "อัปเดต patch, meta และ counter ทุก 6 ชั่วโมง"
            : "Refresh patch, meta, and counters every 6 hours";
    }

    private async Task ToggleAutoRefreshAsync()
    {
        _settings.AutoRefreshData = !_settings.AutoRefreshData;
        AppSettingsService.Save(_settings);
        ApplyAutoRefreshState();
        StatusText.Text = _isThai
            ? _settings.AutoRefreshData ? "เปิดอัปเดตข้อมูลอัตโนมัติทุก 6 ชั่วโมงแล้ว" : "ปิดอัปเดตข้อมูลอัตโนมัติแล้ว"
            : _settings.AutoRefreshData ? "Automatic 6-hour data refresh enabled." : "Automatic data refresh disabled.";
        if (_settings.AutoRefreshData)
        {
            await RefreshMetaDataAsync(false);
        }
    }

    private async Task RefreshMetaDataAsync(bool forceRefresh)
    {
        if (_isRefreshingData || GetActiveRows().Count == 0)
        {
            return;
        }

        _isRefreshingData = true;
        RefreshDataMenuButton.IsEnabled = false;
        StatusText.Text = _isThai ? "กำลังอัปเดต patch, meta และ counter..." : "Refreshing patch, meta, and counter data...";
        try
        {
            var result = await new MetaCounterService().AnalyzeAsync(GetActiveRows(), _isThai, forceRefresh);
            _currentPatchNumber = result.PatchNumber;
            _patchLookupFailed = false;
            UpdateVersionTags();
            StatusText.Text = _isThai
                ? $"อัปเดตข้อมูลแพตช์ {result.PatchNumber} แล้ว • {DateTime.Now:HH:mm:ss}"
                : $"Patch {result.PatchNumber} data updated • {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            _patchLookupFailed = string.IsNullOrWhiteSpace(_currentPatchNumber);
            UpdateVersionTags();
            StatusText.Text = _isThai
                ? $"อัปเดตข้อมูลไม่สำเร็จ: {exception.Message}"
                : $"Data refresh failed: {exception.Message}";
        }
        finally
        {
            RefreshDataMenuButton.IsEnabled = true;
            _isRefreshingData = false;
            if (_settings.AutoRefreshData)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Start();
            }
        }
    }

    private async Task LoadPatchVersionAsync(bool forceRefresh)
    {
        _patchLookupFailed = false;
        UpdateVersionTags();
        try
        {
            _currentPatchNumber = await new DotaPatchContextService().GetCurrentPatchNumberAsync(forceRefresh);
        }
        catch
        {
            _patchLookupFailed = true;
        }

        UpdateVersionTags();
    }

    private void UpdateVersionTags()
    {
        AppVersionTagText.Text = $"APP v{AppVersionInfo.Current}";
        RankModeButton.Content = _rankComboMode switch
        {
            RankComboMode.Archon => "RANK: ARCHON",
            RankComboMode.Legend => "RANK: LEGEND",
            _ => "COMBO: ALL"
        };
        RankModeButton.ToolTip = _isThai
            ? $"คลิกเพื่อสลับ ALL / ARCHON / LEGEND • อ้างอิงแพตช์ {_config.RankCombos.Patch} • คัดจาก win rate รายฮีโร่"
            : $"Cycle ALL / ARCHON / LEGEND • Patch {_config.RankCombos.Patch} • Curated from individual hero win rates";
        DotaPatchTagText.Text = !string.IsNullOrWhiteSpace(_currentPatchNumber)
            ? $"DOTA {_currentPatchNumber}"
            : _patchLookupFailed ? "DOTA PATCH OFFLINE" : "DOTA PATCH …";
        DotaPatchTagText.ToolTip = _isThai
            ? "เวอร์ชันแพตช์ล่าสุดจาก Valve"
            : "Latest patch version from Valve";
        UpdateWebSyncTag();
    }

    private void UpdateWebSyncTag()
    {
        WebSyncButton.Content = _browserSyncServer.IsConnected
            ? "WEB: LINKED"
            : string.IsNullOrWhiteSpace(_browserSyncServer.ErrorMessage) ? "WEB: WAITING" : "WEB: ERROR";
        WebSyncButton.ToolTip = _browserSyncServer.IsConnected
            ? (_isThai ? $"เชื่อมต่อ {_browserSyncServer.ExtensionName} แล้ว • คลิกเพื่อค้นเว็บ" : $"Connected to {_browserSyncServer.ExtensionName} • Click to search the web")
            : !string.IsNullOrWhiteSpace(_browserSyncServer.ErrorMessage)
                ? (_isThai ? $"เปิด Web Sync ไม่สำเร็จ: {_browserSyncServer.ErrorMessage}" : $"Web Sync failed: {_browserSyncServer.ErrorMessage}")
                : (_isThai ? $"รอ Chrome Extension ที่ 127.0.0.1:{BrowserSyncServer.Port} • คลิกเพื่อจับคู่" : $"Waiting for Chrome Extension on 127.0.0.1:{BrowserSyncServer.Port} • Click to pair");
    }

    private void BrowserSyncServer_StatusChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.Invoke(UpdateWebSyncTag);
    }

    private void McpServer_StatusChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.Invoke(UpdateMcpMenuText);
    }

    private void UpdateMcpMenuText()
    {
        McpMenuTitleText.Text = _mcpServer.IsRunning ? "LOCAL MCP: READY" : "LOCAL MCP: ERROR";
        McpMenuDetailText.Text = _mcpServer.IsRunning
            ? (_isThai ? $"รับคำสั่งที่ {McpServer.Endpoint} • {_mcpServer.RequestCount} requests" : $"Listening at {McpServer.Endpoint} • {_mcpServer.RequestCount} requests")
            : (_isThai ? $"เปิด server ไม่สำเร็จ: {_mcpServer.ErrorMessage}" : $"Could not start server: {_mcpServer.ErrorMessage}");
    }

    private void OpenMetaCounter()
    {
        NavigationPopup.IsOpen = false;
        try
        {
            new MetaCounterWindow(GetActiveRows(), _isThai) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปิดหน้าแผนสู้เมตาไม่สำเร็จ: {exception.Message}"
                : $"Could not open Meta Counter: {exception.Message}";
        }
    }

    private void OpenHeroCounter()
    {
        NavigationPopup.IsOpen = false;
        try
        {
            new HeroCounterWindow(_isThai, _config.Gemini) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปิดหน้าค้นหาฮีโร่แก้ทางไม่สำเร็จ: {exception.Message}"
                : $"Could not open Hero Counter Finder: {exception.Message}";
        }
    }

    private void ShowWindow()
    {
        Show();
        ApplyWindowConfig();
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _watcher?.Dispose();
        _autoRefreshTimer.Stop();
        _mcpServer.StatusChanged -= McpServer_StatusChanged;
        _mcpServer.Dispose();
        _browserSyncServer.StatusChanged -= BrowserSyncServer_StatusChanged;
        _browserSyncServer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs eventArgs)
    {
        if (WindowState == WindowState.Minimized && _config.Window.HideOnMinimize)
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_exitRequested)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private async void AutoUpdateButton_Click(object sender, RoutedEventArgs eventArgs) => await ToggleAutoRefreshAsync();

    private void MenuButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        NavigationPopup.IsOpen = !NavigationPopup.IsOpen;
    }

    private void MetaCounterMenuItem_Click(object sender, RoutedEventArgs eventArgs) => OpenMetaCounter();

    private void HeroCounterMenuItem_Click(object sender, RoutedEventArgs eventArgs) => OpenHeroCounter();

    private void ComboJsonMenuItem_Click(object sender, RoutedEventArgs eventArgs)
    {
        NavigationPopup.IsOpen = false;
        try
        {
            new ComboJsonWindow(_boardPath, _isThai) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai ? $"เปิดหน้า Combo JSON ไม่สำเร็จ: {exception.Message}" : $"Could not open Combo JSON: {exception.Message}";
        }
    }

    private void McpMenuItem_Click(object sender, RoutedEventArgs eventArgs)
    {
        NavigationPopup.IsOpen = false;
        try
        {
            new McpWindow(_mcpServer, _isThai) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปิดหน้า MCP ไม่สำเร็จ: {exception.Message}"
                : $"Could not open MCP page: {exception.Message}";
        }
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs eventArgs)
    {
        NavigationPopup.IsOpen = false;
        try
        {
            if (new SettingsWindow(_isThai, _config.Gemini) { Owner = this }.ShowDialog() == true)
            {
                _settings.GeminiApiKey = AppSettingsService.Load().GeminiApiKey;
                StatusText.Text = _isThai ? "บันทึกการตั้งค่า Gemini แล้ว" : "Gemini settings saved.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai ? $"เปิดหน้าตั้งค่าไม่สำเร็จ: {exception.Message}" : $"Could not open Settings: {exception.Message}";
        }
    }

    private async void RefreshDataMenuItem_Click(object sender, RoutedEventArgs eventArgs) => await RefreshMetaDataAsync(true);

    private void StartupMenuItem_Click(object sender, RoutedEventArgs eventArgs) => ToggleStartup();

    private void ReloadButton_Click(object sender, RoutedEventArgs eventArgs) => LoadBoard();

    private void LanguageButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _isThai = !_isThai;
        ApplyLanguageText();
        RenderBoard();
        UpdateStartupMenuState();
        UpdateAutoUpdateButton();
        StatusText.Text = _isThai ? "เปลี่ยนภาษาเป็นไทยแล้ว" : "Language changed to English";
    }

    private void RankModeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _rankComboMode = _rankComboMode switch
        {
            RankComboMode.All => RankComboMode.Archon,
            RankComboMode.Archon => RankComboMode.Legend,
            _ => RankComboMode.All
        };
        ApplyLanguageText();
        RenderBoard();
        UpdateBoardStatus();
    }

    private void WebSyncButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            new BrowserSearchWindow(_browserSyncServer, _isThai) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai
                ? $"เปิดหน้าค้นเว็บไม่สำเร็จ: {exception.Message}"
                : $"Could not open Browser Search: {exception.Message}";
        }
    }

    private void ApplyLanguageText()
    {
        Title = "Dota 2 Team Board";
        BoardTitleText.Text = Title;
        SubtitleText.Text = _rankComboMode switch
        {
            RankComboMode.Archon => _isThai
                ? "ชุด ARCHON คัดจาก core/support win rate สูง • คลิกทีมเพื่อวิเคราะห์"
                : "ARCHON combos curated from high-win-rate cores/supports • Click to analyze",
            RankComboMode.Legend => _isThai
                ? "ชุด LEGEND คัดจาก core/support win rate สูง • คลิกทีมเพื่อวิเคราะห์"
                : "LEGEND combos curated from high-win-rate cores/supports • Click to analyze",
            _ => _isThai
                ? "คลิกชุดฮีโร่เพื่อให้ Gemini วิเคราะห์ • รีเฟรช JSON อัตโนมัติ"
                : "Click a lineup for Gemini analysis • JSON auto-refresh"
        };
        LanguageButton.Content = _isThai ? "LANG: TH" : "LANG: EN";
        LanguageButton.ToolTip = _isThai ? "เปลี่ยนเป็นภาษาอังกฤษ" : "Switch to Thai";
        ReloadButton.ToolTip = _isThai ? "รีเฟรช JSON" : "Refresh JSON";
        FullButton.ToolTip = _isThai ? "เต็มจอ / คืนขนาด" : "Full screen / Restore";
        MinimizeButton.ToolTip = _isThai ? "ย่อไปที่ system tray" : "Minimize to tray";
        HideButton.ToolTip = _isThai ? "ซ่อนไปที่ system tray" : "Hide to tray";
        MenuButton.Content = _isThai ? "เมนู ▾" : "MENU ▾";
        ToolsMenuTitleText.Text = _isThai ? "เครื่องมือจัดทีม" : "TEAM TOOLS";
        MetaCounterMenuTitleText.Text = _isThai ? "แผนสู้ฮีโร่เมตา" : "META COUNTER";
        MetaCounterMenuDetailText.Text = _isThai ? "เลือกแผนรับมือฮีโร่ยอดนิยมในแพตช์ปัจจุบัน" : "Choose a plan against current popular heroes";
        HeroCounterMenuTitleText.Text = _isThai ? "ค้นหาฮีโร่แก้ทาง" : "HERO COUNTER FINDER";
        HeroCounterMenuDetailText.Text = _isThai ? "ค้นหาตัวที่ได้เปรียบและเสียเปรียบให้ทันดราฟต์" : "Find favorable and difficult matchups fast";
        ComboJsonMenuTitleText.Text = _isThai ? "COMBO หลัก 3 ฮีโร่ / JSON" : "MAIN 3-HERO COMBOS / JSON";
        ComboJsonMenuDetailText.Text = _isThai ? "นำเข้า-ส่งออกเฉพาะชุด Carry + Mid + Support หน้าหลัก" : "Import or export main Carry + Mid + Support rows";
        UpdateMcpMenuText();
        RefreshDataMenuButton.Content = _isThai ? "↻  อัปเดตข้อมูลเมตาเดี๋ยวนี้" : "↻  REFRESH META DATA NOW";
        SettingsMenuButton.Content = _isThai ? "⚙  ตั้งค่า / GEMINI API" : "⚙  SETTINGS / GEMINI API";
        UpdateVersionTags();
        if (_trayOpenItem is not null)
        {
            _trayOpenItem.Text = _isThai ? "เปิด" : "Open";
            _trayRefreshItem!.Text = _isThai ? "รีเฟรช JSON" : "Refresh JSON";
            UpdateStartupMenuState();
            _trayExitItem!.Text = _isThai ? "ออก" : "Exit";
        }
    }

    private void FullButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        FullButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void HideButton_Click(object sender, RoutedEventArgs eventArgs) => Hide();
}
