using System.Net.Http;
using System.Text.Json;

namespace NovaDayZ.Services;

public static class SteamWorkshopApi
{
    public class FileDetails
    {
        public ulong publishedfileid { get; set; }
        public string title { get; set; } = "Mod";
        public uint time_updated { get; set; }
    }

    public static async Task<List<long>> GetCollectionIdsAsync(long collectionId)
    {
        using var http = new HttpClient();
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("itemcount", "1"),
            new("publishedfileids[0]", collectionId.ToString())
        };
        var resp = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/",
            new FormUrlEncodedContent(pairs));
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var list = new List<long>();
        foreach (var col in json.RootElement.GetProperty("response").GetProperty("collectiondetails").EnumerateArray())
        {
            if (col.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray())
                    if (child.TryGetProperty("publishedfileid", out var pid))
                        list.Add((long)pid.GetUInt64());
        }
        return list;
    }

    public static async Task<Dictionary<string, FileDetails>> GetDetailsAsync(IEnumerable<long> ids)
    {
        var result = new Dictionary<string, FileDetails>();
        var idList = ids.Distinct().ToList();
        if (!idList.Any()) return result;

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("format", "json"),
            new("itemcount", idList.Count.ToString())
        };
        for (int i = 0; i < idList.Count; i++)
            pairs.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", idList[i].ToString()));

        using var http = new HttpClient();
        var resp = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            new FormUrlEncodedContent(pairs));
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        foreach (var el in json.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            var d = new FileDetails
            {
                publishedfileid = el.GetProperty("publishedfileid").GetUInt64(),
                title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "Mod" : "Mod",
                time_updated = el.TryGetProperty("time_updated", out var u) ? u.GetUInt32() : 0
            };
            result[d.publishedfileid.ToString()] = d;
        }
        return result;
    }
}