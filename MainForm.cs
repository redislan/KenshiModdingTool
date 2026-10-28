using System.Drawing;
using System.Windows.Forms;

namespace KenshiModdingTool;

public sealed class MainForm : Form
{
    private readonly TabControl tabs;
    private readonly TabPage pageTr, pageWs, pageMl;
    private readonly TranslatorControl translator;
    private readonly WorkshopControl workshop;
    private readonly ModlistControl modlist;

    public MainForm(string initial)
    {
        Text = "Kenshi Modding Tool";
        Width = 1400;
        Height = 880;
        MinimumSize = new Size(1000, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AllowDrop = true;

        translator = new TranslatorControl();
        workshop = new WorkshopControl();
        modlist = new ModlistControl();

        pageTr = new TabPage("Mod translator") { UseVisualStyleBackColor = true, Padding = new Padding(0) };
        pageWs = new TabPage("Workshop → mods") { UseVisualStyleBackColor = true, Padding = new Padding(0) };
        pageMl = new TabPage("Modlist") { UseVisualStyleBackColor = true, Padding = new Padding(0) };
        pageTr.Controls.Add(translator);
        pageWs.Controls.Add(workshop);
        pageMl.Controls.Add(modlist);
        tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(pageTr);
        tabs.TabPages.Add(pageWs);
        tabs.TabPages.Add(pageMl);
        Controls.Add(tabs);

        translator.TitleChanged += name => Text = "Kenshi Modding Tool - " + name;
        translator.ThemeChanged += dark =>
        {
            workshop.SetDark(dark);
            modlist.SetDark(dark);
            foreach (var p in new[] { pageTr, pageWs, pageMl })
            {
                p.UseVisualStyleBackColor = !dark;
                p.BackColor = dark ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
            }
        };
        Action<string> translate = path =>
        {
            if (!translator.ConfirmDiscard()) return;
            tabs.SelectedTab = pageTr;
            translator.OpenPath(path);
        };
        workshop.TranslateRequested += translate;
        modlist.TranslateRequested += translate;

        translator.InstalledFr += (original, fr) =>
        {
            try
            {
                bool added = ModlistControl.AddToLoadOrder(original, fr);
                if (added) modlist.Reload();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Could not edit mods.cfg:\n" + ex.Message, "Kenshi Modding Tool",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        // the list is reloaded on every visit, unless the order was changed and not saved
        tabs.SelectedIndexChanged += (s, e) =>
        {
            if (tabs.SelectedTab == pageMl && !modlist.Dirty) modlist.Reload();
        };

        DragEnter += (s, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (s, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 &&
                files[0].EndsWith(".mod", StringComparison.OrdinalIgnoreCase) && translator.ConfirmDiscard())
            {
                tabs.SelectedTab = pageTr;
                translator.OpenPath(files[0]);
            }
        };
        FormClosing += (s, e) =>
        {
            if (!translator.ConfirmDiscard() || !modlist.ConfirmDiscard()) e.Cancel = true;
        };
        Shown += (s, e) => { if (!string.IsNullOrEmpty(initial)) translator.OpenPath(initial); };
    }
}
