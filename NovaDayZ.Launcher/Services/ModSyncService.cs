using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NovaDayZ.Services;

public static class ModSyncService
{
    public static string GetSteamRoot()
    {
        return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", null)?.ToString()
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null)?.ToString()
            ?? @"C:\Program Files (x86)\Steam";
    }

    // Все Steam-библиотеки: основная + пути из libraryfolders.vdf
    public static List<string> GetWorkshopRoots()
    {
        var roots = new List<string>
        {
            Path.Combine(GetSteamRoot(), "steamapps", "workshop", "content", "221100")
        };

        var vdf = Path.Combine(GetSteamRoot(), "config", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                var p = Path.Combine(lib, "steamapps", "workshop", "content", "221100");
                if (!roots.Contains(p)) roots.Add(p);
            }
        }
        return roots;
    }

    // Установленные моды: папки content\221100\<WorkshopID>
    public static Dictionary<string, string> GetInstalledMods()
    {
        var result = new Dictionary<string, string>();
        foreach (var root in GetWorkshopRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var id = Path.GetFileName(dir);
                if (id.Length >= 5 && id.All(char.IsDigit) && !result.ContainsKey(id))
                    result[id] = dir;
            }
        }
        return result;
    }

    // Название мода -> Workshop ID (читаем mod.cpp / meta.cpp)
    public static Dictionary<string, string> GetInstalledModNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetWorkshopRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var id = Path.GetFileName(dir);
                if (!(id.Length >= 5 && id.All(char.IsDigit))) continue;

                string text = null;
                var modCpp = Path.Combine(dir, "mod.cpp");
                var metaCpp = Path.Combine(dir, "meta.cpp");
                if (File.Exists(modCpp)) text = File.ReadAllText(modCpp);
                else if (File.Exists(metaCpp)) text = File.ReadAllText(metaCpp);
                if (text == null) continue;

                var nm = Regex.Match(text, "name\\s*=\\s*\"([^\"]+)\"");
                if (nm.Success && !result.ContainsKey(nm.Groups[1].Value))
                    result[nm.Groups[1].Value] = id;
            }
        }
        return result;
    }

    public class ModReport
    {
        public List<SteamWorkshopApi.FileDetails> Missing { get; } = new();
        public List<SteamWorkshopApi.FileDetails> Outdated { get; } = new();
        public bool HasWork => Missing.Count > 0 || Outdated.Count > 0;
    }

    // Проверка: чего не хватает и что устарело (с названиями для диалогов)
    public static async Task<ModReport> CheckAsync(IEnumerable<long> requiredIds, Dictionary<long, string> knownNames = null)
    {
        var report = new ModReport();
        var ids = requiredIds.Distinct().ToList();
        if (!ids.Any()) return report;

        var dirs = GetInstalledMods();
        var local = WorkshopLocalState.Read();

        Dictionary<string, SteamWorkshopApi.FileDetails> remote = new();
        try { remote = await SteamWorkshopApi.GetDetailsAsync(ids); }
        catch { }

        foreach (var id in ids)
        {
            var key = id.ToString();
            remote.TryGetValue(key, out var det);

            string title = !string.IsNullOrWhiteSpace(det?.title) && det.title != "Mod"
                ? det.title
                : (knownNames != null && knownNames.TryGetValue(id, out var kn) ? kn : "Mod " + id);

            var info = new SteamWorkshopApi.FileDetails
            {
                publishedfileid = (ulong)id,
                title = title,
                time_updated = det?.time_updated ?? 0
            };

            if (!dirs.ContainsKey(key))
                report.Missing.Add(info);
            else if (det != null && local.TryGetValue(key, out var li) && li.TimeLastUpdated < det.time_updated)
                report.Outdated.Add(info);
        }
        return report;
    }

    // Открыть страницу мода/коллекции в Steam (фолбэк, если Steamworks не инициализировался)
    public static void OpenSteamWorkshopPage(long publishedFileId)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://url/CommunityFilePage/{publishedFileId}",
                UseShellExecute = true
            });
        }
        catch { }
    }

    // Ждём, пока Steam-клиент скачает все моды списка
    public static async Task<bool> WaitForInstalledAsync(IEnumerable<long> ids, Action<string> status,
        Action tick = null, int timeoutMinutes = 20)
    {
        var need = ids.Distinct().ToList();
        if (!need.Any()) return true;
        var deadline = DateTime.Now.AddMinutes(timeoutMinutes);

        while (DateTime.Now < deadline)
        {
            tick?.Invoke();
            var installed = GetInstalledMods();
            var have = need.Count(id => installed.ContainsKey(id.ToString()));
            status?.Invoke($"Steam скачивает моды: {have}/{need.Count}");
            if (have == need.Count) return true;
            await Task.Delay(4000);
        }
        return false;
    }

    // Ждём, пока Steam-клиент обновит моды до свежих версий
    public static async Task<bool> WaitForUpdatedAsync(Dictionary<long, uint> targets, Action<string> status,
        Action tick = null, int timeoutMinutes = 15)
    {
        if (!targets.Any()) return true;
        var deadline = DateTime.Now.AddMinutes(timeoutMinutes);

        while (DateTime.Now < deadline)
        {
            tick?.Invoke();
            var local = WorkshopLocalState.Read();
            int have = 0;
            foreach (var kv in targets)
                if (local.TryGetValue(kv.Key.ToString(), out var li) && li.TimeLastUpdated >= kv.Value) have++;
            status?.Invoke($"Steam обновляет моды: {have}/{targets.Count}");
            if (have == targets.Count) return true;
            await Task.Delay(4000);
        }
        return false;
    }

    // Junction-ссылка на мод в папке игры (без прав администратора)
    public static void LinkModToGame(string workshopDir, string modName, string gameRoot)
    {
        var target = Path.Combine(gameRoot, "!Workshop", modName);
        if (Directory.Exists(target)) return;
        Directory.CreateDirectory(Path.Combine(gameRoot, "!Workshop"));

        var cmdArgs = "/c mklink /J \"" + target + "\" \"" + workshopDir + "\"";
        var psi = new ProcessStartInfo("cmd.exe", cmdArgs)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(5000);
    }
}