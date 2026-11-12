using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Where Looks reads and writes. Handed in, so the page and the tick that
    // re-applies can be pointed at a pretend Roblox in tests - nothing here
    // ever names the real one except Real().
    class LooksPlaces
    {
        public string Versions;     // Roblox's version folders
        public string Library;      // the mod packs kept by this app
        public string Backup;       // the originals of every file a pack replaced

        public static LooksPlaces Real()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            LooksPlaces p = new LooksPlaces();
            p.Versions = Path.Combine(local, "Roblox", "Versions");
            p.Library = Path.Combine(local, "RobloxKeeper", "mods");
            p.Backup = Path.Combine(local, "RobloxKeeper", "backup");
            return p;
        }
    }

    // What the Looks page and the main window share, kept apart from both so
    // it can be tested.
    static class Looks
    {
        // Quality by name; the number is Roblox's own 1 to 21.
        public static readonly string[] QualityNames = { "Roblox decides", "Lowest", "Low", "Medium", "High", "Highest" };
        public static readonly int[] QualityLevels = { 0, 1, 5, 10, 16, 21 };

        public static string Describe(LooksChoices c)
        {
            List<string> parts = new List<string>();
            if (c.AntiAliasing != LooksAntiAliasing.Default)
                parts.Add(c.AntiAliasing == LooksAntiAliasing.X1 ? "smooth edges off" : "smooth edges " + c.AntiAliasing.ToString().Substring(1) + "x");
            if (c.QualityLevel > 0) parts.Add("quality " + c.QualityLevel);
            if (c.Grass != LooksGrass.Default) parts.Add("grass " + c.Grass.ToString().ToLowerInvariant());
            if (c.DpiScalingOff) parts.Add("no display scaling");
            return parts.Count == 0 ? "nothing changed" : string.Join(", ", parts.ToArray());
        }

        public static bool Any(LooksChoices c)
        {
            return c != null && c.ToFlags().Count > 0;
        }

        // A version folder Roblox has added since the settings were last
        // written: it has none of them.
        public static bool FlagsMissingSomewhere(string versionsRoot)
        {
            foreach (string folder in LooksFlags.VersionFolders(versionsRoot))
                if (!File.Exists(LooksFlags.PathIn(folder))) return true;
            return false;
        }

        // What went wrong, in a line someone can act on.
        public static string Failures(IList<LooksFailure> failed)
        {
            if (failed == null || failed.Count == 0) return null;
            return (failed.Count == 1 ? "1 file" : failed.Count + " files") + " couldn't be changed - " + failed[0].Why
                 + ". Close every Roblox window and try again.";
        }

        // Roblox installs a new version into a new folder, which has none of
        // this in it. What was asked for is written there too. Null when
        // there was nothing to do; otherwise what to say about it.
        public static string Reapply(LooksPlaces at, LooksChoices chosen)
        {
            List<string> did = new List<string>();
            if (Any(chosen) && FlagsMissingSomewhere(at.Versions))
            {
                LooksFlagsResult r = LooksFlags.Apply(at.Versions, chosen);
                if (r.Written > 0 && r.Failed.Count == 0) did.Add("graphics settings");
            }
            if (Directory.Exists(at.Library))
            {
                // Only what Apply was pressed for - see LooksMods.Applied.
                LooksMods mods = new LooksMods(at.Library);
                if (mods.NeedsReapply(at.Versions, at.Backup))
                {
                    LooksModsResult r = mods.Reapply(at.Versions, at.Backup);
                    if (r.Failed.Count == 0) did.Add("mod packs");
                }
            }
            return did.Count == 0 ? null
                 : "Roblox updated - your " + string.Join(" and ", did.ToArray())
                   + " were put back on the new version.";
        }

        // Everything put back as Roblox installed it: the settings file's
        // entries taken out, every replaced file restored, every added one
        // removed. What it says, for the page and the activity list.
        public static string RestoreAll(LooksPlaces at)
        {
            LooksFlagsResult flags = LooksFlags.Clear(at.Versions);
            LooksModsResult mods = LooksMods.Restore(at.Versions, at.Backup);
            List<LooksFailure> failed = new List<LooksFailure>(flags.Failed);
            failed.AddRange(mods.Failed);
            // Nothing is applied any more, so nothing is to be put onto the
            // next Roblox version - even where a file could not be put back:
            // going on applying is the opposite of what was asked.
            try { if (Directory.Exists(at.Library)) new LooksMods(at.Library).ForgetApplied(); } catch { }
            string problem = Failures(failed);
            if (problem != null) return "Put back what it could: " + problem;
            return "Roblox is back as it was installed: " + mods.Restored + " file" + (mods.Restored == 1 ? "" : "s") + " restored"
                 + (mods.Removed > 0 ? ", " + mods.Removed + " removed" : "") + ", graphics settings cleared.";
        }
    }

    // Looks: how Roblox is drawn. Graphics settings Roblox allows to be set
    // from a file, and packs of replacement fonts, sounds and textures.
    //
    // This is the one page that changes Roblox's own files, so it says so,
    // changes nothing until asked, keeps the original of everything it
    // replaces, and has one button that puts it all back.
    class LooksPage : ShellPage
    {
        const int COL_W = 424, GAP = 14, FIELD_X = 170, ROW = 34;

        readonly LooksPlaces at;
        ThemedPicker cmbEdges, cmbQuality, cmbGrass;
        ThemedCheckBox chkDpi;
        Label lblGraphics, lblPacks, lblNone;
        ScrollPanel list;
        Button btnApply, btnPacks, btnRestore, btnAdd;
        LooksMods mods;
        bool busy;

        public LooksPage(IShell shell, Size size) : this(shell, size, LooksPlaces.Real()) { }

        public LooksPage(IShell shell, Size size, LooksPlaces at) : base(shell, size)
        {
            this.at = at;
            Head("Looks", "Change how Roblox looks. This page edits Roblox's own files, and you can undo it all.");
            BuildGraphics();
            BuildPacks();
            BuildRestore();
            BuildShaders();
            LooksExtras.AddTo(this, shell, at);     // Crosshair, Font and On-screen extras, in the head
        }

        public string GraphicsSays { get { return lblGraphics.Text; } }
        public string PacksSays { get { return lblPacks.Text; } }

        // ---------- graphics ----------

        void BuildGraphics()
        {
            Card card = WatchUi.CardAt(this, "GRAPHICS", CardLayout.PAD, HEAD_H + 8, COL_W, 318);
            int y = 42;

            card.Controls.Add(Ui.RowLabel("Smoother edges", Ui.PAD, y, 26, 140, 9f, Theme.Muted));
            cmbEdges = Ui.DarkCombo(FIELD_X, y, 200);
            foreach (string s in new string[] { "Roblox decides", "Off", "A little (2x)", "Smooth (4x)", "Smoothest (8x)" }) cmbEdges.Items.Add(s);
            card.Controls.Add(cmbEdges);
            y += ROW;

            card.Controls.Add(Ui.RowLabel("Graphics quality", Ui.PAD, y, 26, 140, 9f, Theme.Muted));
            cmbQuality = Ui.DarkCombo(FIELD_X, y, 200);
            foreach (string s in Looks.QualityNames) cmbQuality.Items.Add(s);
            card.Controls.Add(cmbQuality);
            y += ROW;

            card.Controls.Add(Ui.RowLabel("Grass", Ui.PAD, y, 26, 140, 9f, Theme.Muted));
            cmbGrass = Ui.DarkCombo(FIELD_X, y, 200);
            foreach (string s in new string[] { "Roblox decides", "None", "Close by only", "Far into the distance" }) cmbGrass.Items.Add(s);
            card.Controls.Add(cmbGrass);
            y += ROW;

            chkDpi = Ui.DarkCheck("Don't let Windows stretch the game (sharper on a scaled display)", Ui.PAD, y + 2, 9f);
            chkDpi.AutoSize = true;
            card.Controls.Add(chkDpi);
            y += ROW;

            Label hint = Ui.MutedLabel(
                "Close and reopen Roblox to see a change. Settings Roblox doesn't allow are ignored.", Ui.PAD, y + 4, 8.25f);
            hint.MaximumSize = new Size(COL_W - Ui.PAD * 2, 0);
            card.Controls.Add(hint);

            btnApply = WatchUi.Primary("Apply to Roblox", Ui.PAD, 318 - 44, 150, 30);
            btnApply.Click += delegate { ApplyGraphics(); };
            card.Controls.Add(btnApply);
            lblGraphics = Ui.MutedLabel("", Ui.PAD + 160, 318 - 46, 8.25f);
            lblGraphics.MaximumSize = new Size(COL_W - Ui.PAD * 2 - 160, 34);
            card.Controls.Add(lblGraphics);

            Show(shell.LooksChosen);
        }

        void Show(LooksChoices c)
        {
            cmbEdges.SelectedIndex = (int)c.AntiAliasing;
            int q = Array.IndexOf(Looks.QualityLevels, c.QualityLevel);
            if (q < 0)
            {
                // A level set by hand that has no name here is kept as it is.
                cmbQuality.Items.Add("Level " + c.QualityLevel);
                q = cmbQuality.Items.Count - 1;
            }
            cmbQuality.SelectedIndex = q;
            cmbGrass.SelectedIndex = (int)c.Grass;
            chkDpi.Checked = c.DpiScalingOff;
        }

        // The form as choices.
        public LooksChoices Collect()
        {
            LooksChoices c = new LooksChoices();
            c.AntiAliasing = (LooksAntiAliasing)Math.Max(0, cmbEdges.SelectedIndex);
            int q = Math.Max(0, cmbQuality.SelectedIndex);
            c.QualityLevel = q < Looks.QualityLevels.Length ? Looks.QualityLevels[q] : shell.LooksChosen.QualityLevel;
            c.Grass = (LooksGrass)Math.Max(0, cmbGrass.SelectedIndex);
            c.DpiScalingOff = chkDpi.Checked;
            return c;
        }

        // For tests: as if chosen from the lists.
        public void Choose(LooksAntiAliasing edges, int quality, LooksGrass grass, bool noScaling)
        {
            cmbEdges.SelectedIndex = (int)edges;
            cmbQuality.SelectedIndex = quality;
            cmbGrass.SelectedIndex = (int)grass;
            chkDpi.Checked = noScaling;
        }

        public void ApplyGraphics()
        {
            if (busy) return;
            LooksChoices c = Collect();
            shell.LooksChosen = c;
            Working(lblGraphics, "Saving...");
            InBackground(delegate { return LooksFlags.Apply(at.Versions, c); }, delegate(LooksFlagsResult r) { GraphicsDone(c, r); });
        }

        void GraphicsDone(LooksChoices c, LooksFlagsResult r)
        {
            busy = false;
            Buttons(true);
            if (r == null) { Said(lblGraphics, "Couldn't save the settings.", true); return; }
            string problem = Looks.Failures(r.Failed);
            if (problem != null) Said(lblGraphics, problem, true);
            else if (r.Written == 0) Said(lblGraphics, "Couldn't find Roblox, so nothing was changed.", true);
            else
            {
                Said(lblGraphics, Looks.Any(c) ? "Done. Close and reopen Roblox to see it." : "Cleared. Roblox decides everything again.", false);
                shell.Say("Looks: " + Looks.Describe(c) + " - saved for " + r.Written + " Roblox version" + (r.Written == 1 ? "" : "s") + ".");
            }
        }

        // ---------- mod packs ----------

        void BuildPacks()
        {
            int x = CardLayout.PAD + COL_W + GAP;
            Card card = WatchUi.CardAt(this, "MOD PACKS", x, HEAD_H + 8, COL_W, 318);
            Label what = Ui.MutedLabel(
                "Packs swap Roblox's fonts, cursors, sounds, sky and textures. A pack is a folder or zip. Bloxstrap packs work.", Ui.PAD, 38, 8.25f);
            what.MaximumSize = new Size(COL_W - Ui.PAD * 2, 0);
            card.Controls.Add(what);

            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 84);
            list.Size = new Size(COL_W - Ui.PAD * 2, 318 - 84 - 84);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);
            lblNone = Ui.MutedLabel("No packs yet. Press Add a pack - there are ready-made mouse pointers to try. "
                + "Nothing changes until you press Apply packs.", 2, 6, 8.25f);
            lblNone.MaximumSize = new Size(list.Width - 12, 0);

            btnAdd = WatchUi.Secondary("Add a pack", Ui.PAD, 318 - 78, 110, 28);
            btnAdd.Click += delegate { ShowAdd(btnAdd); };
            card.Controls.Add(btnAdd);
            LinkLabel how = Ui.RowLink("How to make one", Ui.PAD + 122, 318 - 78, 28, 8.25f);
            how.Click += delegate { Guide.Open(shell.Say); };
            card.Controls.Add(how);
            btnPacks = WatchUi.Primary("Apply packs", COL_W - Ui.PAD - 120, 318 - 78, 120, 28);
            btnPacks.Click += delegate { ApplyPacks(); };
            card.Controls.Add(btnPacks);

            lblPacks = Ui.MutedLabel("", Ui.PAD, 318 - 44, 8.25f);
            lblPacks.MaximumSize = new Size(COL_W - Ui.PAD * 2, 34);
            card.Controls.Add(lblPacks);
        }

        LooksMods Mods()
        {
            if (mods == null) mods = new LooksMods(at.Library);
            return mods;
        }

        public int PackRows { get { return mods == null ? 0 : mods.Packs.Count; } }

        void RebuildPacks()
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, lblNone)) c.Dispose();
            }
            List<LooksModPack> packs = Mods().Packs;
            if (packs.Count == 0) list.Controls.Add(lblNone);
            int y = 0;
            for (int i = 0; i < packs.Count; i++)
            {
                LooksModPack pack = packs[i];
                int index = i;
                ThemedCheckBox on = Ui.DarkCheck(pack.Name, 0, y + 3, 9f);
                on.AutoSize = false;
                on.Size = new Size(list.Width - 196, 22);
                on.Checked = pack.Enabled;
                on.CheckedChanged += delegate
                {
                    if (busy) return;
                    string also = Tick(pack.Name, on.Checked);
                    Said(lblPacks, "Press Apply packs to " + (on.Checked ? "add it to" : "remove it from") + " Roblox." + also, false);
                    // The rows are made again once this click is over, not
                    // from inside it: the box being clicked is one of them.
                    if (also.Length > 0 && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) RebuildPacks(); });
                };
                list.Controls.Add(on);

                // Before anything is applied: what this pack would change.
                LinkLabel files = Ui.RowLink("Files", list.Width - 190, y, 28, 8.25f);
                files.Click += delegate { ShowFiles(files, pack.Name); };
                list.Controls.Add(files);

                // Later in the list wins where two packs replace the same file.
                LinkLabel up = Ui.RowLink("Up", list.Width - 146, y, 28, 8.25f);
                up.Click += delegate { if (Mods().Move(pack.Name, index - 1)) { Mods().Save(); RebuildPacks(); } };
                list.Controls.Add(up);
                LinkLabel down = Ui.RowLink("Down", list.Width - 118, y, 28, 8.25f);
                down.Click += delegate { if (Mods().Move(pack.Name, index + 1)) { Mods().Save(); RebuildPacks(); } };
                list.Controls.Add(down);
                LinkLabel remove = Ui.RowLink("Remove", list.Width - 72, y, 28, 8.25f);      // clear of "Down", which ends at -74
                remove.Click += delegate { RemovePack(pack.Name); };
                list.Controls.Add(remove);
                y += 28;
            }
            list.ResumeLayout();
        }

        // A pack ticked or unticked in the list. The ready-made pointers all
        // replace the same pictures, so ticking one unticks the others here
        // as it does from the menu; what it says about that, or nothing.
        public string Tick(string name, bool on)
        {
            Mods().SetEnabled(name, on);
            List<string> off = new List<string>();
            if (on) PremadeMods.OnlyThis(Mods(), name, off);
            Mods().Save();
            return off.Count == 0 ? "" : " " + string.Join(", ", off.ToArray()) + " was turned off - only one pointer can show.";
        }

        // What a pack would put into Roblox, a file a line, whether or not
        // it is ticked - so it can be read before Apply is pressed.
        public List<string> FilesOf(string name)
        {
            const int SHOWN = 14;
            List<string> lines = new List<string>();
            LooksModPreview p = Mods().FilesOf(name);
            if (p == null) return lines;
            if (p.Problems.Count > 0)
            {
                lines.Add("This pack can't be used:");
                for (int i = 0; i < p.Problems.Count && i < SHOWN; i++) lines.Add(p.Problems[i]);
                return lines;
            }
            lines.Add(p.Files.Count == 0 ? "There are no files in this pack."
                    : "Replaces or adds " + p.Files.Count + (p.Files.Count == 1 ? " file" : " files") + " in Roblox:");
            for (int i = 0; i < p.Files.Count && i < SHOWN; i++)
                lines.Add(p.Files[i].Path + (p.Files[i].Wins ? "" : "   (" + p.Files[i].WonBy + " is lower in the list, so it wins)"));
            if (p.Files.Count > SHOWN) lines.Add("... and " + (p.Files.Count - SHOWN) + " more");
            return lines;
        }

        void ShowFiles(Control under, string name)
        {
            if (busy) return;
            ContextMenuStrip menu = DarkMenu.Make();
            foreach (string line in FilesOf(name)) menu.Items.Add(line);
            if (menu.Items.Count > 0) menu.Show(under, new Point(0, under.Height));
        }

        // The ones that come with the app first: a pack to try without
        // having to find one.
        void ShowAdd(Control under)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            foreach (PremadePack p in PremadeMods.All)
            {
                PremadePack pack = p;
                ToolStripItem item = menu.Items.Add("Ready-made: " + p.Name, null, delegate { AddReadyMade(pack); });
                item.ToolTipText = p.Says;
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("From a folder...", null, delegate { AddFolder(); });
            menu.Items.Add("From a zip file...", null, delegate { AddZip(); });
            menu.Show(under, new Point(0, under.Height));
        }

        // One of the app's own packs into the library, ticked. Nothing in
        // Roblox changes yet.
        public void AddReadyMade(PremadePack p)
        {
            if (busy) return;
            Working(lblPacks, "Making the pack...");
            List<string> off = new List<string>();
            InBackground(delegate { return PremadeMods.Use(Mods(), p, off) ?? ""; }, delegate(string why)
            {
                busy = false;
                Buttons(true);
                if (why == null) why = "it couldn't be made";
                if (why.Length > 0) Said(lblPacks, "That pack wasn't added: " + why + ".", true);
                else Said(lblPacks, p.Name + " is ticked" + (off.Count > 0 ? ", and " + string.Join(", ", off.ToArray()) + " turned off - only one pointer can show" : "")
                                  + ". Press Apply packs to add it to Roblox.", false);
                RebuildPacks();
            });
        }

        void AddFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose the pack's folder - the one that has content, ExtraContent or PlatformContent inside it.";
                d.ShowNewFolderButton = false;
                if (d.ShowDialog(FindForm()) == DialogResult.OK) Import(d.SelectedPath);
            }
        }

        void AddZip()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a mod pack";
                d.Filter = "Zip files (*.zip)|*.zip";
                if (d.ShowDialog(FindForm()) == DialogResult.OK) Import(d.FileName);
            }
        }

        // A folder or zip into the library. Nothing in Roblox changes yet.
        public void Import(string source)
        {
            if (busy) return;
            Working(lblPacks, "Checking the pack...");
            InBackground(delegate { return Mods().Import(source) ?? ""; }, delegate(string why)
            {
                busy = false;
                Buttons(true);
                if (why == null) why = "it couldn't be read";
                if (why.Length > 0) Said(lblPacks, "That pack wasn't added: " + why + ".", true);
                else Said(lblPacks, "Added. Press Apply packs to add it to Roblox.", false);
                RebuildPacks();
            });
        }

        void RemovePack(string name)
        {
            if (busy) return;
            string why = Mods().Remove(name);
            if (why != null) Said(lblPacks, "Couldn't remove it: " + why + ".", true);
            else Said(lblPacks, "Removed. Press Apply packs to take its files out of Roblox.", false);
            RebuildPacks();
        }

        public void ApplyPacks()
        {
            if (busy) return;
            Working(lblPacks, "Applying...");
            InBackground(delegate { return Mods().Apply(at.Versions, at.Backup); }, delegate(LooksModsResult r)
            {
                busy = false;
                Buttons(true);
                if (r == null) { Said(lblPacks, "Couldn't apply the packs.", true); return; }
                string problem = Looks.Failures(r.Failed);
                if (problem != null) { Said(lblPacks, problem, true); return; }
                int changed = r.Replaced + r.Added;
                Said(lblPacks, changed == 0 && r.Restored + r.Removed == 0
                    ? "Nothing to change. Roblox already has your ticked packs."
                    : "Done: " + changed + " file" + (changed == 1 ? "" : "s") + " changed"
                      + (r.Restored + r.Removed > 0 ? ", " + (r.Restored + r.Removed) + " put back" : "")
                      + ". Close and reopen Roblox to see it.", false);
                shell.Say("Looks: mod packs applied to " + r.Versions + " Roblox version" + (r.Versions == 1 ? "" : "s")
                        + " - " + changed + " file" + (changed == 1 ? "" : "s") + " changed. Your originals are saved.");
            });
        }

        // ---------- the way back ----------

        void BuildRestore()
        {
            Card card = WatchUi.CardAt(this, "PUT ROBLOX BACK", CardLayout.PAD, HEAD_H + 8 + 318 + GAP, COL_W * 2 + GAP, 84);
            Label what = Ui.MutedLabel(
                "Undoes everything on this page and puts Roblox back the way it was installed. "
                + "Roblox doesn't check these files today, but that could change - this is your undo button.", Ui.PAD, 38, 8.25f);
            what.MaximumSize = new Size(COL_W * 2 + GAP - Ui.PAD * 2 - 190, 0);
            card.Controls.Add(what);
            btnRestore = WatchUi.Secondary("Put Roblox back", COL_W * 2 + GAP - Ui.PAD - 170, 40, 170, 32);
            btnRestore.Click += delegate { Restore(); };
            card.Controls.Add(btnRestore);
        }

        public void Restore()
        {
            if (busy) return;
            Working(lblPacks, "Putting everything back...");
            InBackground(delegate { return Looks.RestoreAll(at); }, delegate(string said)
            {
                busy = false;
                Buttons(true);
                // The packs stay in the list, switched off: putting Roblox
                // back should not be undone by the next Apply. Read afresh
                // first - what was applied has just been forgotten on disk.
                mods = null;
                foreach (LooksModPack p in Mods().Packs) Mods().SetEnabled(p.Name, false);
                Mods().Save();
                shell.LooksChosen = new LooksChoices();
                Show(shell.LooksChosen);
                RebuildPacks();
                Said(lblGraphics, "", false);
                Said(lblPacks, said ?? "Couldn't put everything back.", said == null || said.StartsWith("Put back what"));
                if (said != null) shell.Say("Looks: " + said);
            });
        }

        // ---------- shaders ----------

        ThemedPicker cmbShader, cmbStrength;
        Label lblShader;
        readonly List<string> shaderChoices = new List<string>();     // what each row of the list stands for
        readonly Timer shaderTick = new Timer();
        bool fillingShaders;

        void BuildShaders()
        {
            int y = HEAD_H + 8 + 318 + GAP + 84 + GAP;
            Card card = WatchUi.CardAt(this, "SHADERS", CardLayout.PAD, y, COL_W * 2 + GAP, 126);

            card.Controls.Add(Ui.RowLabel("Look", Ui.PAD, 40, 26, 50, 9f, Theme.Muted));
            cmbShader = Ui.DarkCombo(Ui.PAD + 54, 40, 362);         // the longest look and its line fit closed
            cmbShader.SelectedIndexChanged += delegate { ShaderChosen(); };
            card.Controls.Add(cmbShader);

            card.Controls.Add(Ui.RowLabel("How strong", Ui.PAD + 444, 40, 26, 84, 9f, Theme.Muted));
            cmbStrength = Ui.DarkCombo(Ui.PAD + 532, 40, 120);
            foreach (string s in ShaderLooks.StrengthNames) cmbStrength.Items.Add(s);
            cmbStrength.SelectedIndex = Math.Max(0, Math.Min(ShaderLooks.StrengthNames.Length - 1, shell.ShaderStrength));
            cmbStrength.SelectedIndexChanged += delegate { ShaderChosen(); };
            card.Controls.Add(cmbStrength);

            LinkLabel folder = Ui.RowLink("Make my own...", COL_W * 2 + GAP - Ui.PAD - 110, 40, 26, 8.25f);
            folder.Click += delegate { OpenShaderFolder(); };
            card.Controls.Add(folder);

            lblShader = Ui.RowLabel("", Ui.PAD, 72, 20, COL_W * 2 + GAP - Ui.PAD * 2, 8.25f, Theme.Text);
            card.Controls.Add(lblShader);

            Label what = Ui.MutedLabel(
                "A filter drawn over the game - nothing is put into Roblox. "
                + "It uses some graphics power and shows the game a split second late.", Ui.PAD, 94, 8.25f);
            what.MaximumSize = new Size(COL_W * 2 + GAP - Ui.PAD * 2, 0);
            card.Controls.Add(what);

            FillShaders();
            // Only while the page is being looked at.
            shaderTick.Interval = 1000;
            shaderTick.Tick += delegate { lblShader.Text = shell.ShaderState; };
        }

        // Off, the built-in looks, then anything in the shaders folder.
        void FillShaders()
        {
            fillingShaders = true;
            cmbShader.Items.Clear();
            shaderChoices.Clear();
            cmbShader.Items.Add("Off");
            shaderChoices.Add(ShaderLooks.OFF);
            foreach (ShaderPreset p in ShaderLooks.Presets)
            {
                cmbShader.Items.Add(p.Name + " - " + p.Says);
                shaderChoices.Add(ShaderLooks.PRESET + p.Name);
            }
            foreach (string file in ShaderLooks.UserFiles(ShaderLooks.Folder))
            {
                cmbShader.Items.Add("Your own: " + file);
                shaderChoices.Add(ShaderLooks.FILE + file);
            }
            int at = shaderChoices.IndexOf(shell.Shader);
            if (at < 0 && ShaderLooks.IsOn(shell.Shader))
            {
                // Its file has been deleted or renamed. Showing "Off" while
                // the app went on trying it would leave no way to stop it:
                // choosing Off again changes nothing.
                shell.Say("Looks: " + ShaderLooks.Describe(shell.Shader) + " is no longer there, so the shader is off.");
                shell.SetShader(ShaderLooks.OFF, shell.ShaderStrength);
            }
            cmbShader.SelectedIndex = at < 0 ? 0 : at;
            fillingShaders = false;
            lblShader.Text = shell.ShaderState;
        }

        public int ShaderChoices { get { return shaderChoices.Count; } }

        // For tests: as if chosen from the list.
        public void ChooseShader(int index)
        {
            cmbShader.SelectedIndex = index;
        }

        void ShaderChosen()
        {
            if (fillingShaders || cmbShader.SelectedIndex < 0 || cmbShader.SelectedIndex >= shaderChoices.Count) return;
            string look = shaderChoices[cmbShader.SelectedIndex];
            int strength = Math.Max(0, cmbStrength.SelectedIndex);
            if (look == shell.Shader && strength == shell.ShaderStrength) return;
            shell.SetShader(look, strength);
            lblShader.Text = shell.ShaderState;
        }

        // The folder, with an example in it to start from.
        void OpenShaderFolder()
        {
            try
            {
                Directory.CreateDirectory(ShaderLooks.Folder);
                string example = Path.Combine(ShaderLooks.Folder, "example.hlsl");
                if (!File.Exists(example)) File.WriteAllText(example, ShaderLooks.Example().Replace("\n", "\r\n"));
                System.Diagnostics.Process.Start(ShaderLooks.Folder);
                lblShader.Text = "Copy example.hlsl, rename it and change it. It shows up in the list when you come back to this page.";
            }
            catch (Exception ex) { lblShader.Text = "Couldn't open the folder: " + ex.Message; }
        }

        // ---------- shared ----------

        void Working(Label where, string text)
        {
            busy = true;
            Buttons(false);
            Said(where, text, false);
        }

        // Everything that changes the packs or Roblox, not only the three
        // big buttons: a job on another thread is reading the list of packs
        // and copying their files, and a tick, a move or a Remove under it
        // would change both while it works.
        void Buttons(bool on)
        {
            btnApply.Enabled = btnPacks.Enabled = btnRestore.Enabled = btnAdd.Enabled = list.Enabled = on;
        }

        static void Said(Label where, string text, bool warn)
        {
            where.Text = text;
            where.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        protected override void OnEnterPage()
        {
            // The library is read here, not when the window is built - but
            // not from under a job that is still working on the one it has.
            if (!busy) mods = null;
            RebuildPacks();
            FillShaders();
            shaderTick.Start();
        }

        protected override void OnLeavePage() { shaderTick.Stop(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shaderTick.Dispose();
            base.Dispose(disposing);
        }
    }
}
