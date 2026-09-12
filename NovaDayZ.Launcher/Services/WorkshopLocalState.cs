using System.IO;
using System.Text.RegularExpressions;

namespace NovaDayZ.Services;

public static class WorkshopLocalState
{
    public class LocalItem
    {
        public string Id { get; set; } = "";
        public uint TimeLastUpdated { get; set; }
    }

    public static Dictionary<string, LocalItem> Read()
    {
        var result = new Dictionary<string, LocalItem>();

        foreach (var root in ModSyncService.GetWorkshopRoots())
        {
            // root = ...\steamapps\workshop\content\221100  ->  acf лежит на уровень выше
            var acf = Path.GetFullPath(Path.Combine(root, "..", "appworkshop_221100.acf"));
            if (!File.Exists(acf)) continue;

            var text = File.ReadAllText(acf);
            foreach (Match m in Regex.Matches(text, "\"(\\d{5,})\"\\s*\\{(.*?)\\}", RegexOptions.Singleline))
            {
                var tu = Regex.Match(m.Groups[2].Value, "\"timelastupdated\"\\s+\"(\\d+)\"");
                if (tu.Success && !result.ContainsKey(m.Groups[1].Value))
                {
                    result[m.Groups[1].Value] = new LocalItem
                    {
                        Id = m.Groups[1].Value,
                        TimeLastUpdated = uint.Parse(tu.Groups[1].Value)
                    };
                }
            }
        }
        return result;
    }
}