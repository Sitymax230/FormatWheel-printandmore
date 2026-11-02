using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FormatWheel.Sdk;

// Пример расширения Format Wheel: отправка файлов на печать.
//
// Лепесток «Печать» попадает в ТРЕТЬЕ меню колеса — слот WheelSlot.Alt (Alt + Shift).
// Run() вызывается в отдельном STA-потоке — оттуда можно показывать диалог печати и окна Windows Forms.
public sealed class PrintActions : IWheelActions
{
    public string Name => "Print";

    // Изображения и документы — можно печатать
    private static readonly string[] PrintableExtensions =
    {
        // фото
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp",
        // текстовые документы
        ".txt", ".log", ".csv", ".rtf",
        // остальные документы (печать через системную оболочку)
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    // Видео — нельзя печатать
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v", ".3gp"
    };

    // Аудио — нельзя печатать
    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".wma", ".flac", ".aac", ".ogg", ".m4a"
    };

    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(ext)) return false;

        // Не видео и не аудио
        if (Array.Exists(VideoExtensions, v => v == ext)) return false;
        if (Array.Exists(AudioExtensions, a => a == ext)) return false;

        // Только изображения и документы
        return Array.Exists(PrintableExtensions, p => p == ext);
    }

    public IReadOnlyList<WheelAction> ActionsFor(IReadOnlyList<string> files)
    {
        if (files == null || files.Count == 0 || files.Any(f => !CanHandle(f)))
            return Array.Empty<WheelAction>();

        return new List<WheelAction>
        {
            new WheelAction("Печать", "отправить на принтер · Alt + Shift", PrintAsync)
            {
                Slot = WheelSlot.Alt // ТРЕТЬЕ МЕНЮ: Alt + Shift (Primary = Shift — первое, Ctrl = Ctrl+Shift — второе)
            }
        };
    }

    private static Task PrintAsync(IReadOnlyList<string> files, Action<double> report, CancellationToken ct)
    {
        // WheelController уже вызывает Run в STA-потоке, можно показывать окна напрямую
        ShowPrintDialogAndPrint(files, report, ct);
        return Task.CompletedTask;
    }

    private static void ShowPrintDialogAndPrint(IReadOnlyList<string> files, Action<double> report, CancellationToken ct)
    {
        using var dialog = new PrintForm(files);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            report(0); // пользователь отменил — не ошибка
            return;
        }

        var settings = dialog.PrinterSettings;
        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PrintFile(file, settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось напечатать «{Path.GetFileName(file)}»: {ex.Message}",
                    "Format Wheel · печать",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            done++;
            report((double)done / files.Count);
        }
    }

    private static void PrintFile(string file, PrinterSettings settings)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();

        // Прямая печать через PrintDocument для изображений и текстовых файлов
        if (Array.Exists(new[]
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp",
                ".txt", ".log", ".csv", ".rtf"
            }, e => e == ext))
        {
            using var doc = new PrintDocument
            {
                PrinterSettings = settings,
                DocumentName = Path.GetFileName(file),
            };
            doc.PrintPage += (_, e) => DrawPage(e, file);
            doc.Print();
        }
        else
        {
            // Для остальных документов (PDF, DOC, XLS и т.д.) — системная печать через Shell
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(30000);
        }
    }

    private static void DrawPage(PrintPageEventArgs e, string file)
    {
        var margin = e.MarginBounds;
        var ext = Path.GetExtension(file);

        if (ext is ".txt" or ".log" or ".csv")
        {
            using var font = new Font("Consolas", 10);
            float y = margin.Top;
            foreach (var line in File.ReadLines(file))
            {
                string text = line ?? "";
                if (y + font.Height > margin.Bottom)
                {
                    e.HasMorePages = true;
                    return;
                }
                e.Graphics.DrawString(text, font, Brushes.Black, margin.Left, y);
                y += font.Height;
            }
        }
        else
        {
            // картинка вписывается в страницу с сохранением пропорций
            using var image = Image.FromFile(file);
            float scale = Math.Min((float)margin.Width / image.Width, (float)margin.Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            float x = margin.Left + (margin.Width - w) / 2;
            float y = margin.Top + (margin.Height - h) / 2;
            e.Graphics.DrawImage(image, x, y, w, h);
        }
    }
}

// Кастомное диалоговое окно печати: выбор принтера, кнопки «Свойства» и «Печать»
internal sealed class PrintForm : Form
{
    private readonly ComboBox _printerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _propertiesButton = new() { Text = "Свойства" };
    private readonly Button _printButton = new() { Text = "Печать", DialogResult = DialogResult.OK };
    private readonly Button _cancelButton = new() { Text = "Отмена", DialogResult = DialogResult.Cancel };

    public PrinterSettings PrinterSettings { get; private set; } = new PrinterSettings();

    public PrintForm(IReadOnlyList<string> files)
    {
        Text = "Печать — Format Wheel";
        Width = 460;
        Height = 240;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // Заголовок
        var titleLabel = new Label
        {
            Text = $"Файлов для печати: {files.Count}",
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(15, 0, 0, 0),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        // Подсказка о типах файлов
        var hintLabel = new Label
        {
            Text = "Печать доступна для фото и документов (видео и аудио не поддерживаются).",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(15, 0, 0, 0),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };

        // Выбор принтера
        var printerLabel = new Label
        {
            Text = "Принтер:",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(15, 8, 0, 0)
        };

        _printerCombo.Dock = DockStyle.Top;
        _printerCombo.Height = 26;
        _printerCombo.Margin = new Padding(15, 0, 15, 10);

        foreach (string p in PrinterSettings.InstalledPrinters)
            _printerCombo.Items.Add(p);

        if (_printerCombo.Items.Count > 0)
        {
            _printerCombo.SelectedIndex = 0;
            PrinterSettings.PrinterName = _printerCombo.SelectedItem?.ToString() ?? "";
        }
        else
        {
            _printerCombo.Items.Add("Нет доступных принтеров");
            _printerCombo.SelectedIndex = 0;
            _printButton.Enabled = false;
        }

        _printerCombo.SelectedIndexChanged += (_, _) =>
        {
            var name = _printerCombo.SelectedItem?.ToString() ?? "";
            if (name != "Нет доступных принтеров")
                PrinterSettings.PrinterName = name;
        };

        // Кнопки (расположены вправо внизу)
        _propertiesButton.Width = 100;
        _propertiesButton.Height = 30;
        _propertiesButton.Click += (_, _) => ShowPrinterProperties();

        _printButton.Width = 100;
        _printButton.Height = 30;
        _printButton.Click += (_, _) => DialogResult = DialogResult.OK;

        _cancelButton.Width = 100;
        _cancelButton.Height = 30;

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(10)
        };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_printButton);
        buttonPanel.Controls.Add(_propertiesButton);

        // Кнопки вправо
        UpdateButtonLocations(buttonPanel.Width);
        buttonPanel.Resize += (_, _) => UpdateButtonLocations(buttonPanel.Width);

        Controls.Add(buttonPanel);
        Controls.Add(_printerCombo);
        Controls.Add(printerLabel);
        Controls.Add(hintLabel);
        Controls.Add(titleLabel);

        AcceptButton = _printButton;
        CancelButton = _cancelButton;
    }

    private void UpdateButtonLocations(int panelWidth)
    {
        _propertiesButton.Location = new Point(panelWidth - 115, 13);
        _printButton.Location = new Point(panelWidth - 225, 13);
        _cancelButton.Location = new Point(panelWidth - 335, 13);
    }

    private void ShowPrinterProperties()
    {
        var name = _printerCombo.SelectedItem?.ToString() ?? "";
        if (name == "Нет доступных принтеров") return;

        using var pd = new PrintDialog
        {
            PrinterSettings = new PrinterSettings { PrinterName = name },
            AllowSomePages = false,
            PrintToFile = false,
            ShowHelp = false,
            ShowNetwork = false,
            UseEXDialog = true
        };

        if (pd.ShowDialog() == DialogResult.OK)
        {
            PrinterSettings = pd.PrinterSettings;
            // Обновляем выбранный принтер в комбобоксе
            for (int i = 0; i < _printerCombo.Items.Count; i++)
            {
                if (_printerCombo.Items[i]?.ToString() == PrinterSettings.PrinterName)
                {
                    _printerCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }
}
