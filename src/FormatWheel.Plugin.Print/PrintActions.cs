using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FormatWheel.Sdk;

namespace FormatWheel.PrintPlugin;

// Пример расширения Format Wheel: отправка файлов на печать.
//
// Лепесток «Печать» попадает в ТРЕТЬЕ меню колеса — слот WheelSlot.Alt (Alt + Shift).
// Run() вызывается в отдельном STA-потоке — оттуда можно показывать окна Windows Forms.
//
// 10 функций:
// 1. Количество копий (NumericUpDown в диалоге)
// 2. Диапазон страниц (все / выбранные)
// 3. Ориентация и формат бумаги (книжная/альбомная, A4/A3/A5/Letter)
// 4. Цветная / ЧБ печать
// 5. Двусторонняя печать (дуплекс)
// 6. «Быстрая печать» — второй лепесток без диалога
// 7. Запомнить последний принтер (JSON в AppData)
// 8. Статус принтера (WMI Win32_Printer)
// 9. DOCX/XLSX через Office Interop (если установлен Word/Excel)
// 10. Печать в файл (Microsoft Print to PDF / XPS)
// + Автообновление плагина с GitHub releases

public sealed class PrintActions : IWheelActions
{
    public string Name => "Print";

    private static readonly string[] PrintableExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp",
        ".txt", ".log", ".csv", ".rtf",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v", ".3gp"
    };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".wma", ".flac", ".aac", ".ogg", ".m4a"
    };

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string RepoOwner = "Sitymax230";
    private const string RepoName = "FormatWheel-printandmore";
    public const string CurrentVersion = "1.0.4";

    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(ext)) return false;
        if (Array.Exists(VideoExtensions, v => v == ext)) return false;
        if (Array.Exists(AudioExtensions, a => a == ext)) return false;
        return Array.Exists(PrintableExtensions, p => p == ext);
    }

    public IReadOnlyList<WheelAction> ActionsFor(IReadOnlyList<string> files)
    {
        if (files == null || files.Count == 0 || files.Any(f => !CanHandle(f)))
            return Array.Empty<WheelAction>();

        // Автообновление при первом показе меню (fire-and-forget)
        _ = CheckForUpdatesAsync();

        return new List<WheelAction>
        {
            // 6. «Быстрая печать» — мгновенно на принтер по умолчанию без диалога
            new WheelAction("Быстрая печать", "мгновенно на принтер по умолчанию", QuickPrintAsync)
            {
                Slot = WheelSlot.Alt
            },
            // Обычная печать с полным диалогом
            new WheelAction("Печать", "открыть диалог печати · Alt + Shift", ShowPrintDialogAndPrint)
            {
                Slot = WheelSlot.Alt
            }
        };
    }

    // ========================================================================
    // ОБЫЧНАЯ ПЕЧАТЬ (с диалогом)
    // ========================================================================

    private static Task ShowPrintDialogAndPrint(IReadOnlyList<string> files, Action<double> report, CancellationToken ct)
    {
        var settings = PluginSettings.Load();
        using var dialog = new PrintForm(files.ToArray(), settings);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            report(0);
            return Task.CompletedTask;
        }

        // Сохраняем настройки
        settings.Copies = dialog.Copies;
        settings.Color = dialog.Color;
        settings.Duplex = dialog.DuplexEnabled;
        settings.DuplexVertical = dialog.DuplexVertical;
        settings.PaperSize = dialog.PaperSizeName;
        settings.Landscape = dialog.Landscape;
        settings.PrintToFile = dialog.PrintToFile;
        settings.Save();

        ApplyPrinterSettings(dialog.PrinterSettings, dialog.Copies, dialog.Color, dialog.DuplexEnabled,
            dialog.DuplexVertical, dialog.Landscape, dialog.PaperSizeName);

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PrintFile(file, dialog.PrinterSettings, dialog.PageRange, dialog.PrintToFile);
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
        return Task.CompletedTask;
    }

    // ========================================================================
    // БЫСТРАЯ ПЕЧАТЬ (без диалога)
    // ========================================================================

    private static Task QuickPrintAsync(IReadOnlyList<string> files, Action<double> report, CancellationToken ct)
    {
        var settings = PluginSettings.Load();
        using var doc = new PrintDocument
        {
            PrinterSettings = new PrinterSettings(),
            DocumentName = files.Count == 1 ? Path.GetFileName(files[0]) : $"Печать {files.Count} файлов"
        };

        if (settings.Copies > 0)
            doc.PrinterSettings.Copies = (short)Math.Min(settings.Copies, 999);
        doc.PrinterSettings.DefaultPageSettings.Color = settings.Color;

        if (settings.Duplex)
            doc.PrinterSettings.Duplex = settings.DuplexVertical ? Duplex.Vertical : Duplex.Horizontal;

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                doc.PrintPage += (_, e) => DrawPage(e, file);
                doc.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось напечатать «{Path.GetFileName(file)}»: {ex.Message}",
                    "Format Wheel · быстрая печать",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            done++;
            report((double)done / files.Count);
        }
        return Task.CompletedTask;
    }

    // ========================================================================
    // ПЕЧАТЬ ОТДЕЛЬНОГО ФАЙЛА
    // ========================================================================

    private static void PrintFile(string file, PrinterSettings settings, string? pageRange, bool printToFile)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();

        // 10. Печать в файл (Microsoft Print to PDF / XPS)
        if (printToFile)
        {
            settings.PrintFileName = Path.Combine(
                Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(file)}_{Guid.NewGuid():N}.pdf");
            settings.PrintToFile = true;
        }

        // 9. DOC/DOCX/XLS/XLSX/PPT/PPTX через Office Interop (если установлен Office)
        if (ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx")
        {
            if (TryPrintOffice(file, settings))
                return;
            ShellPrint(file);
            return;
        }

        // PDF и RTF — через системную оболочку
        if (ext == ".pdf" || ext == ".rtf")
        {
            ShellPrint(file);
            return;
        }

        // Изображения и текст — через PrintDocument
        using var doc = new PrintDocument
        {
            PrinterSettings = settings,
            DocumentName = Path.GetFileName(file),
        };
        doc.PrintPage += (_, e) => DrawPage(e, file, pageRange);
        doc.Print();
    }

    // ========================================================================
    // 9. ПЕЧАТЬ ЧЕРЕЗ OFFICE INTEROP (DOCX, XLSX, PPTX)
    // ========================================================================

    private static bool TryPrintOffice(string file, PrinterSettings settings)
    {
        try
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            object? officeApp = null;

            if (ext is ".doc" or ".docx")
                officeApp = CreateOfficeProgID("Word.Application");
            else if (ext is ".xls" or ".xlsx")
                officeApp = CreateOfficeProgID("Excel.Application");
            else if (ext is ".ppt" or ".pptx")
                officeApp = CreateOfficeProgID("PowerPoint.Application");

            if (officeApp == null) return false;

            dynamic app = officeApp;
            app.Visible = false;
            app.DisplayAlerts = false;

            dynamic doc;
            if (ext is ".doc" or ".docx")
            {
                doc = app.Documents.Open(file);
                for (int i = 0; i < settings.Copies; i++)
                    doc.PrintOut();
                doc.Close(false);
                app.Quit();
            }
            else if (ext is ".xls" or ".xlsx")
            {
                doc = app.Workbooks.Open(file);
                for (int i = 0; i < settings.Copies; i++)
                    doc.PrintOut();
                doc.Close(false);
                app.Quit();
            }
            else if (ext is ".ppt" or ".pptx")
            {
                doc = app.Presentations.Open(file);
                for (int i = 0; i < settings.Copies; i++)
                    doc.PrintOut();
                doc.Close();
                app.Quit();
            }
            else
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? CreateOfficeProgID(string progId)
    {
        try
        {
            var type = Type.GetTypeFromProgID(progId);
            if (type != null)
                return Activator.CreateInstance(type);
        }
        catch { }
        return null;
    }

    // ========================================================================
    // ПЕЧАТЬ ЧЕРЕЗ СИСТЕМНУЮ ОБОЛОЧКУ (PDF, RTF через Shell)
    // ========================================================================

    private static void ShellPrint(string file)
    {
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

    // ========================================================================
    // ОТРИСОВКА СТРАНИЦЫ (изображения и текст)
    // ========================================================================

    private static void DrawPage(PrintPageEventArgs e, string file, string? pageRange = null)
    {
        var margin = e.MarginBounds;
        var ext = Path.GetExtension(file);

        if (ext is ".txt" or ".log" or ".csv")
        {
            using var font = new Font("Consolas", 10);
            float y = margin.Top;

            var lines = File.ReadLines(file).ToList();
            int totalLines = lines.Count;
            int startLine = 0, endLine = totalLines;

            // 2. Диапазон страниц — поддержка HasMorePages
            if (!string.IsNullOrWhiteSpace(pageRange))
            {
                var range = ParsePageRange(pageRange, totalLines);
                startLine = range.start;
                endLine = range.end;
            }

            for (int i = startLine; i < endLine; i++)
            {
                string text = lines[i] ?? "";
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
            // Изображение — вписываем в страницу с сохранением пропорций
            using var image = Image.FromFile(file);
            float scale = Math.Min((float)margin.Width / image.Width, (float)margin.Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            float x = margin.Left + (margin.Width - w) / 2;
            float y = margin.Top + (margin.Height - h) / 2;
            e.Graphics.DrawImage(image, x, y, w, h);
        }
    }

    // ========================================================================
    // ПАРСИНГ ДИАПАЗОНА СТРАНИЦ ("1,3,5-7")
    // ========================================================================

    private static (int start, int end) ParsePageRange(string range, int totalLines)
    {
        try
        {
            var parts = range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int start = int.MaxValue, end = int.MinValue;
            foreach (var part in parts)
            {
                if (part.Contains('-'))
                {
                    var sub = part.Split('-');
                    int a = int.Parse(sub[0].Trim());
                    int b = int.Parse(sub[1].Trim());
                    start = Math.Min(start, a);
                    end = Math.Max(end, b);
                }
                else
                {
                    int n = int.Parse(part.Trim());
                    start = Math.Min(start, n);
                    end = Math.Max(end, n);
                }
            }
            start = Math.Clamp(start, 1, totalLines);
            end = Math.Clamp(end, 1, totalLines);
            return (start - 1, end);
        }
        catch
        {
            return (0, totalLines);
        }
    }

    // ========================================================================
    // ПРИМЕНЕНИЕ НАСТРОЕК К PrinterSettings
    // ========================================================================

    private static void ApplyPrinterSettings(PrinterSettings settings, int copies, bool color, bool duplex,
        bool duplexVertical, bool landscape, string paperSize)
    {
        settings.Copies = (short)Math.Min(copies, 999);
        settings.DefaultPageSettings.Color = color;
        settings.DefaultPageSettings.Landscape = landscape;

        if (duplex)
            settings.Duplex = duplexVertical ? Duplex.Vertical : Duplex.Horizontal;
        else
            settings.Duplex = Duplex.Simplex;

        // 3. Формат бумаги
        try
        {
            foreach (PaperSize ps in settings.PaperSizes)
            {
                if (string.Equals(ps.PaperName, paperSize, StringComparison.OrdinalIgnoreCase))
                {
                    settings.DefaultPageSettings.PaperSize = ps;
                    break;
                }
            }
        }
        catch { }
    }

    // ========================================================================
    // 8. СТАТУС ПРИНТЕРА (WMI Win32_Printer)
    // ========================================================================

    public static string GetPrinterStatus(string printerName)
    {
        try
        {
            var mgmtType = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
            if (mgmtType == null)
            {
                try
                {
                    var asm = System.Reflection.Assembly.Load("System.Management");
                    mgmtType = asm.GetType("System.Management.ManagementObjectSearcher");
                }
                catch { }
            }

            if (mgmtType != null)
            {
                var searcher = (IDisposable)Activator.CreateInstance(mgmtType, $"SELECT * FROM Win32_Printer WHERE Name='{printerName}'")!;
                using (searcher)
                {
                    var getMethod = mgmtType.GetMethod("Get", Type.EmptyTypes);
                    var collection = getMethod!.Invoke(searcher, null)!;
                    foreach (var p in (System.Collections.IEnumerable)collection)
                    {
                        var offlineProp = p.GetType().GetProperty("Item");
                        var offline = offlineProp?.GetValue(p, new object[] { "WorkOffline" });
                        var status = offlineProp?.GetValue(p, new object[] { "Status" });

                        if (offline != null && (bool)offline)
                            return "Оффлайн";

                        if (status != null)
                            return $"Статус: {status}";

                        return "Готов к печати";
                    }
                }
            }
        }
        catch { }
        return "Неизвестно";
    }

    // ========================================================================
    // АВТООБНОВЛЕНИЕ С GITHUB
    // ========================================================================

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("FormatWheel-PrintPlugin", CurrentVersion));

            var json = await _httpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (string.IsNullOrEmpty(tag)) return;

            var remoteVersion = tag.TrimStart('v');
            if (!Version.TryParse(remoteVersion, out var remoteVer)) return;
            if (!Version.TryParse(CurrentVersion, out var localVer)) return;

            if (remoteVer <= localVer) return;

            string? installerUrl = null;
            string? installerName = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Print-Setup"))
                    {
                        installerUrl = asset.GetProperty("browser_download_url").GetString();
                        installerName = name;
                        break;
                    }
                }
            }

            var message = $"Доступна версия {remoteVersion} плагина печати (у вас {CurrentVersion}).\n\n";
            if (installerUrl != null)
                message += $"Скачать установщик: {installerUrl}";
            else
                message += $"Перейдите на страницу релизов: https://github.com/{RepoOwner}/{RepoName}/releases";

            MessageBox.Show(message, "Плагин печати: обновление", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            // Тихий отказ — автообновление не критично
        }
    }
}
