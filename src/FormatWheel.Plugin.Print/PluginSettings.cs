using System;
using System.IO;
using System.Text.Json;

namespace FormatWheel.PrintPlugin;

// Сохранение настроек плагина между запусками Format Wheel
internal sealed class PluginSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FormatWheel", "pluginsettings", "Print.json");

    public string LastPrinter { get; set; } = "";
    public int Copies { get; set; } = 1;
    public bool Color { get; set; } = true;
    public bool Duplex { get; set; } = false;
    public bool DuplexVertical { get; set; } = true;
    public string PaperSize { get; set; } = "A4";
    public bool Landscape { get; set; } = false;
    public bool PrintToFile { get; set; } = false;

    public static PluginSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
            }
        }
        catch { }
        return new PluginSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
