using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KenshiModdingTool;

public sealed class ModlistRow
{
    public string File;      // "Name.mod" as written in mods.cfg
    public string Name;      // without the extension
    public bool Active;
    public string Path;      // path of the .mod found, otherwise null
    public string Code = "";  // fr, hasfr, replaced, frcontent, todo, none, missing, error
    public string Status = "Analyzing…";
    public int Texts;
}

// "Modlist" tab: the mods in load order (Kenshi\data\mods.cfg), like MO2,
// with the translation status of each. The last mod in the list has priority.
public sealed class ModlistControl : UserControl
{
    private const string Title = "Kenshi Modding Tool";

    public event Action<string> TranslateRequested;
    public bool Dirty => dirty;

    private readonly List<ModlistRow> rows = new();
    private DataGridView grid;
    private FlowLayoutPanel bar;
    private Panel infoPanel;
    private Label lblInfo, lblStatus;
    private Button btnReload, btnUp, btnDown, btnFr, btnSave, btnTranslate;
    private bool dark, dirty, filling;
    private int scanId;

    private static readonly Dictionary<string, (int likely, int fr)> cache = new();
    private static readonly Regex FrName = new(@"(?<![A-Za-z])fr(?![A-Za-z])|fran[cç]ais|french", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FrWords = new(@"[éèêëàâùûçôîï]|\b(le|la|les|des|une|est|pour|dans|du|au|aux|sur|avec|vous|votre)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] FrSuffixes = { " - FR", "_FR", " FR" };

    public ModlistControl()
    {
        Font = new Font("Segoe UI", 9.5f);
        Dock = DockStyle.Fill;
        BuildUi();
        ApplyTheme();
    }

    // ------------------------------------------------------------------ interface

    private void BuildUi()
    {
        btnReload = new Button { Text = "Refresh", AutoSize = true };
        btnUp = new Button { Text = "▲ Move up", AutoSize = true };
        btnDown = new Button { Text = "▼ Move down", AutoSize = true };
        btnFr = new Button { Text = "Place \" - FR\" mods after their original", AutoSize = true };
        btnSave = new Button { Text = "Save order", AutoSize = true };
        btnTranslate = new Button { Text = "Translate selected mod", AutoSize = true };
        btnReload.Click += (s, e) => { if (ConfirmDiscard()) Reload(); };
        btnUp.Click += (s, e) => Move(-1);
        btnDown.Click += (s, e) => Move(1);
        btnFr.Click += (s, e) => PlaceFrAfterOriginal();
        btnSave.Click += (s, e) => SaveOrder();
        btnTranslate.Click += (s, e) => TranslateSelected();
        bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        bar.Controls.AddRange(new Control[] { btnReload, btnUp, btnDown, btnFr, btnSave, btnTranslate });

        lblInfo = new Label { Text = "", AutoSize = true, Left = 10, Top = 4 };
        infoPanel = new Panel { Dock = DockStyle.Top, Height = 28 };
        infoPanel.Controls.Add(lblInfo);

        lblStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(8, 4, 8, 4),
            Text = "Load order: the last mod in the list wins. A \" - FR\" mod must come AFTER its original. " +
                   "Close the Kenshi launcher before saving the order.",
        };

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            BackgroundColor = SystemColors.Window,
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "active", HeaderText = "Active", Width = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "#", Width = 50, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "mod", HeaderText = "Mod", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 100, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Translation", Width = 280, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "texts", HeaderText = "Texts", Width = 70, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "where", HeaderText = "Location", Width = 110, ReadOnly = true });
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (s, e) =>
        {
            if (filling || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            if (grid.Rows[e.RowIndex].Tag is ModlistRow r)
            {
                r.Active = grid.Rows[e.RowIndex].Cells[0].Value is true;
                dirty = true;
                UpdateInfo();
            }
        };
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 0) TranslateSelected(); };

        // the order of addition determines the stacking of docked controls
        Controls.Add(grid);
        Controls.Add(lblStatus);
        Controls.Add(infoPanel);
        Controls.Add(bar);
    }

    // ------------------------------------------------------------------ theme

    public void SetDark(bool d)
    {
        if (d == dark) return;
        dark = d;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        Color bg = dark ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
        Color edit = dark ? Color.FromArgb(43, 43, 48) : SystemColors.Window;
        Color fg = dark ? Color.FromArgb(230, 230, 235) : SystemColors.ControlText;
        BackColor = bg;
        ForeColor = fg;
        foreach (Control c in new Control[] { bar, infoPanel, lblInfo, lblStatus })
        {
            c.BackColor = bg;
            c.ForeColor = fg;
        }
        foreach (var b in new[] { btnReload, btnUp, btnDown, btnFr, btnSave, btnTranslate })
        {
            b.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
            b.BackColor = dark ? Color.FromArgb(58, 58, 64) : SystemColors.Control;
            b.ForeColor = fg;
        }
        grid.BackgroundColor = edit;
        grid.GridColor = dark ? Color.FromArgb(62, 62, 68) : Color.FromArgb(220, 220, 220);
        grid.DefaultCellStyle.BackColor = edit;
        grid.DefaultCellStyle.ForeColor = fg;
        grid.DefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(70, 98, 160) : Color.FromArgb(0, 120, 215);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(52, 52, 58) : Color.FromArgb(240, 240, 240);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = fg;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        RecolorRows();
    }

    private Color RowColor(string code)
    {
        switch (code)
        {
            case "fr": case "hasfr": case "replaced": case "frcontent":
                return dark ? Color.FromArgb(34, 66, 44) : Color.FromArgb(226, 244, 230);
            case "todo":
                return dark ? Color.FromArgb(94, 72, 28) : Color.FromArgb(255, 241, 214);
            case "missing": case "error":
                return dark ? Color.FromArgb(96, 40, 40) : Color.FromArgb(250, 224, 224);
            default:
                return dark ? Color.FromArgb(43, 43, 48) : SystemColors.Window;
        }
    }

    private void RecolorRows()
    {
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is ModlistRow m)
                row.DefaultCellStyle.BackColor = RowColor(m.Code);
    }

    // ------------------------------------------------------------------ loading

    private static bool IsKenshi(string p) => !string.IsNullOrWhiteSpace(p) && Directory.Exists(Path.Combine(p.Trim(), "data"));

    private static string CfgPath(string kenshi) => Path.Combine(kenshi.Trim(), "data", "mods.cfg");

    private static string StripMod(string file) =>
        file.EndsWith(".mod", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file;

    public void Reload()
    {
        string kenshi = WorkshopControl.LoadSetting();
        rows.Clear();
        dirty = false;
        scanId++;
        if (!IsKenshi(kenshi))
        {
            lblInfo.Text = "First set the Kenshi folder in the \"Workshop → mods\" tab.";
            Fill();
            return;
        }

        string modsDir = Path.Combine(kenshi.Trim(), "mods");
        var workshopDirs = new List<string>();
        foreach (var lib in WorkshopControl.SteamLibraries())
            workshopDirs.Add(Path.Combine(lib, "steamapps", "workshop", "content", "233860"));
        try
        {
            string steamapps = Path.GetDirectoryName(Path.GetDirectoryName(kenshi.Trim().TrimEnd('\\', '/')));
            if (!string.IsNullOrEmpty(steamapps)) workshopDirs.Add(Path.Combine(steamapps, "workshop", "content", "233860"));
        }
        catch { }
        workshopDirs = workshopDirs.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string cfg = CfgPath(kenshi);
            if (File.Exists(cfg))
                foreach (var raw in File.ReadAllLines(cfg))
                {
                    string file = raw.Trim();
                    if (file.Length == 0 || !seen.Add(file)) continue;
                    rows.Add(new ModlistRow { File = file, Name = StripMod(file), Active = true, Path = FindMod(modsDir, workshopDirs, file) });
                }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not read mods.cfg:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // mods present in mods\ but not in the active list
        try
        {
            if (Directory.Exists(modsDir))
                foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    string file = name + ".mod";
                    string p = System.IO.Path.Combine(dir, file);
                    if (File.Exists(p) && seen.Add(file))
                        rows.Add(new ModlistRow { File = file, Name = name, Active = false, Path = p });
                }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }

        Fill();
        StartScan();
    }

    private static string FindMod(string modsDir, List<string> workshopDirs, string file)
    {
        string name = StripMod(file);
        string p = System.IO.Path.Combine(modsDir, name, file);
        if (File.Exists(p)) return p;
        foreach (var w in workshopDirs)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(w))
                {
                    string q = System.IO.Path.Combine(dir, file);
                    if (File.Exists(q)) return q;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
        return null;
    }

    private void Fill()
    {
        int sel = grid.CurrentRow?.Index ?? -1;
        filling = true;
        grid.Rows.Clear();
        string modsDir = Path.Combine(WorkshopControl.LoadSetting().Trim(), "mods") + "\\";
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string where = r.Path == null ? "not found" : r.Path.StartsWith(modsDir, StringComparison.OrdinalIgnoreCase) ? "mods\\" : "Workshop";
            int idx = grid.Rows.Add(r.Active, r.Active ? (i + 1).ToString() : "", r.Name, r.Status, r.Texts > 0 ? r.Texts.ToString() : "", where);
            grid.Rows[idx].Tag = r;
        }
        filling = false;
        RecolorRows();
        if (sel >= 0 && sel < grid.Rows.Count) grid.CurrentCell = grid.Rows[sel].Cells[2];
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (rows.Count == 0 && string.IsNullOrEmpty(lblInfo.Text)) return;
        if (rows.Count == 0) return;
        int active = rows.Count(r => r.Active);
        int done = rows.Count(r => r.Code is "fr" or "hasfr" or "replaced" or "frcontent");
        int todo = rows.Count(r => r.Code == "todo");
        int none = rows.Count(r => r.Code == "none");
        lblInfo.Text = $"{rows.Count} mods ({active} active): {done} in French, {todo} to translate, {none} with no text to translate." +
                       (dirty ? "   ● order changed, not saved yet" : "");
        // renumber the active ones
        int n = 1;
        filling = true;
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is ModlistRow r) row.Cells[1].Value = r.Active ? (n++).ToString() : "";
        filling = false;
    }

    // ------------------------------------------------------------------ translation analysis

    private async void StartScan()
    {
        int id = ++scanId;
        var snapshot = rows.ToList();
        var names = new HashSet<string>(rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        lblStatus.Text = "Analyzing mods…";
        await Task.Run(() =>
        {
            foreach (var r in snapshot)
            {
                if (id != scanId) return;
                Analyze(r, names);
            }
        });
        if (id != scanId) return;
        Fill();
        lblStatus.Text = "Load order: the last mod in the list wins. A \" - FR\" mod must come AFTER its original. " +
                         "Close the Kenshi launcher before saving the order.";
    }

    private static void Analyze(ModlistRow r, HashSet<string> names)
    {
        if (r.Path == null) { r.Code = "missing"; r.Status = "Not found (not copied from the Workshop?)"; return; }
        if (FrName.IsMatch(r.Name)) { r.Code = "fr"; r.Status = "French version"; return; }
        foreach (var suf in FrSuffixes)
            if (names.Contains(r.Name + suf)) { r.Code = "hasfr"; r.Status = "Translated: \"" + r.Name + suf + "\""; return; }
        if (File.Exists(r.Path + ".original")) { r.Code = "replaced"; r.Status = "Translated (original replaced)"; return; }

        try
        {
            var fi = new FileInfo(r.Path);
            string key = r.Path + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Length;
            (int likely, int fr) v;
            bool found;
            lock (cache) found = cache.TryGetValue(key, out v);
            if (!found)
            {
                var entries = Tools.BuildEntries(ModFile.Parse(File.ReadAllBytes(r.Path))).Where(e => e.Likely).ToList();
                v = (entries.Count, entries.Count(e => FrWords.IsMatch(e.Original)));
                lock (cache) cache[key] = v;
            }
            r.Texts = v.likely;
            if (v.likely == 0) { r.Code = "none"; r.Status = "Nothing to translate"; }
            else if (v.fr * 2 >= v.likely) { r.Code = "frcontent"; r.Status = "Already in French (probably)"; }
            else { r.Code = "todo"; r.Status = "To translate"; }
        }
        catch (Exception ex) when (ex is ModFormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            r.Code = "error";
            r.Status = "Unreadable";
        }
    }

    // ------------------------------------------------------------------ load order

    private void Move(int delta)
    {
        int i = grid.CurrentRow?.Index ?? -1;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= rows.Count) return;
        (rows[i], rows[j]) = (rows[j], rows[i]);
        dirty = true;
        Fill();
        grid.CurrentCell = grid.Rows[j].Cells[2];
    }

    private static string BaseOfFr(string name)
    {
        foreach (var suf in FrSuffixes)
            if (name.EndsWith(suf, StringComparison.OrdinalIgnoreCase) && name.Length > suf.Length)
                return name.Substring(0, name.Length - suf.Length);
        return null;
    }

    private void PlaceFrAfterOriginal()
    {
        int moved = 0;
        foreach (var r in rows.ToList())
        {
            string baseName = BaseOfFr(r.Name);
            if (baseName == null) continue;
            int bi = rows.FindIndex(x => x.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
            if (bi < 0 || !rows[bi].Active || !r.Active) continue;
            int ri = rows.IndexOf(r);
            if (ri == bi + 1) continue;
            rows.RemoveAt(ri);
            bi = rows.FindIndex(x => x.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
            rows.Insert(bi + 1, r);
            moved++;
        }
        if (moved > 0) dirty = true;
        Fill();
        lblStatus.Text = moved > 0
            ? $"{moved} \" - FR\" mod(s) placed right after their original. Remember to save the order."
            : "All \" - FR\" mods are already right after their original.";
    }

    private void SaveOrder()
    {
        string kenshi = WorkshopControl.LoadSetting();
        if (!IsKenshi(kenshi)) return;
        string cfg = CfgPath(kenshi);
        try
        {
            if (File.Exists(cfg)) File.Copy(cfg, cfg + ".bak", true);
            File.WriteAllLines(cfg, rows.Where(r => r.Active).Select(r => r.File), new UTF8Encoding(false));
            dirty = false;
            UpdateInfo();
            lblStatus.Text = "Order saved to mods.cfg (backup: mods.cfg.bak).";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not write mods.cfg:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Adds a mod to the active list, right after another one. Returns true if mods.cfg was modified.
    public static bool AddToLoadOrder(string afterFile, string newFile)
    {
        string kenshi = WorkshopControl.LoadSetting();
        if (!IsKenshi(kenshi)) return false;
        string cfg = CfgPath(kenshi);
        var lines = File.Exists(cfg)
            ? File.ReadAllLines(cfg).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string>();
        if (lines.Any(l => l.Equals(newFile, StringComparison.OrdinalIgnoreCase))) return false;
        int i = lines.FindIndex(l => l.Equals(afterFile, StringComparison.OrdinalIgnoreCase));
        lines.Insert(i >= 0 ? i + 1 : lines.Count, newFile);
        if (File.Exists(cfg)) File.Copy(cfg, cfg + ".bak", true);
        File.WriteAllLines(cfg, lines, new UTF8Encoding(false));
        return true;
    }

    private void TranslateSelected()
    {
        if (grid.CurrentRow?.Tag is not ModlistRow r)
        {
            MessageBox.Show(this, "Select a mod in the list.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (r.Path == null)
        {
            MessageBox.Show(this, "This mod could not be found on disk.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        TranslateRequested?.Invoke(r.Path);
    }

    public bool ConfirmDiscard()
    {
        if (!dirty) return true;
        return MessageBox.Show(this, "The load order is not saved. Continue without saving?", Title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }
}
