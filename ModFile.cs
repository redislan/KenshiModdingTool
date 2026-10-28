using System.Text;
using System.Text.RegularExpressions;

namespace KenshiModdingTool;

// Reading / writing the Kenshi binary .mod format (versions 16 and 17),
// based on the open-source OpenConstructionSet library (lmaydev, MIT).

public class ModFormatException : Exception
{
    public ModFormatException(string message) : base(message) { }
}

internal sealed class BinReader
{
    private readonly byte[] d;
    public int P;

    public BinReader(byte[] data, int pos = 0) { d = data; P = pos; }

    private void Need(int n)
    {
        if (n < 0 || (long)P + n > d.Length) throw new ModFormatException("unexpected end of file");
    }

    public void Skip(int n) { Need(n); P += n; }

    public int I32() { Need(4); int v = BitConverter.ToInt32(d, P); P += 4; return v; }

    public uint U32() { Need(4); uint v = BitConverter.ToUInt32(d, P); P += 4; return v; }

    public byte[] StringBytes()
    {
        int n = I32();
        Need(n);
        var b = new byte[n];
        Array.Copy(d, P, b, 0, n);
        P += n;
        return b;
    }

    public string Str() => Encoding.UTF8.GetString(StringBytes());
}

public sealed class ModItem
{
    public byte[] Raw;
    public bool Editable;
    public int Type;
    public int Id;
    public string Name = "";
    public string StringId = "";
    public List<string[]> SDict = new();      // { key, value }: dictionary of string values
    public int NameStart, NameEnd;            // positions relative to the start of the item
    public int SDictStart, SDictEnd;
    public string NewName;
    public Dictionary<int, string> NewValues = new();

    public ModItem(byte[] raw) { Raw = raw; }

    public bool Modified => NewName != null || NewValues.Count > 0;

    public byte[] Serialize()
    {
        if (!Editable || !Modified) return Raw;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            w.Write(Raw, 4, NameStart - 4);
            WriteStr(w, NewName ?? Name);
            w.Write(Raw, NameEnd, SDictStart - NameEnd);
            w.Write(SDict.Count);
            for (int i = 0; i < SDict.Count; i++)
            {
                WriteStr(w, SDict[i][0]);
                WriteStr(w, NewValues.TryGetValue(i, out var nv) ? nv : SDict[i][1]);
            }
            w.Write(Raw, SDictEnd, Raw.Length - SDictEnd);
        }

        byte[] body = ms.ToArray();
        var result = new byte[body.Length + 4];
        BitConverter.GetBytes(body.Length + 4).CopyTo(result, 0);
        Array.Copy(body, 0, result, 4, body.Length);
        return result;
    }

    private static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(b.Length);
        w.Write(b);
    }
}

public sealed class ModFile
{
    public int FileType;
    public int Version;
    public string Author = "";
    public string Description = "";
    public string Dependencies = "";
    public string References = "";
    public byte[] Prefix = Array.Empty<byte>();     // header + lastId + item count
    public List<ModItem> Items = new();
    public byte[] Trailing = Array.Empty<byte>();
    public int RawItems;                            // items not parsed, kept as-is

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        ms.Write(Prefix, 0, Prefix.Length);
        foreach (var it in Items)
        {
            var b = it.Serialize();
            ms.Write(b, 0, b.Length);
        }
        ms.Write(Trailing, 0, Trailing.Length);
        return ms.ToArray();
    }

    public static ModFile Parse(byte[] data)
    {
        var r = new BinReader(data);
        var mod = new ModFile();
        mod.FileType = r.I32();
        if (mod.FileType != 16 && mod.FileType != 17)
            throw new ModFormatException($"this file is not a recognized .mod (type {mod.FileType}, expected 16 or 17)");

        int headerEnd = 0;
        if (mod.FileType == 17)
        {
            int len = r.I32();
            headerEnd = len + r.P;
        }
        mod.Version = r.I32();
        mod.Author = r.Str();
        mod.Description = r.Str();
        mod.Dependencies = r.Str();
        mod.References = r.Str();
        if (headerEnd > 0)
        {
            if (headerEnd > data.Length) throw new ModFormatException("invalid header");
            r.P = headerEnd;
        }
        r.I32(); // lastId
        int count = r.I32();
        if (count < 0) throw new ModFormatException("invalid item count");

        mod.Prefix = Slice(data, 0, r.P);
        int pos = r.P;
        for (int i = 0; i < count; i++)
        {
            var item = ParseItem(data, pos, out int end);
            if (!item.Editable) mod.RawItems++;
            mod.Items.Add(item);
            pos = end;
        }
        mod.Trailing = Slice(data, pos, data.Length - pos);
        return mod;
    }

    private static byte[] Slice(byte[] data, int start, int length)
    {
        var b = new byte[length];
        Array.Copy(data, start, b, 0, length);
        return b;
    }

    // The game's official files write 0 instead of the item length:
    // in that case, the end of the item is deduced by parsing its content.
    private static ModItem ParseItem(byte[] data, int start, out int end)
    {
        var r = new BinReader(data, start);
        int length = r.I32();
        if (length != 0 && (length < 8 || (long)start + length > data.Length))
            throw new ModFormatException("invalid item length");
        int declaredEnd = length != 0 ? start + length : -1;

        var item = new ModItem(null);
        bool ok = false;
        int parsedEnd = -1;
        int ns = 0, ne = 0, ss = 0, se = 0;
        List<string[]> sdict = null;
        try
        {
            item.Type = r.I32();
            item.Id = r.I32();
            ns = r.P;
            item.Name = r.Str();
            ne = r.P;
            item.StringId = r.Str();
            r.U32(); // save data

            // bool, float, int, vector3, vector4
            foreach (int size in new[] { 1, 4, 4, 12, 16 })
            {
                int c = r.I32();
                if (c < 0) throw new ModFormatException("invalid dictionary");
                for (int k = 0; k < c; k++) { r.StringBytes(); r.Skip(size); }
            }

            // string dictionary: this is where the texts are
            ss = r.P;
            int sc = r.I32();
            if (sc < 0) throw new ModFormatException("invalid dictionary");
            sdict = new List<string[]>();
            for (int k = 0; k < sc; k++)
            {
                string key = r.Str();
                string val = r.Str();
                sdict.Add(new[] { key, val });
            }
            se = r.P;

            // file dictionary
            int fc = r.I32();
            if (fc < 0) throw new ModFormatException("invalid dictionary");
            for (int k = 0; k < fc; k++) { r.StringBytes(); r.StringBytes(); }

            // reference categories
            int cc = r.I32();
            if (cc < 0) throw new ModFormatException("invalid categories");
            for (int k = 0; k < cc; k++)
            {
                r.StringBytes();
                int nref = r.I32();
                if (nref < 0) throw new ModFormatException("invalid references");
                for (int m = 0; m < nref; m++) { r.StringBytes(); r.Skip(12); }
            }

            // instances
            int ic = r.I32();
            if (ic < 0) throw new ModFormatException("invalid instances");
            for (int k = 0; k < ic; k++)
            {
                r.StringBytes();
                r.StringBytes();
                r.Skip(12 + 16);
                int nst = r.I32();
                if (nst < 0) throw new ModFormatException("invalid states");
                for (int m = 0; m < nst; m++) r.StringBytes();
            }

            parsedEnd = r.P;
            ok = declaredEnd < 0 || parsedEnd == declaredEnd;
        }
        catch (ModFormatException)
        {
            ok = false;
            parsedEnd = -1;
        }

        if (declaredEnd >= 0) end = declaredEnd;
        else if (parsedEnd >= 0) end = parsedEnd;
        else throw new ModFormatException("unreadable item with no length: cannot continue");

        item.Raw = Slice(data, start, end - start);
        if (ok)
        {
            item.NameStart = ns - start;
            item.NameEnd = ne - start;
            item.SDictStart = ss - start;
            item.SDictEnd = se - start;
            item.SDict = sdict;
            item.Editable = true;
        }
        return item;
    }
}

public sealed class Entry
{
    public ModItem Item;
    public string Field;      // "name" or key of the string dictionary
    public int VIdx;          // -1 = item name, otherwise index in SDict
    public string Original;
    public string Translation = "";
    public bool Likely;       // looks like real text
    public int Idx;
    public string Key;

    public string Alert => Tools.TokenAlert(Original, Translation);
    public bool Done => Translation.Length > 0 && Translation != Original;
}

public static class Tools
{
    private static readonly string[] PathExt =
    {
        ".mesh", ".mod", ".png", ".dds", ".tga", ".xml", ".material", ".skeleton", ".wav", ".ogg",
        ".ttf", ".lua", ".anim", ".cfg", ".nav", ".model", ".base", ".jpg", ".bmp", ".wem", ".save",
        ".txt", ".tif", ".fbx", ".hkx"
    };

    private static readonly HashSet<string> TextKeys = new() { "description", "text", "tooltip", "title", "desc", "name" };
    private static readonly Regex TokenRe = new(@"\[[^\]]*\]|<[^>]*>|\{[^}]*\}|%[a-zA-Z0-9]+|\$\w+|\\n", RegexOptions.Compiled);
    private static readonly Regex CodeLike = new(@"^[A-Z0-9_\-.]+$", RegexOptions.Compiled);

    public static bool LooksLikeText(string value, string field)
    {
        string s = value.Trim();
        if (s.Length == 0 || !s.Any(char.IsLetter)) return false;
        if (s.Contains('/') || s.Contains('\\')) return false;
        foreach (var ext in PathExt)
            if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return false;
        if (CodeLike.IsMatch(s) && s.Contains('_')) return false;
        if (!s.Contains(' ') && field != "name" && !TextKeys.Contains(field.ToLowerInvariant())) return false;
        return true;
    }

    public static string TokenAlert(string original, string translation)
    {
        if (string.IsNullOrEmpty(translation)) return "";
        var a = TokenRe.Matches(original).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var b = TokenRe.Matches(translation).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return a.SequenceEqual(b) ? "" : "codes?";
    }

    public static List<Entry> BuildEntries(ModFile mod)
    {
        var list = new List<Entry>();
        foreach (var it in mod.Items)
        {
            if (!it.Editable) continue;
            string baseKey = !string.IsNullOrEmpty(it.StringId) ? it.StringId : "id" + it.Id;
            if (it.Name.Trim().Length > 0)
                list.Add(new Entry
                {
                    Item = it, Field = "name", VIdx = -1, Original = it.Name,
                    Likely = LooksLikeText(it.Name, "name"), Idx = list.Count, Key = baseKey + "|name"
                });
            for (int i = 0; i < it.SDict.Count; i++)
            {
                string k = it.SDict[i][0], v = it.SDict[i][1];
                if (v.Trim().Length == 0) continue;
                list.Add(new Entry
                {
                    Item = it, Field = k, VIdx = i, Original = v,
                    Likely = LooksLikeText(v, k), Idx = list.Count, Key = baseKey + "|" + k
                });
            }
        }
        return list;
    }

    public static int ApplyEntries(List<Entry> entries)
    {
        foreach (var it in entries.Select(e => e.Item).Distinct())
        {
            it.NewName = null;
            it.NewValues = new Dictionary<int, string>();
        }
        int n = 0;
        foreach (var e in entries)
        {
            if (!e.Done) continue;
            if (e.VIdx < 0) e.Item.NewName = e.Translation;
            else e.Item.NewValues[e.VIdx] = e.Translation;
            n++;
        }
        return n;
    }

    public static void WriteMod(ModFile mod, List<Entry> entries, string path, bool backupOriginal)
    {
        ApplyEntries(entries);
        byte[] data = mod.Serialize();
        ModFile.Parse(data); // the produced file must be re-readable before touching the disk
        if (backupOriginal && File.Exists(path) && !File.Exists(path + ".original"))
            File.Copy(path, path + ".original");
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, true);
    }

    // ---- TSV
    public static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    public static string Unesc(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                switch (n)
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(n); break;
                }
                i++;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static void ExportTsv(List<Entry> entries, string path)
    {
        var sb = new StringBuilder("key\toriginal\ttranslation\n");
        foreach (var e in entries)
            if (e.Likely || e.Translation.Length > 0)
                sb.Append(Esc(e.Key)).Append('\t').Append(Esc(e.Original)).Append('\t').Append(Esc(e.Translation)).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static (int done, int missing) ImportTsv(List<Entry> entries, string path)
    {
        var byKey = new Dictionary<string, Entry>();
        foreach (var e in entries) byKey.TryAdd(e.Key, e);
        int done = 0, missing = 0;
        var lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            var cols = line.Split('\t');
            if (cols.Length < 3) continue;
            if (!byKey.TryGetValue(Unesc(cols[0]), out var e)) { missing++; continue; }
            string tr = Unesc(cols[2]);
            if (tr.Length > 0) { e.Translation = tr; done++; }
        }
        return (done, missing);
    }
}
