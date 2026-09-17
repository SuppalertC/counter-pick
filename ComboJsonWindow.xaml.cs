using System.IO;
using System.Windows;
using DotaComboBoard.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace DotaComboBoard;

public partial class ComboJsonWindow : Window
{
    private readonly string _boardPath;
    private readonly bool _isThai;
    private readonly ComboJsonService _comboService = new();
    private string _patchNumber = "current";
    private string _prompt = string.Empty;

    public ComboJsonWindow(string boardPath, bool isThai)
    {
        InitializeComponent();
        _boardPath = boardPath;
        _isThai = isThai;
        ApplyLanguage();
        AppVersionText.Text = $"APP v{AppVersionInfo.Current}";
        Loaded += async (_, _) => await LoadPatchPromptAsync();
    }

    private async Task LoadPatchPromptAsync()
    {
        SetActionsEnabled(false);
        PatchText.Text = "DOTA PATCH …";
        StatusText.Text = _isThai ? "กำลังอ่านแพตช์ล่าสุดและสร้าง prompt..." : "Loading current patch and generation prompt...";
        try
        {
            _patchNumber = await new DotaPatchContextService().GetCurrentPatchNumberAsync();
        }
        catch
        {
            _patchNumber = "current";
        }

        _prompt = _comboService.CreateGenerationPrompt(_patchNumber);
        PromptBox.Text = _prompt;
        FormatBox.Text = _comboService.Serialize(_comboService.CreateTemplate(_patchNumber, _prompt));
        PatchText.Text = $"DOTA {_patchNumber}";
        StatusText.Text = _isThai
            ? "พร้อมใช้งาน • Export จะบันทึก patch และ prompt ไปกับไฟล์ทุกครั้ง"
            : "Ready. Every export includes its patch and generation prompt.";
        SetActionsEnabled(true);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = _isThai ? "นำเข้า Combo JSON" : "Import Combo JSON",
            Filter = "Combo JSON (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var pack = await _comboService.LoadPackAsync(dialog.FileName);
            var choice = System.Windows.MessageBox.Show(
                this,
                _isThai
                    ? $"พบ {pack.Combos.Count} ชุด\n\nYes = เพิ่มต่อท้ายรายการเดิม\nNo = แทนที่รายการเดิมทั้งหมด\nCancel = ยกเลิก"
                    : $"Found {pack.Combos.Count} combos.\n\nYes = append\nNo = replace all\nCancel = stop",
                _isThai ? "เลือกรูปแบบนำเข้า" : "Choose Import Mode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            var result = _comboService.ApplyImport(_boardPath, pack, replace: choice == MessageBoxResult.No);
            StatusText.Text = _isThai
                ? $"นำเข้าสำเร็จ {result.AddedCount} ชุด • ข้ามรายการซ้ำ {result.SkippedDuplicates} • สำรองไฟล์เดิมใน LocalAppData แล้ว"
                : $"Imported {result.AddedCount} combos • skipped {result.SkippedDuplicates} duplicates • previous board backed up.";
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai ? $"นำเข้าไม่สำเร็จ: {exception.Message}" : $"Import failed: {exception.Message}";
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = CreateSaveDialog($"dota-combos-{_patchNumber}.json", "Combo JSON (*.json)|*.json");
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var board = BoardLoader.Load(_boardPath);
            var pack = _comboService.CreatePack(board, _patchNumber, _prompt);
            await _comboService.ExportAsync(dialog.FileName, pack);
            StatusText.Text = _isThai ? $"Export {pack.Combos.Count} ชุดแล้ว: {dialog.FileName}" : $"Exported {pack.Combos.Count} combos: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            StatusText.Text = _isThai ? $"Export ไม่สำเร็จ: {exception.Message}" : $"Export failed: {exception.Message}";
        }
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(_prompt);
        StatusText.Text = _isThai ? $"คัดลอก prompt สำหรับ Dota patch {_patchNumber} แล้ว" : $"Copied the Dota patch {_patchNumber} prompt.";
    }

    private async void SavePromptButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = CreateSaveDialog($"combo-generation-prompt-{_patchNumber}.txt", "Text (*.txt)|*.txt");
        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, _prompt);
            StatusText.Text = _isThai ? $"บันทึก prompt แล้ว: {dialog.FileName}" : $"Prompt saved: {dialog.FileName}";
        }
    }

    private async void SaveTemplateButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = CreateSaveDialog($"combo-pack-template-{_patchNumber}.json", "Combo JSON (*.json)|*.json");
        if (dialog.ShowDialog(this) == true)
        {
            await _comboService.ExportAsync(dialog.FileName, _comboService.CreateTemplate(_patchNumber, _prompt));
            StatusText.Text = _isThai ? $"บันทึก JSON template แล้ว: {dialog.FileName}" : $"JSON template saved: {dialog.FileName}";
        }
    }

    private static SaveFileDialog CreateSaveDialog(string fileName, string filter)
    {
        return new SaveFileDialog
        {
            FileName = fileName,
            Filter = $"{filter}|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };
    }

    private void SetActionsEnabled(bool enabled)
    {
        ExportButton.IsEnabled = enabled;
        CopyPromptButton.IsEnabled = enabled;
        SavePromptButton.IsEnabled = enabled;
        SaveTemplateButton.IsEnabled = enabled;
    }

    private void ApplyLanguage()
    {
        Title = _isThai ? "Combo JSON" : "Combo JSON";
        TitleText.Text = _isThai ? "COMBO หลัก 3 ฮีโร่ / JSON" : "MAIN 3-HERO COMBOS / JSON";
        SubtitleText.Text = _isThai ? "เฉพาะชุด Carry + Mid + Support ที่แสดงบนหน้าหลัก" : "Main-board Carry + Mid + Support rows only";
        CloseButton.Content = _isThai ? "ปิด" : "CLOSE";
        ImportButton.Content = _isThai ? "นำเข้า JSON" : "IMPORT JSON";
        ExportButton.Content = _isThai ? "EXPORT ชุดปัจจุบัน" : "EXPORT CURRENT COMBOS";
        CopyPromptButton.Content = _isThai ? "คัดลอก PROMPT ตามแพตช์" : "COPY PATCH PROMPT";
        SavePromptButton.Content = _isThai ? "บันทึก PROMPT" : "SAVE PROMPT";
        SaveTemplateButton.Content = _isThai ? "บันทึก JSON TEMPLATE" : "SAVE JSON TEMPLATE";
        PromptTab.Header = _isThai ? "PROMPT ตามแพตช์" : "PATCH PROMPT";
        FormatTab.Header = "JSON FORMAT";
        GuideTab.Header = _isThai ? "กฎการนำเข้า" : "IMPORT RULES";
        GuideHeaderText.Text = _isThai ? "รูปแบบ JSON ที่รองรับ" : "SUPPORTED JSON";
        GuideText.Text = _isThai
            ? "• Combo Pack formatVersion 1 — แนะนำ\n• board.json ของแอปเดิม\n• Array ของ combo โดยตรง\n\nแต่ละชุดต้องมีฮีโร่ 3 ตัวและ role Carry, Mid, Support ครบ\nโหมดเพิ่มต่อท้ายจะข้ามชุดฮีโร่ซ้ำอัตโนมัติ\nก่อนเขียนไฟล์ แอปจะสำรอง board เดิมไว้ใน %LocalAppData%\\DotaComboBoard\\backups"
            : "• Combo Pack formatVersion 1 — recommended\n• Existing app board.json\n• Raw combo array\n\nEach combo needs exactly three heroes covering Carry, Mid, and Support.\nAppend mode skips duplicate hero sets.\nThe current board is backed up under %LocalAppData%\\DotaComboBoard\\backups before writing.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
