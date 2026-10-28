using System.Drawing;
using System.Windows.Forms;

namespace KenshiModdingTool;

public sealed class TranslatorControl : UserControl
{
    private const string Title = "Kenshi Modding Tool";
    public event Action<string> TitleChanged;
    public event Action<bool> ThemeChanged;
    public event Action<string, string> InstalledFr;   // (original.mod, original - FR.mod)
    public bool Dirty => dirty;

    private string path;
    private ModFile mod;
    private List<Entry> entries = new();
    private List<Entry> view = new();
    private Entry cur;
    private int curRow = -1;
    private bool dirty;
    private bool loading;
    private bool dark;

    private DataGridView grid;
    private TextBox txtOrig, txtTr, txtSearch;
    private CheckBox chkUntr, chkTech;
    private ToolStripStatusLabel lblStatus;
    private ToolStripProgressBar bar;
    private ToolStrip tool;
    private ToolStripDropDownButton ddDict;
    private ToolStripMenuItem miLearn, miApply;
    private FlowLayoutPanel filterPanel;
    private StatusStrip statusStrip;
    private SplitContainer split;
    private TableLayoutPanel editPanel;
    private Label lblO, lblT;
    private FlowLayoutPanel btnPanel;
    private ToolStripButton btnTheme;
    private readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 250 };

    public TranslatorControl()
    {
        Font = new Font("Segoe UI", 9.5f);
        Dock = DockStyle.Fill;
        BuildUi();
        ApplyTheme();
        searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshView(); };
        HandleCreated += (s, e) => BeginInvoke(new Action(() =>
        {
            try { split.SplitterDistance = (int)(split.Height * 0.62); } catch { }
        }));
    }

    public void SetDark(bool d)
    {
        if (d == dark) return;
        dark = d;
        ApplyTheme();
    }

    // ------------------------------------------------------------------ interface

    private void BuildUi()
    {
        // grid
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            BackgroundColor = SystemColors.Window,
        };
        grid.Columns.Add(MakeCol("n", "#", 65, false));
        grid.Columns.Add(MakeCol("type", "Type", 55, false));
        grid.Columns.Add(MakeCol("field", "Field", 140, false));
        grid.Columns.Add(MakeCol("orig", "Original", 400, true));
        grid.Columns.Add(MakeCol("tr", "Translation", 400, true));
        grid.Columns.Add(MakeCol("alert", "Alert", 70, false));
        grid.CellValueNeeded += Grid_CellValueNeeded;
        grid.CellFormatting += Grid_CellFormatting;
        grid.CurrentCellChanged += (s, e) => OnRowChanged();

        // editors
        txtOrig = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f) };
        txtTr = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f) };
        txtTr.TextChanged += TxtTr_TextChanged;
        lblO = new Label { Text = "Original (read-only)", AutoSize = true, Margin = new Padding(3, 6, 3, 2) };
        lblT = new Label { Text = "Translation", AutoSize = true, Margin = new Padding(3, 6, 3, 2) };

        btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        btnPanel.Controls.Add(MakeButton("Copy original (Ctrl+D)", (s, e) => CopyOriginal()));
        btnPanel.Controls.Add(MakeButton("Clear translation", (s, e) => { if (cur != null) txtTr.Clear(); }));
        btnPanel.Controls.Add(MakeButton("Next untranslated (Alt+↓)", (s, e) => NextUntranslated()));

        editPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6, 0, 6, 4) };
        editPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editPanel.Controls.Add(lblO, 0, 0);
        editPanel.Controls.Add(lblT, 1, 0);
        editPanel.Controls.Add(txtOrig, 0, 1);
        editPanel.Controls.Add(txtTr, 1, 1);
        editPanel.Controls.Add(btnPanel, 0, 2);
        editPanel.SetColumnSpan(btnPanel, 2);
        txtOrig.Margin = new Padding(3, 3, 6, 3);
        txtTr.Margin = new Padding(6, 3, 3, 3);

        split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(editPanel);

        // filters
        txtSearch = new TextBox { Width = 280 };
        txtSearch.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
        chkUntr = new CheckBox { Text = "Untranslated only", AutoSize = true };
        chkUntr.CheckedChanged += (s, e) => RefreshView();
        chkTech = new CheckBox { Text = "Also show technical fields", AutoSize = true };
        chkTech.CheckedChanged += (s, e) => RefreshView();
        filterPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        filterPanel.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        filterPanel.Controls.Add(txtSearch);
        filterPanel.Controls.Add(chkUntr);
        filterPanel.Controls.Add(chkTech);
        chkUntr.Margin = new Padding(18, 5, 3, 0);
        chkTech.Margin = new Padding(12, 5, 3, 0);

        // toolbar
        tool = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        tool.Items.Add(MakeTool("Open .mod", (s, e) => OpenDialog()));
        tool.Items.Add(MakeTool("Save as…", (s, e) => SaveAs()));
        tool.Items.Add(MakeTool("Install as \"Name - FR\"", (s, e) => InstallFr()));
        tool.Items.Add(MakeTool("Replace original (+ .original)", (s, e) => Install()));
        tool.Items.Add(new ToolStripSeparator());
        tool.Items.Add(MakeTool("Export TSV", (s, e) => DoExport()));
        tool.Items.Add(MakeTool("Import TSV", (s, e) => DoImport()));
        tool.Items.Add(MakeTool("Fill duplicates", (s, e) => FillDuplicates()));
        tool.Items.Add(BuildDictionaryMenu());
        tool.Items.Add(new ToolStripSeparator());
        tool.Items.Add(MakeTool("Mod info", (s, e) => ShowInfo()));
        btnTheme = MakeTool("Dark theme", (s, e) => { dark = !dark; ApplyTheme(); ThemeChanged?.Invoke(dark); });
        tool.Items.Add(btnTheme);

        // status bar
        lblStatus = new ToolStripStatusLabel { Text = "Open a .mod file (or drag it into the window).", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        bar = new ToolStripProgressBar { Width = 220, Maximum = 1, Value = 0 };
        statusStrip = new StatusStrip();
        statusStrip.Items.Add(lblStatus);
        statusStrip.Items.Add(bar);

        // the order of addition determines the stacking of docked controls
        Controls.Add(split);
        Controls.Add(filterPanel);
        Controls.Add(tool);
        Controls.Add(statusStrip);
    }

    private static DataGridViewTextBoxColumn MakeCol(string name, string header, int width, bool fill)
    {
        var c = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        };
        if (fill) c.FillWeight = 100;
        return c;
    }

    private static Button MakeButton(string text, EventHandler h)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        b.Click += h;
        return b;
    }

    private static ToolStripButton MakeTool(string text, EventHandler h)
    {
        var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        b.Click += h;
        return b;
    }

    // ------------------------------------------------------------------ theme

    private Color BgMain => dark ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
    private Color BgEdit => dark ? Color.FromArgb(43, 43, 48) : SystemColors.Window;
    private Color FgMain => dark ? Color.FromArgb(230, 230, 235) : SystemColors.ControlText;
    private Color RowDone => dark ? Color.FromArgb(34, 66, 44) : Color.FromArgb(226, 244, 230);
    private Color RowWarn => dark ? Color.FromArgb(94, 72, 28) : Color.FromArgb(255, 241, 214);

    private void ApplyTheme()
    {
        BackColor = BgMain;
        ForeColor = FgMain;
        foreach (Control c in new Control[] { filterPanel, editPanel, btnPanel, lblO, lblT, chkUntr, chkTech })
        {
            c.BackColor = BgMain;
            c.ForeColor = FgMain;
        }
        split.BackColor = BgMain;
        split.Panel1.BackColor = BgMain;
        split.Panel2.BackColor = BgMain;
        foreach (var t in new[] { txtOrig, txtTr, txtSearch })
        {
            t.BackColor = t == txtOrig ? (dark ? Color.FromArgb(38, 38, 42) : Color.FromArgb(244, 244, 244)) : BgEdit;
            t.ForeColor = FgMain;
        }
        foreach (Control c in btnPanel.Controls)
        {
            if (c is Button b)
            {
                b.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                b.BackColor = dark ? Color.FromArgb(58, 58, 64) : SystemColors.Control;
                b.ForeColor = FgMain;
            }
        }
        tool.RenderMode = ToolStripRenderMode.System;
        tool.BackColor = dark ? Color.FromArgb(40, 40, 44) : SystemColors.Control;
        tool.ForeColor = FgMain;
        foreach (ToolStripItem it in tool.Items) it.ForeColor = FgMain;
        ddDict.DropDown.BackColor = SystemColors.Menu;
        foreach (ToolStripItem it in ddDict.DropDownItems) it.ForeColor = SystemColors.MenuText;
        statusStrip.BackColor = dark ? Color.FromArgb(40, 40, 44) : SystemColors.Control;
        statusStrip.ForeColor = FgMain;
        lblStatus.ForeColor = FgMain;

        grid.BackgroundColor = BgEdit;
        grid.GridColor = dark ? Color.FromArgb(62, 62, 68) : Color.FromArgb(220, 220, 220);
        grid.DefaultCellStyle.BackColor = BgEdit;
        grid.DefaultCellStyle.ForeColor = FgMain;
        grid.DefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(70, 98, 160) : Color.FromArgb(0, 120, 215);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(52, 52, 58) : Color.FromArgb(240, 240, 240);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = FgMain;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        btnTheme.Text = dark ? "Light theme" : "Dark theme";
        grid.Invalidate();
    }

    // ------------------------------------------------------------------ files

    private static string DefaultDir()
    {
        foreach (var p in new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Kenshi\mods",
            @"C:\Program Files\Steam\steamapps\common\Kenshi\mods",
        })
            if (Directory.Exists(p)) return p;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Title = "Open a Kenshi mod",
            InitialDirectory = DefaultDir(),
            Filter = "Kenshi mod (*.mod)|*.mod|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.FileName);
    }

    public void OpenPath(string p)
    {
        ModFile m;
        try
        {
            m = ModFile.Parse(File.ReadAllBytes(p));
        }
        catch (Exception ex) when (ex is ModFormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not read this file:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        path = p;
        mod = m;
        entries = Tools.BuildEntries(m);
        cur = null;
        curRow = -1;
        dirty = false;
        SetEditors("", "");
        TitleChanged?.Invoke(Path.GetFileName(p));
        RefreshView();
        string msg = $"{entries.Count} texts found in {m.Items.Count} items.";
        if (m.RawItems > 0) msg += $" {m.RawItems} items not parsed (kept as-is).";
        if (GameDictionary.AutoApply)
        {
            int filled = GameDictionary.Apply(entries);
            if (filled > 0)
            {
                dirty = true;
                RefreshView();
                msg += $" {filled} texts filled in by the dictionary.";
            }
        }
        lblStatus.Text = msg;
        if (GameDictionary.OfficialCount == 0 && !GameDictionary.OfficialAsked)
        {
            GameDictionary.OfficialAsked = true;
            GameDictionary.SaveOptions();
            BeginInvoke(new Action(async () => await BuildOfficialAsync(false)));
        }
    }

    private bool NeedMod()
    {
        if (mod != null) return true;
        MessageBox.Show(this, "Open a .mod file first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void SaveAs()
    {
        if (!NeedMod()) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Save the translated mod",
            DefaultExt = "mod",
            InitialDirectory = Path.GetDirectoryName(path),
            FileName = Path.GetFileName(path),
            Filter = "Kenshi mod (*.mod)|*.mod",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool same = string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        Write(dlg.FileName, same);
    }

    private void Install()
    {
        if (!NeedMod()) return;
        var r = MessageBox.Show(this,
            "Replace the original file with the translated version?\n\n" + path +
            "\n\nThe original will be copied as .mod.original (if that copy does not already exist).\n" +
            "Warning: for a Workshop mod, Steam may update it and overwrite your translation. " +
            "Better to copy the mod into Kenshi\\mods\\ and open it from there.",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r == DialogResult.Yes) Write(path, true);
    }

    // Creates a separate "<name> - FR" mod next to the original, which stays untouched.
    private void InstallFr()
    {
        if (!NeedMod()) return;
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(" - FR", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "This mod is already a \" - FR\" version. Use \"Save as…\".", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string srcDir = Path.GetDirectoryName(path);
        string newName = name + " - FR";
        bool ownFolder = string.Equals(Path.GetFileName(srcDir), name, StringComparison.OrdinalIgnoreCase);
        string destDir = ownFolder ? Path.Combine(Path.GetDirectoryName(srcDir), newName) : srcDir;
        string destMod = Path.Combine(destDir, newName + ".mod");
        if (File.Exists(destMod) &&
            MessageBox.Show(this, "\"" + newName + "\" already exists. Replace it?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            if (ownFolder) CopyModFolder(srcDir, destDir, path);
            Write(destMod, false);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Installation failed:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        lblStatus.Text = $"Installed: {destMod}";
        var ask = MessageBox.Show(this,
            $"✔ \"{newName}\" has been installed:\n{destMod}\n\n" +
            $"Add it to the active mod list now, right after \"{name}\"?\n" +
            "(Close the Kenshi launcher first. You can also do this in the Modlist tab.)",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (ask == DialogResult.Yes) InstalledFr?.Invoke(name + ".mod", newName + ".mod");
    }

    // Copies the mod's files (textures, models…) without the original .mod or the backups.
    private static void CopyModFolder(string src, string dest, string skipMod)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
        {
            if (string.Equals(f, skipMod, StringComparison.OrdinalIgnoreCase)) continue;
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".original" || ext == ".tmp" || f.EndsWith(".local-backup", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        }
        foreach (var sub in Directory.GetDirectories(src))
            CopyModFolder(sub, Path.Combine(dest, Path.GetFileName(sub)), skipMod);
    }

    private void Write(string p, bool backup)
    {
        try
        {
            Tools.WriteMod(mod, entries, p, backup);
        }
        catch (Exception ex) when (ex is ModFormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Save failed:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        dirty = false;
        string saved = $"Saved: {p} ({entries.Count(e => e.Done)} translated texts applied).";
        if (GameDictionary.AutoLearn)
        {
            int learned = GameDictionary.Learn(entries);
            saved += $" Dictionary: +{learned} ({GameDictionary.Count} total).";
        }
        lblStatus.Text = saved;
    }

    private void DoExport()
    {
        if (!NeedMod()) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Export texts (TSV)",
            DefaultExt = "tsv",
            FileName = Path.GetFileNameWithoutExtension(path) + ".tsv",
            Filter = "TSV (*.tsv)|*.tsv",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Tools.ExportTsv(entries, dlg.FileName);
            lblStatus.Text = "Exported: " + dlg.FileName;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoImport()
    {
        if (!NeedMod()) return;
        using var dlg = new OpenFileDialog
        {
            Title = "Import translations (TSV)",
            Filter = "TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (done, missing) = Tools.ImportTsv(entries, dlg.FileName);
            if (done > 0) dirty = true;
            RefreshView();
            lblStatus.Text = $"Import: {done} translations applied, {missing} keys not found.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowInfo()
    {
        if (!NeedMod()) return;
        string deps = string.IsNullOrEmpty(mod.Dependencies) ? "-" : mod.Dependencies;
        MessageBox.Show(this,
            $"File: {path}\nFile type: {mod.FileType}\nVersion: {mod.Version}\nAuthor: {mod.Author}\n" +
            $"Description: {mod.Description}\nDependencies: {deps}\nItems: {mod.Items.Count} ({mod.RawItems} not parsed)\n" +
            $"Texts: {entries.Count} ({entries.Count(e => e.Likely)} probably translatable)",
            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public bool ConfirmDiscard()
    {
        if (!dirty) return true;
        return MessageBox.Show(this, "Some translations are not saved. Continue?", Title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    // ------------------------------------------------------------------ list

    private bool IsVisible(Entry e)
    {
        if (!chkTech.Checked && !(e.Likely || e.Translation.Length > 0)) return false;
        if (chkUntr.Checked && e.Translation.Length > 0) return false;
        string q = txtSearch.Text.Trim();
        if (q.Length > 0)
        {
            var cmp = StringComparison.CurrentCultureIgnoreCase;
            if (e.Original.IndexOf(q, cmp) < 0 && e.Translation.IndexOf(q, cmp) < 0 &&
                e.Field.IndexOf(q, cmp) < 0 && e.Item.Name.IndexOf(q, cmp) < 0)
                return false;
        }
        return true;
    }

    private void RefreshView()
    {
        Entry keep = cur;
        view = entries.Where(IsVisible).ToList();
        loading = true;
        grid.RowCount = 0;
        grid.RowCount = view.Count;
        loading = false;
        curRow = -1;
        if (keep != null)
        {
            int idx = view.IndexOf(keep);
            if (idx >= 0)
            {
                grid.CurrentCell = grid.Rows[idx].Cells[3];
                curRow = idx;
            }
        }
        if (curRow < 0)
        {
            cur = null;
            if (view.Count > 0)
            {
                grid.CurrentCell = grid.Rows[0].Cells[3];
                curRow = 0;
            }
        }
        OnRowChanged(); // loads the selected entry into the editors (even if the cell did not change)
        if (view.Count == 0) { cur = null; curRow = -1; SetEditors("", ""); }
        UpdateStatus();
    }

    private static string Short(string s, int n = 160)
    {
        s = s.Replace("\r", "").Replace("\n", " ⏎ ");
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }

    private void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
        var en = view[e.RowIndex];
        switch (e.ColumnIndex)
        {
            case 0: e.Value = en.Idx + 1; break;
            case 1: e.Value = en.Item.Type; break;
            case 2: e.Value = en.Field; break;
            case 3: e.Value = Short(en.Original); break;
            case 4: e.Value = Short(en.Translation); break;
            case 5: e.Value = en.Alert; break;
        }
    }

    private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
        var en = view[e.RowIndex];
        Color? bg = null;
        if (en.Alert.Length > 0) bg = RowWarn;
        else if (en.Translation.Length > 0) bg = RowDone;
        if (bg.HasValue && e.RowIndex != curRow)
            e.CellStyle.BackColor = bg.Value;
    }

    private void UpdateStatus()
    {
        if (mod == null) return;
        int total = entries.Count(e => e.Likely || e.Translation.Length > 0);
        int done = entries.Count(e => (e.Likely || e.Translation.Length > 0) && e.Translation.Length > 0);
        bar.Maximum = Math.Max(1, total);
        bar.Value = Math.Min(bar.Maximum, done);
        lblStatus.Text = $"{done} / {total} texts translated  |  {view.Count} rows shown  |  {entries.Count} texts in total";
    }

    // ------------------------------------------------------------------ editing

    private static string ToDisplay(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string FromDisplay(string text, string original)
    {
        string nl = original.Contains("\r\n") ? "\r\n" : "\n";
        return text.Replace("\r\n", "\n").Replace("\n", nl);
    }

    private void SetEditors(string orig, string tr)
    {
        loading = true;
        txtOrig.Text = ToDisplay(orig);
        txtTr.Text = ToDisplay(tr);
        loading = false;
    }

    private void OnRowChanged()
    {
        if (loading) return;
        int row = grid.CurrentCell?.RowIndex ?? -1;
        if (row < 0 || row >= view.Count) return;
        curRow = row;
        cur = view[row];
        SetEditors(cur.Original, cur.Translation);
    }

    private void TxtTr_TextChanged(object sender, EventArgs e)
    {
        if (loading || cur == null) return;
        cur.Translation = FromDisplay(txtTr.Text, cur.Original);
        dirty = true;
        if (curRow >= 0) grid.InvalidateRow(curRow);
        UpdateStatus();
    }

    private void CopyOriginal()
    {
        if (cur == null) return;
        txtTr.Text = ToDisplay(cur.Original);
        txtTr.Focus();
        txtTr.SelectionStart = txtTr.TextLength;
    }

    private void NextUntranslated()
    {
        if (view.Count == 0) return;
        int start = curRow + 1;
        for (int k = 0; k < view.Count; k++)
        {
            int i = (start + k) % view.Count;
            if (view[i].Translation.Length == 0)
            {
                grid.CurrentCell = grid.Rows[i].Cells[3];
                if (i >= 0) grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3);
                txtTr.Focus();
                return;
            }
        }
        lblStatus.Text = "Everything shown is already translated.";
    }

    private void FillDuplicates()
    {
        if (!NeedMod()) return;
        var known = new Dictionary<string, string>();
        foreach (var e in entries)
            if (e.Done) known.TryAdd(e.Original, e.Translation);
        int n = 0;
        foreach (var e in entries)
            if (e.Translation.Length == 0 && known.TryGetValue(e.Original, out var tr))
            {
                e.Translation = tr;
                n++;
            }
        if (n > 0) dirty = true;
        RefreshView();
        lblStatus.Text = $"{n} identical texts filled in automatically.";
    }

    // ------------------------------------------------------------------ dictionary

    private ToolStripDropDownButton BuildDictionaryMenu()
    {
        ddDict = new ToolStripDropDownButton("Dictionary") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        ddDict.DropDownItems.Add("Apply dictionary now", null, (s, e) => ApplyDictionaryNow());
        ddDict.DropDownItems.Add("Build the game's official dictionary (fr_FR)…", null, async (s, e) => await BuildOfficialAsync(true));
        ddDict.DropDownItems.Add("Learn from an already translated mod…", null, (s, e) => LearnFromModPair());
        ddDict.DropDownItems.Add(new ToolStripSeparator());
        ddDict.DropDownItems.Add("Import a dictionary (TSV)…", null, (s, e) => ImportDictionary());
        ddDict.DropDownItems.Add("Export the dictionary (TSV)…", null, (s, e) => ExportDictionary());
        ddDict.DropDownItems.Add("Open the dictionary folder", null, (s, e) => OpenDictionaryFolder());
        ddDict.DropDownItems.Add(new ToolStripSeparator());
        miLearn = new ToolStripMenuItem("Learn automatically on save") { CheckOnClick = true, Checked = GameDictionary.AutoLearn };
        miApply = new ToolStripMenuItem("Apply automatically when a mod is opened") { CheckOnClick = true, Checked = GameDictionary.AutoApply };
        miLearn.CheckedChanged += (s, e) => { GameDictionary.AutoLearn = miLearn.Checked; GameDictionary.SaveOptions(); };
        miApply.CheckedChanged += (s, e) => { GameDictionary.AutoApply = miApply.Checked; GameDictionary.SaveOptions(); };
        ddDict.DropDownItems.Add(miLearn);
        ddDict.DropDownItems.Add(miApply);
        return ddDict;
    }

    // Official dictionary: all the French translations shipped with the game (names, descriptions, dialogues)
    private async Task BuildOfficialAsync(bool explicitRequest)
    {
        string kenshi = WorkshopControl.LoadSetting();
        const string locale = "fr_FR";
        bool found = !string.IsNullOrWhiteSpace(kenshi) &&
                     File.Exists(Path.Combine(kenshi, "locale", locale, locale + ".translation"));
        if (!found)
        {
            if (!explicitRequest) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = "Choose the Kenshi folder (the one that contains the \"locale\" folder)",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            kenshi = dlg.SelectedPath;
        }
        else if (!explicitRequest)
        {
            var r = MessageBox.Show(this,
                "Kenshi includes its official French translation (names, descriptions, dialogues).\n\n" +
                "Build the official dictionary now? Texts already translated by the game will then be filled in automatically in your mods " +
                "(a few seconds, only once).",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }

        lblStatus.Text = "Building the official dictionary…";
        try
        {
            int n = await Task.Run(() => GameDictionary.BuildOfficial(kenshi, locale,
                s => BeginInvoke(new Action(() => lblStatus.Text = s))));
            string msg = $"Official dictionary ({locale}): {n} texts.";
            if (mod != null && GameDictionary.AutoApply)
            {
                int filled = GameDictionary.Apply(entries);
                if (filled > 0) { dirty = true; RefreshView(); }
                msg += $" {filled} texts filled in this mod.";
            }
            lblStatus.Text = msg;
        }
        catch (Exception ex) when (ex is ModFormatException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            lblStatus.Text = "Official dictionary failed.";
            MessageBox.Show(this, "Could not build the official dictionary:\n" + ex.Message, Title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyDictionaryNow()
    {
        if (!NeedMod()) return;
        int n = GameDictionary.Apply(entries);
        if (n > 0) dirty = true;
        RefreshView();
        lblStatus.Text = $"Dictionary: {n} texts filled in ({GameDictionary.Count} yours + {GameDictionary.OfficialCount} official).";
    }

    private void LearnFromModPair()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the TRANSLATED mod",
            InitialDirectory = mod != null && path != null ? Path.GetDirectoryName(path) : DefaultDir(),
            Filter = "Kenshi mod (*.mod)|*.mod|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string translated = dlg.FileName;
        string original = translated + ".original";
        if (!File.Exists(original))
        {
            using var dlg2 = new OpenFileDialog
            {
                Title = "Choose the ORIGINAL (untranslated) version of this mod",
                InitialDirectory = Path.GetDirectoryName(translated),
                Filter = "Kenshi mod|*.mod;*.original|All files (*.*)|*.*",
            };
            if (dlg2.ShowDialog(this) != DialogResult.OK) return;
            original = dlg2.FileName;
        }
        try
        {
            var a = Tools.BuildEntries(ModFile.Parse(File.ReadAllBytes(original)));
            var b = Tools.BuildEntries(ModFile.Parse(File.ReadAllBytes(translated)));
            int n = GameDictionary.LearnPairs(a, b);
            lblStatus.Text = $"Dictionary: {n} translations learned ({GameDictionary.Count} entries in total).";
        }
        catch (Exception ex) when (ex is ModFormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not read these files:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportDictionary()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import a dictionary (TSV: original, translation)",
            Filter = "TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int n = GameDictionary.Import(dlg.FileName);
            lblStatus.Text = $"Dictionary: {n} entries imported ({GameDictionary.Count} in total).";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportDictionary()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Export the dictionary",
            DefaultExt = "tsv",
            FileName = "kenshi-dictionary.tsv",
            Filter = "TSV (*.tsv)|*.tsv",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        lblStatus.Text = GameDictionary.Export(dlg.FileName)
            ? $"Dictionary exported: {dlg.FileName} ({GameDictionary.Count} entries)."
            : "Dictionary export failed.";
    }

    private void OpenDictionaryFolder()
    {
        try
        {
            Directory.CreateDirectory(GameDictionary.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GameDictionary.Folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S: SaveAs(); return true;
            case Keys.Control | Keys.O: OpenDialog(); return true;
            case Keys.Control | Keys.F: txtSearch.Focus(); txtSearch.SelectAll(); return true;
            case Keys.Control | Keys.D: CopyOriginal(); return true;
            case Keys.Alt | Keys.Down: NextUntranslated(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
