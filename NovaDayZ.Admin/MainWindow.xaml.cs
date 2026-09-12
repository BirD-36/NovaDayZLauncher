using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using NovaDayZ.Shared;

namespace NovaDayZ.Admin;

public partial class MainWindow : Window
{
    public ObservableCollection<ServerEntry> Servers { get; } = new();
    private LauncherConfig _cfg = new();
    private string _configPath = "config.json";
    private string _zipPath = "";

    public MainWindow() { InitializeComponent(); DataContext = this; }

    private void LoadClick(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "config.json|config.json" };
        if (d.ShowDialog() != true) return;
        _configPath = d.FileName;
        _cfg = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(_configPath)) ?? new();
        Servers.Clear();
        foreach (var sv in _cfg.Servers) Servers.Add(sv);
        VersionBox.Text = _cfg.Launcher.Version;
        UpdateUrlBox.Text = _cfg.Launcher.UpdateUrl;
        ShaBox.Text = _cfg.Launcher.Sha256;
        NotesBox.Text = _cfg.Launcher.ReleaseNotes;
    }

    private LauncherConfig Collect()
    {
        _cfg.Launcher.Version = VersionBox.Text;
        _cfg.Launcher.UpdateUrl = UpdateUrlBox.Text;
        _cfg.Launcher.Sha256 = ShaBox.Text;
        _cfg.Launcher.ReleaseNotes = NotesBox.Text;
        _cfg.Servers = Servers.ToList();
        return _cfg;
    }

    private void SaveClick(object s, RoutedEventArgs e)
    {
        var json = JsonSerializer.Serialize(Collect(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
        MessageBox.Show($"Сохранено: {_configPath}");
    }

    private void HashClick(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Архив обновления|*.zip" };
        if (d.ShowDialog() != true) return;
        _zipPath = d.FileName;
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(_zipPath);
        ShaBox.Text = Convert.ToHexString(sha.ComputeHash(fs));
    }

    private void PublishClick(object s, RoutedEventArgs e)
    {
        try
        {
            var cfg = Collect();
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);

            if (!string.IsNullOrWhiteSpace(FtpHostBox.Text))
            {
                UploadFtp(System.Text.Encoding.UTF8.GetBytes(json), $"ftp://{FtpHostBox.Text}/launcher/config.json");
                if (File.Exists(_zipPath))
                    UploadFtp(File.ReadAllBytes(_zipPath), $"ftp://{FtpHostBox.Text}/launcher/{Path.GetFileName(_zipPath)}");
            }
            MessageBox.Show("Опубликовано ✔");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void UploadFtp(byte[] data, string url)
    {
        var req = (FtpWebRequest)WebRequest.Create(url);
        req.Method = WebRequestMethods.Ftp.UploadFile;
        req.Credentials = new NetworkCredential(FtpUserBox.Text, FtpPassBox.Password);
        using var stream = req.GetRequestStream();
        stream.Write(data, 0, data.Length);
    }
}