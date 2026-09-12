using System.Diagnostics;
using System.IO.Compression;

if (args.Length < 2) { Console.WriteLine("Usage: Updater.exe <zip> <targetDir>"); return 1; }
var zip = args[0]; var target = args[1];

foreach (var p in Process.GetProcessesByName("NovaDayZ"))
    p.WaitForExit(10000);

for (int attempt = 0; attempt < 5; attempt++)
{
    try { ZipFile.ExtractToDirectory(zip, target, overwriteFiles: true); break; }
    catch (IOException) { await Task.Delay(1000); }
}

try { File.Delete(zip); } catch { }
Process.Start(new ProcessStartInfo
{
    FileName = Path.Combine(target, "NovaDayZ.exe"),
    UseShellExecute = true
});
return 0;