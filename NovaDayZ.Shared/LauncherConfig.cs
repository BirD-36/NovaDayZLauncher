using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaDayZ.Shared;

public class LauncherConfig {
    public LauncherSection Launcher { get; set; } = new();
    public List<ServerEntry> Servers { get; set; } = new();
    public Dictionary<string, List<long>> Mods { get; set; } = new();
    public List<NewsItem> News { get; set; } = new();
}
public class LauncherSection {
    public string Version { get; set; } = "1.0.0";
    public string UpdateUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
}
public class ServerEntry : INotifyPropertyChanged {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int GamePort { get; set; }
    public int QueryPort { get; set; }
    public string Map { get; set; } = "";
    public string Thumb { get; set; } = "";
    public long CollectionId { get; set; }   // ID коллекции Workshop с модами сервера

    private int _players;
    public int Players { get => _players; set { _players = value; Notify(); } }
    private int _maxPlayers;
    public int MaxPlayers { get => _maxPlayers; set { _maxPlayers = value; Notify(); } }
    private int _pingMs;
    public int PingMs { get => _pingMs; set { _pingMs = value; Notify(); } }
    private string _status = "Offline";
    public string Status { get => _status; set { _status = value; Notify(); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void Notify([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public class NewsItem {
    public string Title { get; set; } = "";
    public string Image { get; set; } = "";
    public string Date { get; set; } = "";
}