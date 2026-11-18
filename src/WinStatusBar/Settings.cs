using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinStatusBar;

internal sealed class Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BarEdge Edge { get; set; } = BarEdge.Bottom;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinStatusBar", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Corrupt file: fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not fatal; the bar just won't remember the edge.
        }
    }
}
