using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaDayZ.Services;

public static class ModNameResolver
{
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaDayZ", "mod_name_cache.json");

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "mods_manifest.json");

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaDayZ", "modsync.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
        }
        catch { }
    }

    private static readonly HashSet<string> StandardRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "allowedBuild","clientPort","dedicated","island","language","platform",
        "requiredBuild","requiredVersion","timeLeft","sv_password","gamemode","password"
    };

    private static readonly HashSet<string> SkipValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "win","enoch","dayz","true","false","chernarusplus","livonia"
    };

    // ---- 1. Имена модов с сервера: устойчивый скан печатных строк из A2S_RULES ----
    public static async Task<List<string>> GetServerModNamesAsync(string ip, int queryPort)
    {
        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Parse(ip), queryPort);
        using var cts = new CancellationTokenSource(5000);
        byte[] req = { 0xFF,0xFF,0xFF,0xFF,0x56,0xFF,0xFF,0xFF,0xFF };

        await udp.SendAsync(req, req.Length, ep);
        var res = await udp.ReceiveAsync(cts.Token);
        var b = res.Buffer;

        if (b.Length >= 9 && b[4] == 0x41)   // challenge подставляется ВМЕСТО заглушки
        {
            var req2 = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x56, 0, 0, 0, 0 };
            Array.Copy(b, 5, req2, 5, 4);
            await udp.SendAsync(req2, req2.Length, ep);
            res = await udp.ReceiveAsync(cts.Token);
            b = res.Buffer;
        }

        if (b.Length < 10 || b[4] != 0x45)
            throw new Exception("Bad A2S_RULES response");

        // Сканируем все печатные последовательности: имена модов и правила.
        // Бинарные хеши сами выступают разделителями — смещения не важны.
        var runs = new List<string>();
        var sb = new StringBuilder();
        for (int j = 6; j < b.Length; j++)
        {
            if (b[j] >= 32 && b[j] < 127) sb.Append((char)b[j]);
            else
            {
                if (sb.Length > 0) { runs.Add(sb.ToString()); sb.Clear(); }
            }
        }
        if (sb.Length > 0) runs.Add(sb.ToString());

        var names = runs
            .Select(r => r.Trim())
            .Where(r => r.Length >= 3)
            .Where(r => !r.All(char.IsDigit))
            .Where(r => !StandardRules.Contains(r))
            .Where(r => !SkipValues.Contains(r))
            .Where(r => r.All(c => char.IsLetterOrDigit(c) || " -_.[]()'+".Contains(c)))
            .Distinct()
            .ToList();

        Log($"RULES {ip}:{queryPort} -> {names.Count} names: {string.Join(", ", names)}");
        return names;
    }

    // ---- 2. Имя -> Workshop ID: манифест -> локальные моды -> кэш ----
    public static async Task<Dictionary<string, long>> ResolveAsync(IEnumerable<string> names)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var manifest = LoadManifest();
        var cache = LoadCache();
        var localNames = ModSyncService.GetInstalledModNames();
        var unresolved = new List<string>();

        foreach (var name in names)
        {
            if (manifest.TryGetValue(name, out var mid)) { result[name] = mid; Log($"RESOLVE manifest: {name} -> {mid}"); continue; }
            if (localNames.TryGetValue(name, out var lid)) { result[name] = long.Parse(lid); Log($"RESOLVE local: {name} -> {lid}"); continue; }
            if (cache.TryGetValue(name, out var cid)) { result[name] = cid; Log($"RESOLVE cache: {name} -> {cid}"); continue; }
            unresolved.Add(name);
        }

        if (unresolved.Any())
            Log($"RESOLVE unresolved: {string.Join(", ", unresolved)}");

        Log($"RESOLVE total: {result.Count} ids");
        return result;
    }

    private static Dictionary<string, long> LoadManifest()
    {
        try
        {
            if (File.Exists(ManifestPath))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(ManifestPath)) ?? new();
        }
        catch { }
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, long> LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(CachePath)) ?? new();
        }
        catch { }
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }
}