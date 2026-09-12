using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NovaDayZ.Services;

public static class GameLauncher
{
    public static string DetectDayZPath()
    {
        // 1. Приоритет — путь, указанный вручную в настройках
        var custom = SettingsService.Load().GameRoot;
        if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
            return custom;

        var candidates = new List<string>();

        // 2. Основная библиотека Steam
        var steam = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", null)?.ToString()
                 ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null)?.ToString()
                 ?? @"C:\Program Files (x86)\Steam";
        candidates.Add(Path.Combine(steam, "steamapps", "common", "DayZ"));

        // 3. Все дополнительные библиотеки Steam (libraryfolders.vdf)
        var vdf = Path.Combine(steam, "config", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                candidates.Add(Path.Combine(lib, "steamapps", "common", "DayZ"));
            }
        }

        return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "DayZ_x64.exe")));
    }

    public static string FindBeExe(string dayz)
    {
        var candidates = new[]
        {
            Path.Combine(dayz, "DayZ_BE.exe"),              // стандартное место (корень игры)
            Path.Combine(dayz, "BattlEye", "DayZ_BE.exe"),  // запасной вариант
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Launch(string serverAddress, int serverPort, string nickname, IEnumerable<string> modPaths, string extraArgs)
    {
        var dayz = DetectDayZPath();
        if (dayz == null)
            throw new Exception("DayZ не найден ни в одной библиотеке Steam. Укажите папку игры в настройках (GameRoot в %LocalAppData%\\NovaDayZ\\settings.json).");

        var be = FindBeExe(dayz);
        if (be == null)
            throw new Exception($"DayZ_BE.exe не найден в {dayz}. Проверьте целостность игры в Steam.");

        var args = $"-connect={serverAddress}:{serverPort} -port={serverPort} -name=\"{nickname}\" -nolauncher {extraArgs}";
        var mods = modPaths?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (mods != null && mods.Any())
            args += " \"-mod=" + string.Join(";", mods) + "\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = be,
            Arguments = args,
            WorkingDirectory = dayz,
            UseShellExecute = false
        });
    }
}