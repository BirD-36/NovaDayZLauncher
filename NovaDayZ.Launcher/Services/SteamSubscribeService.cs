using System.IO;
using System.Runtime.InteropServices;

namespace NovaDayZ.Services;

// Прямые вызовы плоских экспортов steam_api64.dll (схема как у ZNTK/DZSA)
public static class SteamSubscribeService
{
    private const uint DayZAppId = 221100;
    private static bool _initialized;
    private static IntPtr _ugc = IntPtr.Zero;

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaDayZ", "modsync.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " STEAMWORKS: " + msg + Environment.NewLine);
        }
        catch { }
    }

    [DllImport("steam_api64", EntryPoint = "SteamInternal_SteamAPI_Init")]
    private static extern bool Internal_Init();

    [DllImport("steam_api64", EntryPoint = "SteamAPI_RunCallbacks")]
    private static extern void Internal_RunCallbacks();

    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v021")] private static extern IntPtr UGC_v021();
    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v020")] private static extern IntPtr UGC_v020();
    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v019")] private static extern IntPtr UGC_v019();
    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v018")] private static extern IntPtr UGC_v018();
    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v017")] private static extern IntPtr UGC_v017();
    [DllImport("steam_api64", EntryPoint = "SteamAPI_SteamUGC_v016")] private static extern IntPtr UGC_v016();

    [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamUGC_SubscribeItem")]
    private static extern ulong SubscribeItem_Native(IntPtr ugc, ulong publishedFileId, bool bHighPriority);

    private static bool EnsureNative()
    {
        try
        {
            var dir = AppContext.BaseDirectory;

            // Критично: нативка читает steam_appid.txt из ТЕКУЩЕЙ папки процесса.
            // Переезжаем в папку exe ДО любых вызовов Steam.
            try { Directory.SetCurrentDirectory(dir); } catch { }

            File.WriteAllText(Path.Combine(dir, "steam_appid.txt"), DayZAppId.ToString());

            var dll = Path.Combine(dir, "steam_api64.dll");
            if (!File.Exists(dll))
            {
                var dayz = GameLauncher.DetectDayZPath();
                if (dayz == null) { Log("DayZ not found"); return false; }
                var src = Path.Combine(dayz, "steam_api64.dll");
                if (!File.Exists(src)) { Log("no dll in DayZ"); return false; }
                File.Copy(src, dll, true);
                Log("dll copied from DayZ");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log("EnsureNative error: " + ex.Message);
            return false;
        }
    }

    public static bool TryInit()
    {
        if (_initialized) return true;
        try
        {
            if (!EnsureNative()) return false;

            var ok = Internal_Init();
            Log("SteamInternal_SteamAPI_Init = " + ok);
            if (!ok) return false;

            var accessors = new Func<IntPtr>[] { UGC_v021, UGC_v020, UGC_v019, UGC_v018, UGC_v017, UGC_v016 };
            foreach (var acc in accessors)
            {
                var name = acc.Method.Name;
                try
                {
                    var p = acc();
                    Log($"accessor {name} -> {p.ToInt64()}");
                    if (p != IntPtr.Zero) { _ugc = p; break; }
                }
                catch (Exception ex)
                {
                    Log($"accessor {name} throw {ex.GetType().Name}");
                }
            }

            if (_ugc == IntPtr.Zero) return false;
            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Log("Init exception: " + ex.Message);
            return false;
        }
    }

    public static bool Subscribe(long publishedFileId)
    {
        if (!TryInit()) return false;
        try
        {
            var call = SubscribeItem_Native(_ugc, (ulong)publishedFileId, false);
            Internal_RunCallbacks();
            Log($"SubscribeItem sent: {publishedFileId} (call {call})");
            return true;
        }
        catch (Exception ex)
        {
            Log("Subscribe error: " + ex.Message);
            return false;
        }
    }

    public static void RunCallbacks()
    {
        if (!_initialized) return;
        try { Internal_RunCallbacks(); } catch { }
    }
}