using System.Windows;
using System.Windows.Media;
using DotaComboBoard.Services;
using MediaColor = System.Windows.Media.Color;

namespace DotaComboBoard;

public partial class McpWindow : Window
{
    private readonly McpServer _server;
    private readonly bool _isThai;

    public McpWindow(McpServer server, bool isThai)
    {
        InitializeComponent();
        _server = server;
        _isThai = isThai;
        PluginNameBox.Text = McpServer.DisplayName;
        TransportBox.Text = McpServer.TransportName;
        EndpointBox.Text = McpServer.Endpoint;
        TokenBox.Text = _server.AccessToken;
        AuthBox.Text = $"Authorization: Bearer {_server.AccessToken}";
        ConfigBox.Text = _server.CreateClientConfig();
        ApplyLanguage();
        UpdateStatus();
        _server.StatusChanged += Server_StatusChanged;
        Closed += (_, _) => _server.StatusChanged -= Server_StatusChanged;
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "Local MCP Server" : "Local MCP Server";
        TitleText.Text = "LOCAL MCP SERVER";
        SubtitleText.Text = _isThai
            ? "ให้ MCP client เรียกคำสั่ง อ่านข้อมูล และส่ง Combo Pack กลับเข้าแอปด้วย JSON format เดียวกัน"
            : "Let MCP clients call tools, read data, and send Combo Packs back in the app's exact JSON format.";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        CopyConfigButton.Content = _isThai ? "คัดลอก CLIENT CONFIG" : "COPY CLIENT CONFIG";
        CopyEndpointButton.Content = CopyTokenButton.Content = _isThai ? "คัดลอก" : "COPY";
        EndpointLabelText.Text = _isThai ? "ปลายทาง MCP" : "MCP ENDPOINT";
        PluginNameLabelText.Text = _isThai ? "ชื่อ PLUGIN" : "PLUGIN NAME";
        TransportLabelText.Text = "TRANSPORT";
        TokenLabelText.Text = _isThai ? "ACCESS TOKEN" : "ACCESS TOKEN";
        AuthLabelText.Text = _isThai ? "AUTH HEADER สำหรับกรอก ADD PLUGIN" : "AUTH HEADER FOR ADD PLUGIN";
        ConfigLabelText.Text = _isThai ? "CONFIG สำหรับ MCP CLIENT" : "GENERIC MCP CLIENT CONFIG";
        ConnectTab.Header = _isThai ? "เชื่อมต่อ" : "CONNECT";
        ToolsTab.Header = _isThai ? "คำสั่ง" : "TOOLS";
        SecurityTab.Header = _isThai ? "ความปลอดภัย" : "SECURITY";
        ContextToolText.Text = _isThai ? "อ่านเวอร์ชันแอป, Dota patch, จำนวนทีม และ format ที่ต้องใช้" : "Read app version, Dota patch, team counts, and required format.";
        GenerationToolText.Text = _isThai ? "รับ prompt ตาม patch พร้อม JSON template ที่สร้างข้อมูลกลับเข้าแอปได้" : "Get a patch-aware prompt and exact JSON template.";
        ListToolText.Text = _isThai ? "อ่านทีม all / archon / legend กลับเป็น Combo Pack" : "Read all, Archon, or Legend teams as a Combo Pack.";
        ValidateToolText.Text = _isThai ? "ตรวจ JSON โดยไม่แก้ข้อมูลในแอป" : "Validate JSON without changing app data.";
        ImportToolText.Text = _isThai ? "เพิ่มหรือแทนที่ Combo Team เมื่อส่ง confirm=true และสำรองข้อมูลเดิมก่อนเสมอ" : "Append or replace Combo Teams with confirm=true; always backs up first.";
        SearchToolText.Text = _isThai ? "ค้นเว็บสาธารณะผ่าน Chrome Extension ที่จับคู่ไว้" : "Search public web sources through the paired Chrome Extension.";
        SecurityText.Text = _isThai
            ? "• Server bind เฉพาะ 127.0.0.1 เท่านั้น\n• ทุก request ต้องส่ง Authorization: Bearer <token>\n• Browser origin อนุญาตเฉพาะ localhost และ Chrome Extension\n• import_combo_pack ต้องส่ง confirm=true\n• Token ถูกเก็บเฉพาะใน LocalAppData ของผู้ใช้ Windows คนนี้"
            : "• Server binds only to 127.0.0.1\n• Every request requires Authorization: Bearer <token>\n• Browser origins are limited to localhost and Chrome extensions\n• import_combo_pack requires confirm=true\n• The token is stored only in this Windows user's LocalAppData";
        FooterText.Text = _isThai
            ? "รองรับ Streamable HTTP MCP ทั้ง initialize handshake และ protocol 2026-07-28 • ปล่อยแอปเปิดไว้ระหว่างใช้งาน"
            : "Supports Streamable HTTP MCP initialize handshakes and protocol 2026-07-28. Keep the app running while connected.";
    }

    private void UpdateStatus()
    {
        if (_server.IsRunning)
        {
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(95, 208, 154));
            StatusText.Text = _isThai ? "MCP พร้อมรับคำสั่ง" : "MCP SERVER READY";
            ActivityText.Text = _isThai
                ? $"รับแล้ว {_server.RequestCount} requests" + (string.IsNullOrWhiteSpace(_server.LastToolName) ? string.Empty : $" • ล่าสุด {_server.LastToolName}")
                : $"{_server.RequestCount} requests" + (string.IsNullOrWhiteSpace(_server.LastToolName) ? string.Empty : $" • Last: {_server.LastToolName}");
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(239, 106, 91));
            StatusText.Text = _isThai ? "MCP เปิดไม่สำเร็จ" : "MCP SERVER ERROR";
            ActivityText.Text = _server.ErrorMessage;
        }
    }

    private void Server_StatusChanged(object? sender, EventArgs eventArgs) => Dispatcher.Invoke(UpdateStatus);

    private void CopyConfigButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(_server.CreateClientConfig());
        ActivityText.Text = _isThai ? "คัดลอก MCP client config แล้ว" : "MCP client config copied.";
    }

    private void CopyEndpointButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(McpServer.Endpoint);
        ActivityText.Text = _isThai ? "คัดลอก endpoint แล้ว" : "Endpoint copied.";
    }

    private void CopyTokenButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(_server.AccessToken);
        ActivityText.Text = _isThai ? "คัดลอก access token แล้ว" : "Access token copied.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
