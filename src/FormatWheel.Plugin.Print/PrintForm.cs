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
    private readonly ComboBox _printerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _printerStatus = new() { Height = 18, ForeColor = System.Drawing.Color.Gray, Font = new Font("Segoe UI", 8.5f) };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 999, Value = 1, Width = 70 };
    private readonly RadioButton _radioAll = new() { Text = "Все страницы", Checked = true };
    private readonly RadioButton _radioPages = new() { Text = "Страницы:" };
    private readonly TextBox _pageRange = new() { Enabled = false, Width = 100 };
    private readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _paperSize = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _colorCheck = new() { Text = "Цветная печать", Checked = true };
    private readonly ComboBox _duplex = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _printToFile = new() { Text = "Печать в файл" };

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
        Width = 520;
        Height = 560;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // Заголовок
        var title = new Label
        {
            Text = $"Файлов для печати: {files.Length}",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(15, 8, 0, 0),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
        };

        var hint = new Label
        {
            Text = "Печать доступна для фото и документов. Видео и аудио не поддерживаются.",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(15, 0, 0, 0),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.Gray
        };

        // Принтер
        var printerGroup = new GroupBox { Text = "Принтер", Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
        var printerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        printerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        printerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

        var printerLabel = new Label { Text = "Принтер:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _printerCombo.Dock = DockStyle.Fill;
        _printerCombo.Height = 26;
        _printerStatus.Dock = DockStyle.Fill;
        _printerStatus.TextAlign = ContentAlignment.MiddleLeft;

        printerLayout.Controls.Add(printerLabel, 0, 0);
        printerLayout.Controls.Add(_printerCombo, 1, 0);
        printerLayout.Controls.Add(new Label(), 0, 1);
        printerLayout.Controls.Add(_printerStatus, 1, 1);
        var propsBtn = new Button { Text = "Свойства", Dock = DockStyle.Fill, Height = 28 };
        propsBtn.Click += (_, _) => ShowPrinterProperties();
        printerLayout.Controls.Add(new Label(), 0, 2);
        printerLayout.Controls.Add(propsBtn, 1, 2);

        printerGroup.Controls.Add(printerLayout);

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

        // Количество копий
        var copiesGroup = new GroupBox { Text = "Копии", Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
        var copiesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        copiesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        copiesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        copiesLayout.Controls.Add(new Label { Text = "Количество:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _copies.Value = _settings.Copies;
        _copies.Dock = DockStyle.Fill;
        _copies.Height = 26;
        copiesLayout.Controls.Add(_copies, 1, 0);
        copiesGroup.Controls.Add(copiesLayout);

        // Диапазон страниц
        var rangeGroup = new GroupBox { Text = "Диапазон страниц", Dock = DockStyle.Top, Height = 70, Padding = new Padding(10) };
        var rangeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        rangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _radioAll.Dock = DockStyle.Fill;
        _radioPages.Dock = DockStyle.Fill;
        _pageRange.Dock = DockStyle.Fill;
        _pageRange.Enabled = false;
        _radioPages.CheckedChanged += (_, _) => _pageRange.Enabled = _radioPages.Checked;
        rangeLayout.Controls.Add(_radioAll, 0, 0);
        rangeLayout.Controls.Add(_radioPages, 1, 0);
        rangeLayout.Controls.Add(_pageRange, 2, 0);
        rangeGroup.Controls.Add(rangeLayout);

        // Ориентация и бумага
        var pageGroup = new GroupBox { Text = "Лист", Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
        var pageLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _orientation.Items.Add("Книжная");
        _orientation.Items.Add("Альбомная");
        _orientation.SelectedIndex = _settings.Landscape ? 1 : 0;
        _orientation.Dock = DockStyle.Fill;
        pageLayout.Controls.Add(new Label { Text = "Ориентация:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        pageLayout.Controls.Add(_orientation, 1, 0);

        _paperSize.Items.Add("A4");
        _paperSize.Items.Add("A3");
        _paperSize.Items.Add("A5");
        _paperSize.Items.Add("Letter");
        int paperIdx = Math.Max(0, Array.IndexOf(new[] { "A4", "A3", "A5", "Letter" }, _settings.PaperSize));
        _paperSize.SelectedIndex = paperIdx;
        _paperSize.Dock = DockStyle.Fill;
        pageLayout.Controls.Add(new Label { Text = "Размер бумаги:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        pageLayout.Controls.Add(_paperSize, 1, 1);
        pageGroup.Controls.Add(pageLayout);

        // Цвет / Дуплекс / в файл
        var optsGroup = new GroupBox { Text = "Параметры", Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
        var optsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        optsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        optsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        optsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _colorCheck.Checked = _settings.Color;
        _colorCheck.Dock = DockStyle.Fill;
        optsLayout.Controls.Add(_colorCheck, 0, 0);

        _duplex.Items.Add("Односторонняя");
        _duplex.Items.Add("Двусторонняя (по вертикали)");
        _duplex.Items.Add("Двусторонняя (по горизонтали)");
        _duplex.SelectedIndex = _settings.Duplex ? (_settings.DuplexVertical ? 1 : 2) : 0;
        _duplex.Dock = DockStyle.Fill;
        optsLayout.Controls.Add(_duplex, 1, 0);

        _printToFile.Checked = _settings.PrintToFile;
        _printToFile.Dock = DockStyle.Fill;
        optsLayout.Controls.Add(_printToFile, 2, 0);

        optsLayout.Controls.Add(new Label(), 0, 1);
        optsLayout.Controls.Add(new Label(), 1, 1);
        optsLayout.Controls.Add(new Label(), 2, 1);
        optsGroup.Controls.Add(optsLayout);

        // Кнопки
        var btnPrint = new Button { Text = "Печать", DialogResult = DialogResult.OK, Width = 110, Height = 34, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var btnCancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 100, Height = 34 };
        var btnProperties = new Button { Text = "Свойства принтера", Width = 130, Height = 30 };
        btnProperties.Click += (_, _) => ShowPrinterProperties();

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(10) };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnPrint);
        btnPanel.Controls.Add(btnProperties);

        btnPrint.Location = new Point(btnPanel.Width - 120, 13);
        btnCancel.Location = new Point(btnPanel.Width - 230, 13);
        btnProperties.Location = new Point(btnPanel.Width - 345, 13);
        btnPanel.Resize += (_, _) =>
        {
            btnPrint.Location = new Point(btnPanel.Width - 120, 13);
            btnCancel.Location = new Point(btnPanel.Width - 230, 13);
            btnProperties.Location = new Point(btnPanel.Width - 345, 13);
        };

        AcceptButton = btnPrint;
        CancelButton = btnCancel;

        Controls.Add(btnPanel);
        Controls.Add(optsGroup);
        Controls.Add(pageGroup);
        Controls.Add(rangeGroup);
        Controls.Add(copiesGroup);
        Controls.Add(printerGroup);
        Controls.Add(hint);
        Controls.Add(title);
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

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{name}'");
            foreach (var p in searcher.Get())
            {
                var offline = p["WorkOffline"];
                var status = p["Status"];
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
        catch { }
        _printerStatus.Text = "Не удалось проверить статус";
        _printerStatus.ForeColor = System.Drawing.Color.Gray;
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
