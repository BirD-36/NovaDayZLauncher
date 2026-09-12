using System.Net.Http;
using System.Text.Json;

namespace NovaDayZ.Services;

public static class DzsaModService
{
    private const string DzsaUrl = "https://dayzsalauncher.com/api/v1/launcher/servers/dayz";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private static List<DzsaServer> _cache = new();
    private static DateTime _cacheTime = DateTime.MinValue;

    public class DzsaMod
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class DzsaServer
    {
        public string Ip { get; set; } = "";
        public int Port { get; set; }
        public int GamePort { get; set; }
        public List<DzsaMod> Mods { get; set; } = new();
    }

    public static async Task<List<DzsaMod>> GetServerModsAsync(string address, int gamePort, int queryPort)
    {
        var list = await GetIndexAsync();
        var result = new List<DzsaMod>();

        foreach (var s in list)
        {
            bool sameIp = string.Equals(s.Ip, address, StringComparison.OrdinalIgnoreCase);
            bool samePort = s.Port == queryPort || s.Port == gamePort || s.GamePort == gamePort;
            if (!sameIp || !samePort) continue;

            result.AddRange(s.Mods);
            return result;
        }
        return result;
    }

    private static async Task<List<DzsaServer>> GetIndexAsync()
    {
        if (_cache.Count > 0 && (DateTime.Now - _cacheTime).TotalMinutes < 5)
            return _cache;

        try
        {
            using var resp = await Http.GetAsync(DzsaUrl);
            resp.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());

            var parsed = new List<DzsaServer>();
            if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in res.EnumerateArray())
                {
                    var server = new DzsaServer();

                    if (item.TryGetProperty("endpoint", out var ep))
                    {
                        server.Ip = ep.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "" : "";
                        server.Port = GetInt(ep, "port");
                    }
                    server.GamePort = GetInt(item, "gamePort");

                    if (item.TryGetProperty("mods", out var modsEl) && modsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in modsEl.EnumerateArray())
                        {
                            var name = m.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                            var id = ExtractId(m);
                            if (!string.IsNullOrWhiteSpace(name) || id > 0)
                                server.Mods.Add(new DzsaMod { Id = id, Name = name });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(server.Ip))
                        parsed.Add(server);
                }
            }

            if (parsed.Count > 0)
            {
                _cache = parsed;
                _cacheTime = DateTime.Now;
            }
            return parsed.Count > 0 ? parsed : _cache;
        }
        catch
        {
            return _cache;
        }
    }

    // Известные имена полей + универсальный поиск числа в диапазоне Workshop ID
    private static long ExtractId(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object) return 0;

        string[] props = { "steamWorkshopId", "steamId", "workshopId", "workshop_id", "modId", "mod_id",
                           "publishedFileId", "published_file_id", "ugcId", "id" };
        foreach (var p in props)
        {
            if (!m.TryGetProperty(p, out var v)) continue;
            var s = v.ValueKind == JsonValueKind.String ? v.GetString()
                  : v.ValueKind == JsonValueKind.Number ? v.ToString() : null;
            if (s != null && long.TryParse(s, out var id) && id > 100000) return id;
        }

        foreach (var prop in m.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number &&
                prop.Value.TryGetInt64(out var n) && n > 100000 && n < 90000000000L)
                return n;
        }
        return 0;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
        return 0;
    }
}