using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NovaDayZ.Shared;
using NovaDayZ.Services;

namespace NovaDayZ;

public partial class MainWindow : Window
{
    public ObservableCollection<ServerEntry> Servers { get; } = new();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly ServerStatusService _status = new();
    private readonly SettingsService.AppSettings _settings = SettingsService.Load();
    private LauncherConfig _config;

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaDayZ", "modsync.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " FLOW: " + msg + Environment.NewLine);
        }
        catch { }
    }

    public MainWindow()
    {
        InitializeComponent();
        ServersList.ItemsSource = Servers;
        Loaded += MainWindow_Loaded;
        _pollTimer.Tick += async (s, e) => await PollAsync();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Загрузка конфигурации...";
        _config = await ConfigService.LoadAsync();
        VersionLabel.Text = "v" + _config.Launcher.Version;

        Servers.Clear();
        foreach (var sv in _config.Servers) Servers.Add(sv);

        await PollAsync();
        _pollTimer.Start();
        StatusText.Text = "Готов к запуску";
    }

    private async Task PollAsync()
    {
        await Task.WhenAll(Servers.ToList().Select(async sv =>
        {
            await PollOneAsync(sv);
            await Dispatcher.InvokeAsync(UpdateDiagnostics);
        }));
    }

    private void UpdateDiagnostics()
    {
        Title = "NovaDayZ Launcher — " + string.Join(" | ", Servers.Select(s =>
            $"#{s.Id}:{s.Status}:{s.Players}/{s.MaxPlayers}:{s.PingMs}ms:q{s.QueryPort}"));
    }

    private async Task PollOneAsync(ServerEntry sv)
    {
        int[] candidates = { sv.QueryPort, sv.GamePort, sv.GamePort + 1, 27016 };
        foreach (var port in candidates.Where(p => p > 0).Distinct())
        {
            try
            {
                var (p, m, ping) = await _status.QueryAsync(sv.Address, port);
                sv.Players = p; sv.MaxPlayers = m; sv.PingMs = ping;
                sv.Status = "Online";
                sv.QueryPort = port;
                return;
            }
            catch { }
        }
        sv.Players = 0; sv.MaxPlayers = 0; sv.PingMs = 0; sv.Status = "Offline";
    }

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var btn = (System.Windows.Controls.Button)sender;
        var sv = (ServerEntry)btn.DataContext;
        Log($"PLAY click: {sv.Name} ({sv.Address}:{sv.GamePort}, q{sv.QueryPort}), status={sv.Status}");

        if (sv.Status != "Online")
        {
            MessageBox.Show("Сервер сейчас выключен. Попробуйте позже.", "NovaDayZ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btn.IsEnabled = false;
        var required = new List<long>();
        try
        {
            var knownNames = new Dictionary<long, string>();
            try
            {
                StatusText.Text = "Запрашиваю список модов сервера (DZSA)...";
                var dzsaMods = await DzsaModService.GetServerModsAsync(sv.Address, sv.GamePort, sv.QueryPort);
                Log($"DZSA mods: {dzsaMods.Count}");

                if (dzsaMods.Any())
                {
                    var withIds = dzsaMods.Where(m => m.Id > 0).ToList();
                    if (withIds.Any())
                    {
                        required = withIds.Select(m => m.Id).Distinct().ToList();
                        foreach (var m in withIds)
                            if (!knownNames.ContainsKey(m.Id)) knownNames[m.Id] = m.Name;
                    }
                    else
                    {
                        StatusText.Text = $"Определяю Workshop ID по названиям ({dzsaMods.Count})...";
                        var nameToId = await ModNameResolver.ResolveAsync(dzsaMods.Select(m => m.Name));
                        required = nameToId.Values.Distinct().ToList();
                        foreach (var kv in nameToId)
                            if (!knownNames.ContainsKey(kv.Value)) knownNames[kv.Value] = kv.Key;

                        var unresolved = dzsaMods.Select(m => m.Name).Where(n => !nameToId.ContainsKey(n)).ToList();
                        if (required.Count == 0 && unresolved.Any())
                        {
                            Log("ABORT: no ids resolved for names: " + string.Join(", ", unresolved));
                            MessageBox.Show(
                                "Сервер использует моды:\n" + string.Join("\n", unresolved.Select(u => "• " + u)) +
                                "\n\nWorkshop ID для них не известны лаунчеру. Запуск отменён.",
                                "NovaDayZ — моды", MessageBoxButton.OK, MessageBoxImage.Error);
                            StatusText.Text = "Готов к запуску";
                            return;
                        }
                    }
                }
                Log($"required ids: {required.Count}");

                if (required.Any())
                {
                    StatusText.Text = "Проверяю установленные моды...";
                    var report = await ModSyncService.CheckAsync(required, knownNames);
                    Log($"check: missing={report.Missing.Count}, outdated={report.Outdated.Count}");

                    if (report.HasWork)
                    {
                        var lines = report.Missing.Select(m => $"• {m.title} — не установлен")
                            .Concat(report.Outdated.Select(o => $"• {o.title} — доступно обновление"));
                        var ans = MessageBox.Show(
                            "Для игры на этом сервере нужны моды:\n\n" + string.Join("\n", lines) +
                            "\n\nНачать автоматическую загрузку через Steam?",
                            "NovaDayZ — моды", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        Log("dialog answer: " + ans);
                        if (ans != MessageBoxResult.Yes) { StatusText.Text = "Готов к запуску"; return; }

                        var useSteamworks = SteamSubscribeService.TryInit();
                        Log("steamworks init: " + useSteamworks);

                        if (useSteamworks)
                        {
                            foreach (var m in report.Missing)
                                SteamSubscribeService.Subscribe((long)m.publishedfileid);
                        }
                        else if (sv.CollectionId > 0)
                        {
                            ModSyncService.OpenSteamWorkshopPage(sv.CollectionId);
                        }
                        else
                        {
                            foreach (var m in report.Missing)
                            {
                                ModSyncService.OpenSteamWorkshopPage((long)m.publishedfileid);
                                await Task.Delay(1000);
                            }
                        }

                        if (report.Missing.Any())
                        {
                            var ok = await ModSyncService.WaitForInstalledAsync(
                                report.Missing.Select(m => (long)m.publishedfileid),
                                t => Dispatcher.InvokeAsync(() => StatusText.Text = t),
                                () => SteamSubscribeService.RunCallbacks());
                            Log("wait installed: " + ok);
                            if (!ok)
                            {
                                MessageBox.Show("Steam не скачал все моды за отведённое время.\nЗапуск отменён: сервер требует эти моды.",
                                    "NovaDayZ — моды", MessageBoxButton.OK, MessageBoxImage.Error);
                                StatusText.Text = "Готов к запуску";
                                return;
                            }
                        }

                        if (report.Outdated.Any())
                        {
                            foreach (var o in report.Outdated)
                            {
                                if (useSteamworks) SteamSubscribeService.Subscribe((long)o.publishedfileid);
                                else ModSyncService.OpenSteamWorkshopPage((long)o.publishedfileid);
                                await Task.Delay(1000);
                            }

                            var targets = report.Outdated.ToDictionary(o => (long)o.publishedfileid, o => o.time_updated);
                            var okUpd = await ModSyncService.WaitForUpdatedAsync(targets,
                                t => Dispatcher.InvokeAsync(() => StatusText.Text = t),
                                () => SteamSubscribeService.RunCallbacks());
                            Log("wait updated: " + okUpd);
                            if (!okUpd)
                            {
                                MessageBox.Show("Steam не обновил моды до свежих версий.\nЗапуск отменён: сервер требует актуальные моды.",
                                    "NovaDayZ — моды", MessageBoxButton.OK, MessageBoxImage.Error);
                                StatusText.Text = "Готов к запуску";
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception modEx)
            {
                Log("mod sync exception: " + modEx);
                if (required.Any())
                {
                    MessageBox.Show("Ошибка синхронизации модов:\n" + modEx.Message +
                                    "\n\nЗапуск отменён: сервер требует моды, а они не установлены.",
                                    "NovaDayZ — моды", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Готов к запуску";
                    return;
                }

                var cont = MessageBox.Show(
                    "Не удалось получить список модов сервера:\n" + modEx.Message +
                    "\n\nЗапустить без синхронизации модов (игра докачает сама)?",
                    "NovaDayZ — моды", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (cont != MessageBoxResult.Yes) { StatusText.Text = "Готов к запуску"; return; }
            }

            var gameRoot = string.IsNullOrWhiteSpace(_settings.GameRoot) ? GameLauncher.DetectDayZPath() : _settings.GameRoot;
            Log("gameRoot: " + (gameRoot ?? "null"));
            if (gameRoot == null || !Directory.Exists(gameRoot))
                throw new Exception("DayZ не найден. Установите игру через Steam или укажите папку в настройках.");

            var installed = ModSyncService.GetInstalledMods();
            var modPaths = new List<string>();
            foreach (var id in required)
            {
                if (installed.TryGetValue(id.ToString(), out var dir))
                {
                    var linkName = "@ws_" + id;
                    ModSyncService.LinkModToGame(dir, linkName, gameRoot);
                    modPaths.Add(Path.Combine(gameRoot, "!Workshop", linkName));
                }
            }
            Log($"launch: mods linked={modPaths.Count}");

            StatusText.Text = $"Запускаю DayZ: {sv.Name}...";
            GameLauncher.Launch(sv.Address, sv.GamePort, _settings.Nickname, modPaths, _settings.ExtraArgs);
            Log("launch called");
            StatusText.Text = "Игра запущена. Хорошей выживки, survivor!";
        }
        catch (Exception ex)
        {
            Log("PLAY exception: " + ex);
            StatusText.Text = "Ошибка: " + ex.Message;
            MessageBox.Show(ex.Message, "NovaDayZ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private void OnServersClick(object sender, RoutedEventArgs e) { }
    private void OnModsClick(object sender, RoutedEventArgs e) { }
    private void OnSettingsClick(object sender, RoutedEventArgs e) { }
}