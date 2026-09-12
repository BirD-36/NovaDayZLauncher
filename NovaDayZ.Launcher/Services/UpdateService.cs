using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using NovaDayZ.Shared;

namespace NovaDayZ.Services;

public class UpdateService
{
    private readonly HttpClient _http = new();

    public bool IsUpdateAvailable(LauncherConfig cfg)
        => Version.TryParse(cfg.Launcher.Version, out var v) &&
           v > (Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0));

    public async Task<string> DownloadAsync(LauncherConfig cfg, IProgress<double> progress)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "novadayz_update.zip");
        using var resp = await _http.GetAsync(cfg.Launcher.UpdateUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var net = await resp.Content.ReadAsStreamAsync();
        using var file = File.Create(tmp);
        long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
        var buf = new byte[81920]; int n;
        while ((n = await net.ReadAsync(buf.AsMemory(0, buf.Length))) > 0)
        {
            await file.WriteAsync(buf.AsMemory(0, n));
            done += n;
            if (total > 0) progress.Report(done * 100.0 / total);
        }
        file.Close();

        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(tmp);
        var hash = Convert.ToHexString(await sha.ComputeHashAsync(fs));
        if (!hash.Equals(cfg.Launcher.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SHA256 mismatch");
        return tmp;
    }

    public void HandOverToUpdater(string zipPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "Updater.exe"),
            Arguments = $"\"{zipPath}\" \"{AppContext.BaseDirectory}\"",
            UseShellExecute = false
        });
        Environment.Exit(0);
    }
}