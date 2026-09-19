using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using DotaComboBoard.Services;
using MediaColor = System.Windows.Media.Color;

namespace DotaComboBoard;

public partial class McpWindow : Window
{
    private const string TunnelSettingsUrl = "https://platform.openai.com/settings/organization/tunnels";
    private const string TunnelGuideUrl = "https://developers.openai.com/api/docs/guides/secure-mcp-tunnels";
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
        CopyEndpointButton.Content = _isThai ? "คัดลอก" : "COPY";
        EndpointLabelText.Text = _isThai ? "ปลายทาง MCP" : "MCP ENDPOINT";
        PluginNameLabelText.Text = _isThai ? "ชื่อ PLUGIN" : "PLUGIN NAME";
        TransportLabelText.Text = "TRANSPORT";
        ConfigLabelText.Text = _isThai ? "CONFIG สำหรับ LOCAL MCP CLIENT" : "LOCAL MCP CLIENT CONFIG";
        ConnectTab.Header = _isThai ? "เชื่อมต่อ" : "CONNECT";
        ChatGptTab.Header = "CHATGPT ADD PLUGIN";
        ToolsTab.Header = _isThai ? "คำสั่ง" : "TOOLS";
        SecurityTab.Header = _isThai ? "ความปลอดภัย" : "SECURITY";
        ContextToolText.Text = _isThai ? "อ่านเวอร์ชันแอป, Dota patch, จำนวนทีม และ format ที่ต้องใช้" : "Read app version, Dota patch, team counts, and required format.";
        GenerationToolText.Text = _isThai ? "รับ prompt ตาม patch พร้อม JSON template ที่สร้างข้อมูลกลับเข้าแอปได้" : "Get a patch-aware prompt and exact JSON template.";
        ListToolText.Text = _isThai ? "อ่านทีม all / archon / legend กลับเป็น Combo Pack" : "Read all, Archon, or Legend teams as a Combo Pack.";
        ValidateToolText.Text = _isThai ? "ตรวจ JSON โดยไม่แก้ข้อมูลในแอป" : "Validate JSON without changing app data.";
        ImportToolText.Text = _isThai ? "เพิ่มหรือแทนที่ Combo Team เมื่อส่ง confirm=true และสำรองข้อมูลเดิมก่อนเสมอ" : "Append or replace Combo Teams with confirm=true; always backs up first.";
        SearchToolText.Text = _isThai ? "ค้นเว็บสาธารณะผ่าน Chrome Extension ที่จับคู่ไว้" : "Search public web sources through the paired Chrome Extension.";
        UnsafeUrlTitleText.Text = _isThai ? "อย่าใส่ LOCAL URL ในช่อง PUBLIC URL" : "DO NOT USE THE LOCAL URL IN PUBLIC URL";
        UnsafeUrlDetailText.Text = _isThai
            ? $"{McpServer.Endpoint} ใช้ได้เฉพาะในเครื่อง จึงถูก ChatGPT ปฏิเสธเป็น Unsafe URL ไม่ใช่ข้อผิดพลาด OAuth ของแอป"
            : $"{McpServer.Endpoint} is local-only, so ChatGPT rejects it as an unsafe public URL. This is not an app OAuth failure.";
        TunnelStepsText.Text = _isThai
            ? "1. เปิด Tunnel Settings และสร้าง MCP Tunnel\n2. รัน tunnel-client บนเครื่องนี้ โดยตั้ง local target เป็น URL ด้านล่าง\n3. ใน ChatGPT Add plugin เลือก Connection = Tunnel\n4. เลือก tunnel ที่สร้างไว้ หรือวาง tunnel_id — ไม่ต้องกรอก OAuth ของแอป"
            : "1. Open Tunnel Settings and create an MCP tunnel.\n2. Run tunnel-client on this PC with the local target below.\n3. In ChatGPT Add plugin, choose Connection = Tunnel.\n4. Select the tunnel or paste its tunnel_id; do not configure app OAuth.";
        OpenTunnelSettingsButton.Content = _isThai ? "เปิด TUNNEL SETTINGS" : "OPEN TUNNEL SETTINGS";
        OpenTunnelGuideButton.Content = _isThai ? "เปิดคู่มือ OPENAI" : "OPEN OFFICIAL GUIDE";
        CopyTunnelTargetButton.Content = _isThai ? "คัดลอก LOCAL TARGET" : "COPY LOCAL TARGET";
        TunnelConnectionTitleText.Text = _isThai ? "ค่าที่ใช้กับ tunnel-client" : "TUNNEL-CLIENT TARGET";
        TunnelConnectionDetailText.Text = $"MCP server URL: {McpServer.Endpoint}\nTransport: {McpServer.TransportName}\nAuthentication: none";
        SecurityText.Text = _isThai
            ? "• Server bind เฉพาะ 127.0.0.1 เท่านั้น\n• ไม่ต้องใช้ token หรือ Authorization header\n• Browser origin อนุญาตเฉพาะ localhost และ Chrome Extension\n• import_combo_pack ต้องส่ง confirm=true"
            : "• Server binds only to 127.0.0.1\n• No token or Authorization header is required\n• Browser origins are limited to localhost and Chrome extensions\n• import_combo_pack requires confirm=true";
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

    private void OpenTunnelSettingsButton_Click(object sender, RoutedEventArgs eventArgs) => OpenUrl(TunnelSettingsUrl);

    private void OpenTunnelGuideButton_Click(object sender, RoutedEventArgs eventArgs) => OpenUrl(TunnelGuideUrl);

    private void CopyTunnelTargetButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(McpServer.Endpoint);
        ActivityText.Text = _isThai ? "คัดลอก local target สำหรับ tunnel-client แล้ว" : "Tunnel local target copied.";
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
