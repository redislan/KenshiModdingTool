using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KenshiModdingTool;

public sealed class WorkshopMod
{
    public string Id;
    public string Name;
    public string SrcDir;
    public string SrcMod;
    public string DestDir;
    public string DestMod;
    public long Size;
    public DateTime SrcTime;
    public string Code;      // new, update, ok, local
    public string Status;
}

// "Workshop -> mods" tab: copies Steam Workshop mods (numbered folders)
// to <Kenshi>\mods\<ModName>\, like Nexus Mods does.
public sealed class WorkshopControl : UserControl
{
    private const string AppId = "233860";
    private const string Title = "Kenshi Modding Tool";

    public event Action<string> TranslateRequested;

    private TextBox txtKenshi, log;
    private DataGridView grid;
    private CheckBox chkForce;
    private Label lblInfo, lblPath;
    private FlowLayoutPanel rowPath;
    private Panel rowButtons;
    private Button btnDetect, btnBrowse, btnOpen, btnScan, btnCopy, btnTranslate;
    private readonly List<WorkshopMod> items = new();
    private bool dark;
    private bool busy;

    public WorkshopControl()
    {
        Font = new Font("Segoe UI", 9.5f);
        Dock = DockStyle.Fill;
        BuildUi();
        ApplyTheme();

        string saved = LoadSetting();
        if (!string.IsNullOrEmpty(saved)) txtKenshi.Text = saved;
        HandleCreated += (s, e) => BeginInvoke(new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(txtKenshi.Text)) Detect(false);
            if (IsKenshiPath(txtKenshi.Text)) Scan();
        }));
    }

    // ------------------------------------------------------------------ interface

    private void BuildUi()
    {
        lblPath = new Label { Text = "Kenshi folder:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
        txtKenshi = new TextBox { Width = 520, Margin = new Padding(3, 5, 3, 3) };
        btnDetect = new Button { Text = "Detect", AutoSize = true };
        btnBrowse = new Button { Text = "Browse…", AutoSize = true };
        btnOpen = new Button { Text = "Open mods folder", AutoSize = true };
        btnDetect.Click += (s, e) => { Detect(true); if (IsKenshiPath(txtKenshi.Text)) Scan(); };
        btnBrowse.Click += (s, e) => Browse();
        btnOpen.Click += (s, e) => OpenModsFolder();
        txtKenshi.Leave += (s, e) => { if (IsKenshiPath(txtKenshi.Text)) SaveSetting(txtKenshi.Text); };
        rowPath = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        rowPath.Controls.AddRange(new Control[] { lblPath, txtKenshi, btnDetect, btnBrowse, btnOpen });

        btnScan = new Button { Text = "Scan Workshop", Left = 8, Top = 6, AutoSize = true };
        btnCopy = new Button { Text = "Copy checked mods to mods\\", Left = 170, Top = 6, AutoSize = true };
        btnTranslate = new Button { Text = "Translate selected mod", Left = 390, Top = 6, AutoSize = true };
        chkForce = new CheckBox { Text = "Force (copy again even if up to date or modified locally)", Left = 600, Top = 10, AutoSize = true };
        lblInfo = new Label { Text = "", AutoSize = true, Left = 8, Top = 40 };
        btnScan.Click += (s, e) => Scan();
        btnCopy.Click += async (s, e) => await CopyCheckedAsync();
        btnTranslate.Click += async (s, e) => await TranslateSelectedAsync();
        rowButtons = new Panel { Dock = DockStyle.Top, Height = 64 };
        rowButtons.Controls.AddRange(new Control[] { btnScan, btnCopy, btnTranslate, chkForce, lblInfo });

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
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "chk", HeaderText = "", Width = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "mod", HeaderText = "Mod", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 100, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", Width = 190, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "Workshop ID", Width = 120, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "Size", Width = 90, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "date", HeaderText = "Workshop date", Width = 140, ReadOnly = true });
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        log = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 130,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f),
        };

        // the order of addition determines the stacking of docked controls
        Controls.Add(grid);
        Controls.Add(log);
        Controls.Add(rowButtons);
        Controls.Add(rowPath);
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
        foreach (Control c in new Control[] { rowPath, rowButtons, lblPath, lblInfo, chkForce })
        {
            c.BackColor = bg;
            c.ForeColor = fg;
        }
        foreach (var t in new[] { txtKenshi, log })
        {
            t.BackColor = edit;
            t.ForeColor = fg;
        }
        foreach (var b in new[] { btnDetect, btnBrowse, btnOpen, btnScan, btnCopy, btnTranslate })
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

    private void RecolorRows()
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not WorkshopMod m) continue;
            row.DefaultCellStyle.BackColor = m.Code switch
            {
                "new" => dark ? Color.FromArgb(34, 66, 44) : Color.FromArgb(226, 244, 230),
                "update" => dark ? Color.FromArgb(94, 72, 28) : Color.FromArgb(255, 241, 214),
                "local" => dark ? Color.FromArgb(48, 56, 90) : Color.FromArgb(226, 232, 250),
                _ => dark ? Color.FromArgb(43, 43, 48) : SystemColors.Window,
            };
        }
    }

    // ------------------------------------------------------------------ Steam / Kenshi detection

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KenshiModdingTool", "settings.txt");

    public static string LoadSetting()
    {
        try { return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : ""; }
        catch { return ""; }
    }

    public static void SaveSetting(string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllText(SettingsFile, value);
        }
        catch { }
    }

    internal static List<string> SteamLibraries()
    {
        var libs = new List<string>();
        string steam = null;
        foreach (var (root, key) in new[]
        {
            (Registry.CurrentUser, @"Software\Valve\Steam"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
        })
        {
            try
            {
                using var k = root.OpenSubKey(key);
                string p = (k?.GetValue("SteamPath") ?? k?.GetValue("InstallPath")) as string;
                if (!string.IsNullOrEmpty(p)) { steam = p.Replace('/', '\\'); break; }
            }
            catch { }
        }
        if (steam == null) return libs;
        libs.Add(steam);
        try
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        catch { }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsKenshiPath(string p) =>
        !string.IsNullOrWhiteSpace(p) && Directory.Exists(p.Trim()) &&
        (File.Exists(Path.Combine(p.Trim(), "kenshi_x64.exe")) || File.Exists(Path.Combine(p.Trim(), "kenshi.exe")) ||
         Directory.Exists(Path.Combine(p.Trim(), "data")));

    private void Detect(bool verbose)
    {
        foreach (var lib in SteamLibraries())
        {
            string c = Path.Combine(lib, "steamapps", "common", "Kenshi");
            if (IsKenshiPath(c))
            {
                txtKenshi.Text = c;
                SaveSetting(c);
                Log("Kenshi found: " + c);
                return;
            }
        }
        if (verbose)
            MessageBox.Show(this, "Kenshi was not found automatically.\nClick \"Browse…\" and choose the game folder " +
                "(the one that contains kenshi_x64.exe).", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose the Kenshi folder (the one that contains kenshi_x64.exe)", UseDescriptionForTitle = true };
        if (IsKenshiPath(txtKenshi.Text)) dlg.SelectedPath = txtKenshi.Text.Trim();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtKenshi.Text = dlg.SelectedPath;
        if (IsKenshiPath(dlg.SelectedPath)) { SaveSetting(dlg.SelectedPath); Scan(); }
        else MessageBox.Show(this, "This folder does not look like a Kenshi installation.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private string ModsDir => Path.Combine(txtKenshi.Text.Trim(), "mods");

    private void OpenModsFolder()
    {
        if (!IsKenshiPath(txtKenshi.Text)) { Detect(true); return; }
        try
        {
            Directory.CreateDirectory(ModsDir);
            Process.Start(new ProcessStartInfo { FileName = ModsDir, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private IEnumerable<string> WorkshopDirs()
    {
        var dirs = new List<string>();
        foreach (var lib in SteamLibraries())
            dirs.Add(Path.Combine(lib, "steamapps", "workshop", "content", AppId));
        try
        {
            string kenshi = txtKenshi.Text.Trim().TrimEnd('\\', '/');
            string steamapps = Path.GetDirectoryName(Path.GetDirectoryName(kenshi));
            if (!string.IsNullOrEmpty(steamapps))
                dirs.Add(Path.Combine(steamapps, "workshop", "content", AppId));
        }
        catch { }
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists);
    }

    // ------------------------------------------------------------------ scan

    private void Scan()
    {
        if (!IsKenshiPath(txtKenshi.Text))
        {
            MessageBox.Show(this, "First set the Kenshi folder (Detect or Browse button).", Title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        items.Clear();
        grid.Rows.Clear();
        var dirs = WorkshopDirs().ToList();
        if (dirs.Count == 0)
        {
            lblInfo.Text = "No Workshop folder found: are you subscribed to any Kenshi mods?";
            Log(lblInfo.Text);
            return;
        }

        foreach (var w in dirs)
        {
            Log("Workshop: " + w);
            foreach (var dir in Directory.GetDirectories(w))
            {
                try
                {
                    var modFiles = Directory.GetFiles(dir, "*.mod");
                    if (modFiles.Length == 0) continue;
                    string src = modFiles[0];
                    string name = Path.GetFileNameWithoutExtension(src);
                    string destDir = Path.Combine(ModsDir, name);
                    string destMod = Path.Combine(destDir, Path.GetFileName(src));
                    var m = new WorkshopMod
                    {
                        Id = Path.GetFileName(dir),
                        Name = name,
                        SrcDir = dir,
                        SrcMod = src,
                        DestDir = destDir,
                        DestMod = destMod,
                        SrcTime = File.GetLastWriteTime(src),
                        Size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length),
                    };
                    if (!File.Exists(destMod)) { m.Code = "new"; m.Status = "Not in mods\\ yet"; }
                    else
                    {
                        double diff = (File.GetLastWriteTimeUtc(src) - File.GetLastWriteTimeUtc(destMod)).TotalSeconds;
                        if (diff > 2) { m.Code = "update"; m.Status = "Workshop update available"; }
                        else if (diff < -2) { m.Code = "local"; m.Status = "Modified locally"; }
                        else { m.Code = "ok"; m.Status = "Up to date"; }
                    }
                    items.Add(m);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log($"  skipped {Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }

        foreach (var m in items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            int i = grid.Rows.Add(m.Code == "new" || m.Code == "update", m.Name, m.Status, m.Id, FormatSize(m.Size), m.SrcTime.ToString("yyyy-MM-dd HH:mm"));
            grid.Rows[i].Tag = m;
        }
        RecolorRows();
        int nNew = items.Count(x => x.Code == "new"), nUp = items.Count(x => x.Code == "update"), nLoc = items.Count(x => x.Code == "local");
        lblInfo.Text = $"{items.Count} Workshop mods: {nNew} to copy, {nUp} updates, {nLoc} modified locally, {items.Count - nNew - nUp - nLoc} up to date.";
        Log(lblInfo.Text);
    }

    private static string FormatSize(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB" : $"{Math.Max(1, b / 1024)} KB";

    // ------------------------------------------------------------------ copy

    private void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(s))); return; }
        log.AppendText(s + Environment.NewLine);
    }

    private void SetBusy(bool b)
    {
        busy = b;
        rowPath.Enabled = !b;
        rowButtons.Enabled = !b;
        grid.Enabled = !b;
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
        {
            string d = Path.Combine(dest, Path.GetFileName(f));
            if (File.Exists(d)) File.SetAttributes(d, FileAttributes.Normal);
            File.Copy(f, d, true);
        }
        foreach (var sub in Directory.GetDirectories(src))
            CopyDir(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    // Returns true if the mod was copied
    private bool CopyOne(WorkshopMod m, bool force)
    {
        if (m.Code == "ok" && !force) { Log($"  [up to date]  {m.Name}"); return false; }
        if (m.Code == "local" && !force)
        {
            Log($"  [skipped]  {m.Name}: modified locally (check \"Force\" to overwrite)");
            return false;
        }
        if (m.Code == "local" && File.Exists(m.DestMod))
        {
            File.Copy(m.DestMod, m.DestMod + ".local-backup", true);
            Log($"  backup of your local version: {Path.GetFileName(m.DestMod)}.local-backup");
        }
        CopyDir(m.SrcDir, m.DestDir);
        Log($"  [copied]   {m.Name}   (ID {m.Id})");
        return true;
    }

    private async Task CopyCheckedAsync()
    {
        var chosen = new List<WorkshopMod>();
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is WorkshopMod m && row.Cells[0].Value is true) chosen.Add(m);
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, "Check at least one mod in the list.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        bool force = chkForce.Checked;
        SetBusy(true);
        Log($"Copying {chosen.Count} mod(s) to {ModsDir}…");
        int copied = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var m in chosen)
                {
                    try { if (CopyOne(m, force)) copied++; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Log($"  [error]  {m.Name}: {ex.Message}");
                    }
                }
            });
        }
        finally
        {
            SetBusy(false);
        }
        Log($"Done: {copied} mod(s) copied.");
        Scan();
    }

    private async Task TranslateSelectedAsync()
    {
        if (grid.CurrentRow?.Tag is not WorkshopMod m)
        {
            MessageBox.Show(this, "Select a mod in the list.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!File.Exists(m.DestMod))
        {
            SetBusy(true);
            try
            {
                Log($"Copying {m.Name} to mods\\ before translating…");
                await Task.Run(() => CopyOne(m, true));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log($"  [error]  {m.Name}: {ex.Message}");
                SetBusy(false);
                return;
            }
            SetBusy(false);
            Scan();
        }
        TranslateRequested?.Invoke(m.DestMod);
    }
}
