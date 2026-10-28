using System.Text;

namespace KenshiModdingTool;

// Dictionary shared by all mods (like the dictionaries in ESP-ESM Translator):
// exact original -> translation. Stored in %AppData%\KenshiModdingTool\dictionary.tsv
public static class GameDictionary
{
    private static Dictionary<string, string> map = new(StringComparer.Ordinal);       // your translations
    private static Dictionary<string, string> official = new(StringComparer.Ordinal);  // the game's official translation

    public static bool AutoLearn { get; set; } = true;   // learn on every save
    public static bool AutoApply { get; set; } = true;   // apply when a mod is opened
    public static bool OfficialAsked { get; set; }       // we already offered to build the official dictionary

    public static string OfficialLocale { get; private set; } = "";
    public static int OfficialCount => official.Count;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KenshiModdingTool");

    public static string FilePath => Path.Combine(Folder, "dictionary.tsv");
    private static string OptionsPath => Path.Combine(Folder, "dictionary-options.txt");

    public static int Count => map.Count;

    private static string OfficialPath(string locale) => Path.Combine(Folder, "official-" + locale + ".tsv");

    static GameDictionary()
    {
        try
        {
            if (File.Exists(FilePath)) ReadInto(File.ReadAllText(FilePath, Encoding.UTF8), map);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var f in Directory.GetFiles(Folder, "official-*.tsv"))
                {
                    ReadInto(File.ReadAllText(f, Encoding.UTF8), official);
                    OfficialLocale = Path.GetFileNameWithoutExtension(f).Substring("official-".Length);
                    break;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try
        {
            if (File.Exists(OptionsPath))
                foreach (var line in File.ReadAllLines(OptionsPath))
                {
                    if (line.StartsWith("learn=")) AutoLearn = line.EndsWith("1");
                    else if (line.StartsWith("apply=")) AutoApply = line.EndsWith("1");
                    else if (line.StartsWith("asked=")) OfficialAsked = line.EndsWith("1");
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static int ReadInto(string content, Dictionary<string, string> target)
    {
        int n = 0;
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            if (i == 0 && line.StartsWith("original\t")) continue; // header
            var cols = line.Split('\t');
            if (cols.Length < 2) continue;
            string o = Tools.Unesc(cols[0]);
            string t = Tools.Unesc(cols[1]);
            if (o.Length == 0 || t.Length == 0 || o == t) continue;
            target[o] = t;
            n++;
        }
        return n;
    }

    public static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder("original\ttranslation\n");
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(Tools.Esc(kv.Key)).Append('\t').Append(Tools.Esc(kv.Value)).Append('\n');
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void SaveOptions()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(OptionsPath, $"learn={(AutoLearn ? 1 : 0)}\napply={(AutoApply ? 1 : 0)}\nasked={(OfficialAsked ? 1 : 0)}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Adds the translated texts of these entries to the dictionary. Returns the number of additions/changes.
    public static int Learn(IEnumerable<Entry> entries)
    {
        int n = 0;
        foreach (var e in entries)
        {
            if (!e.Done) continue;
            if (!map.TryGetValue(e.Original, out var old) || old != e.Translation)
            {
                map[e.Original] = e.Translation;
                n++;
            }
        }
        if (n > 0) Save();
        return n;
    }

    // Compares an original mod with its translated version (same keys) and learns the differences.
    public static int LearnPairs(List<Entry> original, List<Entry> translated)
    {
        var byKey = new Dictionary<string, Entry>();
        foreach (var e in translated) byKey.TryAdd(e.Key, e);
        int n = 0;
        foreach (var o in original)
        {
            if (!byKey.TryGetValue(o.Key, out var t)) continue;
            if (t.Original.Length == 0 || t.Original == o.Original) continue;
            if (!map.TryGetValue(o.Original, out var old) || old != t.Original)
            {
                map[o.Original] = t.Original;
                n++;
            }
        }
        if (n > 0) Save();
        return n;
    }

    // Fills untranslated entries whose original is in the dictionary.
    public static int Apply(IEnumerable<Entry> entries)
    {
        int n = 0;
        foreach (var e in entries)
        {
            if (e.Translation.Length == 0 &&
                (map.TryGetValue(e.Original, out var tr) || official.TryGetValue(e.Original, out tr)))
            {
                e.Translation = tr;
                n++;
            }
        }
        return n;
    }

    // Builds the official dictionary from the translation shipped with the game:
    // <Kenshi>\locale\<language>\<language>.translation (a real .mod that keeps the original English
    // in the _original_* fields) + the base files in <Kenshi>\data (for names and descriptions).
    public static int BuildOfficial(string kenshiDir, string locale, Action<string> report)
    {
        string trPath = Path.Combine(kenshiDir, "locale", locale, locale + ".translation");
        if (!File.Exists(trPath))
            throw new FileNotFoundException("Official translation not found: " + trPath);

        report("Reading " + Path.GetFileName(trPath) + "…");
        var tr = ModFile.Parse(File.ReadAllBytes(trPath));

        var english = new Dictionary<string, ModItem>(StringComparer.Ordinal);
        foreach (var dep in tr.Dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string f = Path.Combine(kenshiDir, "data", dep);
            if (!File.Exists(f)) continue;
            report("Reading " + dep + "…");
            var m = ModFile.Parse(File.ReadAllBytes(f));
            foreach (var it in m.Items)
                if (it.Editable && it.StringId.Length > 0) english.TryAdd(it.StringId, it);
        }

        report("Pairing English / translation…");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var it in tr.Items)
        {
            if (!it.Editable) continue;
            english.TryGetValue(it.StringId, out var en);

            if (en != null && it.Name.Trim().Length > 0 && en.Name.Trim().Length > 0 && it.Name != en.Name)
                result.TryAdd(en.Name, it.Name);

            foreach (var kv in it.SDict)
            {
                string k = kv[0], v = kv[1];
                if (v.Trim().Length == 0) continue;
                if (k.StartsWith("_original_", StringComparison.Ordinal))
                {
                    string target = k.Substring("_original_".Length);
                    string t = null;
                    foreach (var kv2 in it.SDict)
                        if (kv2[0] == target) { t = kv2[1]; break; }
                    if (!string.IsNullOrWhiteSpace(t) && t != v) result.TryAdd(v, t);
                }
                else if (en != null && !k.StartsWith("_", StringComparison.Ordinal))
                {
                    string ev = null;
                    foreach (var kv2 in en.SDict)
                        if (kv2[0] == k) { ev = kv2[1]; break; }
                    if (!string.IsNullOrWhiteSpace(ev) && ev != v) result.TryAdd(ev, v);
                }
            }
        }

        if (result.Count == 0) throw new InvalidDataException("No text found in " + trPath);

        official = result;
        OfficialLocale = locale;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var old in Directory.GetFiles(Folder, "official-*.tsv")) File.Delete(old);
            var sb = new StringBuilder("original\ttranslation\n");
            foreach (var kv in result)
                sb.Append(Tools.Esc(kv.Key)).Append('\t').Append(Tools.Esc(kv.Value)).Append('\n');
            File.WriteAllText(OfficialPath(locale), sb.ToString(), new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result.Count;
    }

    public static int Import(string path)
    {
        int n = ReadInto(File.ReadAllText(path, Encoding.UTF8), map);
        if (n > 0) Save();
        return n;
    }

    public static bool Export(string path)
    {
        if (!File.Exists(FilePath)) Save();
        try
        {
            File.Copy(FilePath, path, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
