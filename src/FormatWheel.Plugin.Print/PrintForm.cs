using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace FormatWheel.PrintPlugin;

// Диалоговое окно печати с полным набором настроек
internal sealed class PrintForm : Form
{
    private readonly string[] _files;
    private readonly PluginSettings _settings;

    // Элементы управления
    private readonly ComboBox _printerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Height = 24 };
    private readonly Label _printerStatus = new() { Height = 16, ForeColor = System.Drawing.Color.Gray, Font = new Font("Segoe UI", 8.5f), AutoSize = true };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 999, Value = 1, Width = 60, Height = 24 };
    private readonly RadioButton _radioAll = new() { Text = "Все страницы", Checked = true, AutoSize = true };
    private readonly RadioButton _radioPages = new() { Text = "Страницы:", AutoSize = true };
    private readonly TextBox _pageRange = new() { Width = 120, Height = 24, Enabled = false };
    private readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Height = 24 };
    private readonly ComboBox _paperSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Height = 24 };
    private readonly CheckBox _colorCheck = new() { Text = "Цветная печать", Checked = true, AutoSize = true };
    private readonly ComboBox _duplex = new() { DropDownStyle = ComboBoxStyle.DropDownList, Height = 24 };
    private readonly CheckBox _printToFile = new() { Text = "Печать в файл", AutoSize = true };

    public PrinterSettings PrinterSettings { get; private set; } = new PrinterSettings();
    public int Copies => (int)_copies.Value;
    public string? PageRange => _radioPages.Checked ? _pageRange.Text.Trim() : null;
    public bool Landscape => _orientation.SelectedItem?.ToString() == "Альбомная";
    public string PaperSizeName => _paperSize.SelectedItem?.ToString() ?? "A4";
    public bool Color => _colorCheck.Checked;
    public bool DuplexEnabled => _duplex.SelectedItem?.ToString() != "Односторонняя";
    public bool DuplexVertical => _duplex.SelectedItem?.ToString() == "Двусторонняя (по вертикали)";
    public bool PrintToFile => _printToFile.Checked;

    public PrintForm(string[] files, PluginSettings settings)
    {
        _files = files;
        _settings = settings;

        Text = "Печать — Format Wheel";
        Width = 500;
        Height = 540;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);

        // Заголовок
        var title = new Label
        {
            Text = $"Файлов для печати: {files.Length}",
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(0, 4, 0, 0),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(0x20, 0x20, 0x20)
        };

        var hint = new Label
        {
            Text = "Печать доступна для фото и документов. Видео и аудио не поддерживаются.",
            Dock = DockStyle.Top,
            Height = 18,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.Gray
        };

        // Принтер
        var printerGroup = MakeGroup("Принтер", 80);
        var printerTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        printerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        printerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        printerTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        printerTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        printerTable.Controls.Add(new Label { Text = "Принтер:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _printerCombo.Dock = DockStyle.Fill;
        printerTable.Controls.Add(_printerCombo, 1, 0);
        printerTable.Controls.Add(new Label(), 0, 1);
        _printerStatus.Dock = DockStyle.Fill;
        printerTable.Controls.Add(_printerStatus, 1, 1);
        printerGroup.Controls.Add(printerTable);

        // Загружаем принтеры
        foreach (string p in PrinterSettings.InstalledPrinters)
            _printerCombo.Items.Add(p);
        if (_printerCombo.Items.Count > 0)
        {
            string last = _settings.LastPrinter;
            int idx = -1;
            for (int i = 0; i < _printerCombo.Items.Count; i++)
                if (_printerCombo.Items[i]?.ToString() == last) { idx = i; break; }
            if (idx < 0) idx = 0;
            _printerCombo.SelectedIndex = idx;
            PrinterSettings.PrinterName = _printerCombo.SelectedItem?.ToString() ?? "";
            UpdatePrinterStatus();
        }
        else
        {
            _printerCombo.Items.Add("Нет доступных принтеров");
            _printerCombo.SelectedIndex = 0;
            _printerStatus.Text = "Принтеров не найдено";
            _printerStatus.ForeColor = System.Drawing.Color.Red;
        }
        _printerCombo.SelectedIndexChanged += (_, _) =>
        {
            var name = _printerCombo.SelectedItem?.ToString() ?? "";
            if (name != "Нет доступных принтеров")
            {
                PrinterSettings.PrinterName = name;
                _settings.LastPrinter = name;
                UpdatePrinterStatus();
            }
        };

        // Кнопка свойств под принтером
        var propsBtn = new Button { Text = "Свойства принтера", Width = 140, Height = 26, Font = new Font("Segoe UI", 9f) };
        propsBtn.Click += (_, _) => ShowPrinterProperties();
        var propsPanel = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 4, 0, 0) };
        propsPanel.Controls.Add(propsBtn);
        propsBtn.Location = new Point(0, 2);

        // Количество копий
        var copiesGroup = MakeGroup("Копии", 42);
        var copiesTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
        copiesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        copiesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        copiesTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        copiesTable.Controls.Add(new Label { Text = "Количество:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _copies.Dock = DockStyle.Left;
        copiesTable.Controls.Add(_copies, 1, 0);
        copiesGroup.Controls.Add(copiesTable);

        // Диапазон страниц
        var rangeGroup = MakeGroup("Диапазон страниц", 42);
        var rangeTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(4) };
        rangeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rangeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rangeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rangeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _radioAll.Dock = DockStyle.Fill;
        _radioPages.Dock = DockStyle.Fill;
        _pageRange.Dock = DockStyle.Left;
        _pageRange.Enabled = false;
        _radioPages.CheckedChanged += (_, _) => _pageRange.Enabled = _radioPages.Checked;
        rangeTable.Controls.Add(_radioAll, 0, 0);
        rangeTable.Controls.Add(_radioPages, 1, 0);
        rangeTable.Controls.Add(_pageRange, 2, 0);
        rangeGroup.Controls.Add(rangeTable);

        // Ориентация и бумага
        var pageGroup = MakeGroup("Лист", 64);
        var pageTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        pageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        pageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pageTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        pageTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        pageTable.Controls.Add(new Label { Text = "Ориентация:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _orientation.Dock = DockStyle.Fill;
        _orientation.Items.Add("Книжная");
        _orientation.Items.Add("Альбомная");
        _orientation.SelectedIndex = _settings.Landscape ? 1 : 0;
        pageTable.Controls.Add(_orientation, 1, 0);
        pageTable.Controls.Add(new Label { Text = "Размер бумаги:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _paperSize.Dock = DockStyle.Fill;
        _paperSize.Items.Add("A4");
        _paperSize.Items.Add("A3");
        _paperSize.Items.Add("A5");
        _paperSize.Items.Add("Letter");
        int paperIdx = Math.Max(0, Array.IndexOf(new[] { "A4", "A3", "A5", "Letter" }, _settings.PaperSize));
        _paperSize.SelectedIndex = paperIdx;
        pageTable.Controls.Add(_paperSize, 1, 1);
        pageGroup.Controls.Add(pageTable);

        // Цвет / Дуплекс / в файл
        var optsGroup = MakeGroup("Параметры", 42);
        var optsTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(4) };
        optsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _colorCheck.Checked = _settings.Color;
        _colorCheck.Dock = DockStyle.Fill;
        optsTable.Controls.Add(_colorCheck, 0, 0);
        _duplex.Dock = DockStyle.Fill;
        _duplex.Items.Add("Односторонняя");
        _duplex.Items.Add("Двусторонняя (по вертикали)");
        _duplex.Items.Add("Двусторонняя (по горизонтали)");
        _duplex.SelectedIndex = _settings.Duplex ? (_settings.DuplexVertical ? 1 : 2) : 0;
        optsTable.Controls.Add(_duplex, 1, 0);
        _printToFile.Checked = _settings.PrintToFile;
        _printToFile.Dock = DockStyle.Fill;
        optsTable.Controls.Add(_printToFile, 2, 0);
        optsGroup.Controls.Add(optsTable);

        // Кнопки
        var btnPrint = new Button { Text = "Печать", DialogResult = DialogResult.OK, Width = 100, Height = 32, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var btnCancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 8, 0, 0) };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnPrint);
        btnPrint.Location = new Point(btnPanel.Width - 110, 8);
        btnCancel.Location = new Point(btnPanel.Width - 210, 8);
        btnPanel.Resize += (_, _) =>
        {
            btnPrint.Location = new Point(btnPanel.Width - 110, 8);
            btnCancel.Location = new Point(btnPanel.Width - 210, 8);
        };

        AcceptButton = btnPrint;
        CancelButton = btnCancel;

        // Добавляем в правильном порядке (снизу вверх, т.к. Dock Top)
        Controls.Add(btnPanel);
        Controls.Add(optsGroup);
        Controls.Add(pageGroup);
        Controls.Add(rangeGroup);
        Controls.Add(copiesGroup);
        Controls.Add(propsPanel);
        Controls.Add(printerGroup);
        Controls.Add(hint);
        Controls.Add(title);
    }

    private static GroupBox MakeGroup(string text, int height)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = height,
            Padding = new Padding(6, 4, 6, 4),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
    }

    private void UpdatePrinterStatus()
    {
        var name = PrinterSettings.PrinterName;
        if (name == "Нет доступных принтеров")
        {
            _printerStatus.Text = "Принтеров не найдено";
            _printerStatus.ForeColor = System.Drawing.Color.Red;
            return;
        }

        // Проверка статуса через reflection (чтобы не ломать JIT при отсутствии System.Management)
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
                var searcher = (IDisposable)Activator.CreateInstance(mgmtType, $"SELECT * FROM Win32_Printer WHERE Name='{name}'")!;
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
                        {
                            _printerStatus.Text = "Принтер оффлайн";
                            _printerStatus.ForeColor = System.Drawing.Color.Red;
                            return;
                        }
                        if (status != null)
                        {
                            _printerStatus.Text = $"Статус: {status}";
                            _printerStatus.ForeColor = System.Drawing.Color.Green;
                            return;
                        }
                    }
                }
                _printerStatus.Text = "Готов к печати";
                _printerStatus.ForeColor = System.Drawing.Color.Green;
                return;
            }
        }
        catch { }

        _printerStatus.Text = "Выбран";
        _printerStatus.ForeColor = System.Drawing.Color.Green;
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
            _settings.LastPrinter = PrinterSettings.PrinterName;
            _settings.Save();
            for (int i = 0; i < _printerCombo.Items.Count; i++)
                if (_printerCombo.Items[i]?.ToString() == PrinterSettings.PrinterName)
                {
                    _printerCombo.SelectedIndex = i;
                    break;
                }
            UpdatePrinterStatus();
        }
    }
}
