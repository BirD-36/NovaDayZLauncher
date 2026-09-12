using System.IO;
using System.Text.Json;

namespace NovaDayZ.Services;

public class SettingsService
{
    public class AppSettings
    {
        public string Nickname { get; set; } = "Survivor";
        public string GameRoot { get; set; } = "";
        public string ExtraArgs { get; set; } = "-noSplash -skipIntro";
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaDayZ", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}