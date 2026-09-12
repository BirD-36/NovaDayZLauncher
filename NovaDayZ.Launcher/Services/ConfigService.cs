using System.Net.Http;
using System.Net.Http.Json;
using NovaDayZ.Shared;

namespace NovaDayZ.Services;

public static class ConfigService
{
    public const string ConfigUrl = "https://novadayz.com/launcher/config.json";

    public static async Task<LauncherConfig> LoadAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            return await http.GetFromJsonAsync<LauncherConfig>(ConfigUrl + "?t=" + DateTime.UtcNow.Ticks) ?? Fallback();
        }
        catch { return Fallback(); }
    }

    public static LauncherConfig Fallback() => new()
    {
        Servers = new List<ServerEntry>
        {
            new() { Id=1, Name="NovaDayZ #1 - PVP - Livonia",   Address="80.242.59.221", GamePort=2221, QueryPort=2321, Map="Livonia" },
            new() { Id=2, Name="NovaDayZ #2 - PVP - Chernarus", Address="80.242.59.221", GamePort=2222, QueryPort=2322, Map="Chernarus" },
            new() { Id=3, Name="NovaDayZ #3 - PVP - Livonia Vanilla",   Address="185.97.255.190", GamePort=3000, QueryPort=3001, Map="Livonia" },
            new() { Id=4, Name="NovaDayZ #4 - PVP - Chernarus Vanilla", Address="185.97.255.190", GamePort=4000, QueryPort=4001, Map="Chernarus" },
        }
    };
}