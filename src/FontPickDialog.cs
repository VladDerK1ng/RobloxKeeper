using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Sample text drawn in a font that may be nothing but a file.
    class FontSample : Control
    {
        string file, face, family;
        bool loaded;

        public string[] Lines = { "The quick brown fox", "Play   Settings   Leave", "0123456789  !?" };

        public FontSample()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Inset;
        }

        // Whether Windows could draw with it. False until a font is shown.
        public bool Drawn { get; private set; }

        public void Show(string fontFile, string fullName, string familyName)
        {
            Forget();
            file = fontFile;
            face = fullName;
            family = familyName;
            if (file != null) loaded = Xn.AddFontResourceExW(file, Xn.FR_PRIVATE, IntPtr.Zero) > 0;
            Invalidate();
        }

        void Forget()
        {
            if (loaded && file != null) Xn.RemoveFontResourceExW(file, Xn.FR_PRIVATE, IntPtr.Zero);
            loaded = false;
            Drawn = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (face == null)
            {
                using (Font f = Ui.F("Segoe UI", 9f))
                    TextRenderer.DrawText(e.Graphics, "Click a font on the left to see it here.", f, ClientRectangle, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            IntPtr dc = e.Graphics.GetHdc();
            try
            {
                Xn.SetBkMode(dc, 1);
                Xn.SetTextColor(dc, (uint)(Theme.Text.R | (Theme.Text.G << 8) | (Theme.Text.B << 16)));
                int y = 14;
                int[] heights = { 30, 20, 20 };
                for (int i = 0; i < Lines.Length; i++)
                {
                    IntPtr font = Make(dc, heights[i]);
                    IntPtr old = Xn.SelectObject(dc, font);
                    Xn.RECT r; r.L = 16; r.T = y; r.R = Width - 12; r.B = y + heights[i] + 14;
                    Xn.DrawTextW(dc, Lines[i], -1, ref r, 0x20 | 0x800 | 0x8000);    // single line, no prefix, end ellipsis
                    Xn.SelectObject(dc, old);
                    Xn.DeleteObject(font);
                    y += heights[i] + 18;
                }
            }
            finally { e.Graphics.ReleaseHdc(dc); }
        }

        // By its full name first ("Arial Bold"); if Windows answers with a
        // different font, by its family with the weight its name suggests.
        IntPtr Make(IntPtr dc, int px)
        {
            IntPtr f = Xn.CreateFontW(-px, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
            IntPtr old = Xn.SelectObject(dc, f);
            StringBuilder got = new StringBuilder(128);
            Xn.GetTextFaceW(dc, got.Capacity, got);
            Xn.SelectObject(dc, old);
            string name = got.ToString();
            if (string.Equals(name, face, StringComparison.OrdinalIgnoreCase) || string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
            {
                Drawn = true;
                return f;
            }
            if (!string.IsNullOrEmpty(family))
            {
                Xn.DeleteObject(f);
                bool bold = face.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 || face.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0;
                bool italic = face.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 || face.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
                f = Xn.CreateFontW(-px, 0, 0, 0, bold ? 700 : 400, italic ? 1u : 0u, 0, 0, 1, 0, 0, 5, 0, family);
                old = Xn.SelectObject(dc, f);
                got.Length = 0;
                Xn.GetTextFaceW(dc, got.Capacity, got);
                Xn.SelectObject(dc, old);
                Drawn = string.Equals(got.ToString(), family, StringComparison.OrdinalIgnoreCase);
            }
            return f;
        }

        protected override void Dispose(bool disposing)
        {
            Forget();
            base.Dispose(disposing);
        }
    }

    // A font for Roblox: click one that is on this PC, or choose a font
    // file, see it, press Apply.
    //
    // Apply makes a mod pack from it and applies the packs, exactly as the
    // Looks page does: every file it replaces is saved first, and one button
    // puts Roblox's own fonts back.
    class FontPickDialog : Form
    {
        // Rows are 22 px; the list is exactly 11 of them so no half row shows.
        const int W = 640, TOP = WatchUi.TITLEBAR_H + 6, CARD_W = W - 32, LIST_W = 240, ROW_H = 22, LIST_H = 11 * ROW_H;

        readonly IShell shell;
        readonly LooksPlaces at;
        ListBox list;
        FontSample sample;
        Label lblName, lblSays, lblNow;
        Button btnApply, btnBack, btnClose;
        List<FontChoice> fonts = new List<FontChoice>();
        string chosenFile, chosenName;
        bool busy;

        public FontPickDialog(IShell shell, LooksPlaces at)
        {
            this.shell = shell;
            this.at = at;
            WatchUi.Frame(this, "Roblox's font", W, 470);
            Build();
            Fill(FontPick.Installed());
        }

        // For tests.
        public string Says { get { return lblSays.Text; } }
        public string ChosenFile { get { return chosenFile; } }
        public int FontsListed { get { return fonts.Count; } }

        void Build()
        {
            Label how = Ui.MutedLabel(
                "Change the font Roblox uses for menus, chat and most buttons. "
                + "Click a font to preview it, then press Apply. Games with their own font keep it.", 16, TOP, 8.25f);
            how.MaximumSize = new Size(W - 32, 0);
            how.BackColor = Theme.Bg;
            Controls.Add(how);

            Card card = WatchUi.CardAt(this, "CHOOSE A FONT", 16, TOP + 40, CARD_W, 42 + LIST_H + 52);

            list = new ListBox();
            list.Location = new Point(Ui.PAD, 42);
            list.Size = new Size(LIST_W, LIST_H);
            list.BackColor = Theme.Inset;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.Font = Ui.F("Segoe UI", 9f);
            list.IntegralHeight = false;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = ROW_H;
            list.DrawItem += ListDrawItem;
            // A dark scrollbar to match the window. An older Windows has no
            // dark theme for it and keeps the light one.
            list.HandleCreated += delegate
            {
                try { Xn.SetWindowTheme(list.Handle, "DarkMode_Explorer", null); }
                catch { }
            };
            list.SelectedIndexChanged += delegate { ListChosen(); };
            card.Controls.Add(list);

            Button file = WatchUi.Secondary("Use a font file...", Ui.PAD, 42 + LIST_H + 10, 150, 28);
            file.Click += delegate { ChooseFile(); };
            card.Controls.Add(file);

            int rx = Ui.PAD + LIST_W + 16, rw = CARD_W - rx - Ui.PAD;
            lblName = Ui.RowLabel("", rx, 42, 22, rw, 9f, Theme.Text);
            lblName.AutoEllipsis = true;
            card.Controls.Add(lblName);
            sample = new FontSample();
            sample.Location = new Point(rx, 68);
            sample.Size = new Size(rw, 150);
            card.Controls.Add(sample);
            lblNow = Ui.MutedLabel("", rx, 68 + 150 + 10, 8.25f);
            lblNow.MaximumSize = new Size(rw, 0);
            card.Controls.Add(lblNow);
            Label reach = Ui.MutedLabel(
                "Font collections (.ttc) aren't in the list. Close and reopen Roblox to see a change.", rx, 68 + 150 + 34, 8.25f);
            reach.MaximumSize = new Size(rw, 0);
            card.Controls.Add(reach);

            int foot = card.Bottom + 12;
            // Buttons and the status line share the card's left edge (16 px),
            // with 10 px between the buttons and the status line.
            lblSays = Ui.MutedLabel("", 16, foot + 40, 8.25f);
            lblSays.BackColor = Theme.Bg;
            lblSays.MaximumSize = new Size(W - 32, 34);
            Controls.Add(lblSays);
            btnApply = WatchUi.Primary("Apply to Roblox", 16, foot, 150, 30);
            btnApply.Click += delegate { Apply(); };
            Controls.Add(btnApply);
            btnBack = WatchUi.Secondary("Put Roblox's font back", 16 + 158, foot, 180, 30);
            btnBack.Click += delegate { PutBack(); };
            Controls.Add(btnBack);
            btnClose = WatchUi.Secondary("Close", W - 16 - 90, foot, 90, 30);
            btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnClose);
            ClientSize = new Size(W, foot + 30 + 48);
            ShowNow();
        }

        void ShowNow()
        {
            string name = Extras.Loaded ? Extras.Current.FontName : "";
            bool inPlace = false;
            try { inPlace = FontPick.InRoblox(at.Versions); } catch { }
            lblNow.Text = inPlace ? "In Roblox now: " + (string.IsNullOrEmpty(name) ? "a font of yours" : name) + "."
                                  : "In Roblox now: its own fonts.";
        }

        public void Fill(List<FontChoice> installed)
        {
            fonts = installed ?? new List<FontChoice>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (FontChoice f in fonts) list.Items.Add(f.Name);
            list.EndUpdate();
        }

        // Each row is drawn here so it fits the app: a dark row, the chosen
        // one in the accent colour, the name left aligned and centred.
        void ListDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool chosen = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(chosen ? Theme.Accent : Theme.Inset))
                e.Graphics.FillRectangle(back, e.Bounds);
            Rectangle text = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), list.Font, text, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void ListChosen()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= fonts.Count) return;
            Choose(fonts[list.SelectedIndex].File);
        }

        void ChooseFile()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a font file";
                d.Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                list.ClearSelected();
                Choose(d.FileName);
            }
        }

        // A font is looked at: it must be one Roblox can read before Apply
        // will take it.
        public void Choose(string file)
        {
            string why, family = null, name = null;
            try
            {
                FileInfo fi = new FileInfo(file);
                if (!fi.Exists) why = "the file is no longer there";
                else if (fi.Length > 40 * 1024 * 1024) why = "it's bigger than 40 MB";
                else name = FontPick.NameIn(File.ReadAllBytes(file), out family, out why);
            }
            catch (Exception ex) { why = ex.Message; }

            if (name == null)
            {
                chosenFile = chosenName = null;
                lblName.Text = "";
                sample.Show(null, null, null);
                Said("That font can't be used: " + why + ".", true);
                return;
            }
            chosenFile = file;
            chosenName = name;
            lblName.Text = name;
            sample.Show(file, name, family);
            Said("", false);
        }

        void Said(string text, bool warn)
        {
            lblSays.Text = text;
            lblSays.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        void Busy(bool on)
        {
            busy = on;
            btnApply.Enabled = btnBack.Enabled = !on;
        }

        void InBackground(Func<string> work, Action<string> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                try { said = work(); }
                catch (Exception ex) { said = "!" + ex.Message; }
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) done(said); }); }
                catch { }
            });
        }

        // The work of Apply, without a window: the pack is made, put in the
        // library and the packs applied. What to say; it starts with "!" when
        // it went wrong.
        public static string ApplyFont(LooksPlaces at, string file)
        {
            LooksMods mods = new LooksMods(at.Library);
            string why = FontPick.Use(mods, file, at.Versions);
            if (why != null) return "!That font wasn't applied: " + why + ".";
            LooksModsResult r = mods.Apply(at.Versions, at.Backup);
            string problem = Looks.Failures(r.Failed);
            if (problem != null) return "!" + problem;
            return "Done. Close and reopen Roblox to see your font.";
        }

        // The font pack taken out and the packs applied again, which puts
        // back the original of every file only it had replaced.
        public static string RemoveFont(LooksPlaces at)
        {
            if (!Directory.Exists(at.Library)) return "Roblox already has its own fonts.";
            LooksMods mods = new LooksMods(at.Library);
            // Asked of Roblox's folder as well as the library: the font can be
            // in Roblox with no line for it left in the library's list.
            bool had = FontPick.Drop(mods);
            if (!had && !FontPick.InRoblox(at.Versions)) return "Roblox already has its own fonts.";
            LooksModsResult r = mods.Apply(at.Versions, at.Backup);
            string problem = Looks.Failures(r.Failed);
            if (problem != null) return "!" + problem;
            return "Roblox's own fonts are back. Close and reopen Roblox to see them.";
        }

        public void Apply()
        {
            if (busy) return;
            if (chosenFile == null) { Said("Click a font first.", true); return; }
            if (!sample.Drawn)
            {
                Said("Windows couldn't draw that font, so Roblox probably can't either. Choose another.", true);
                return;
            }
            string file = chosenFile, name = chosenName;
            Busy(true);
            Said("Applying your font...", false);
            InBackground(delegate { return ApplyFont(at, file); }, delegate(string said)
            {
                Busy(false);
                bool bad = said.StartsWith("!");
                Said(bad ? said.Substring(1) : said, bad);
                if (bad) return;
                Remember(name);
                if (shell != null) shell.Say("Looks: Roblox's font changed to " + name + ". Your originals are saved, and Put Roblox's font back undoes it.");
                ShowNow();
            });
        }

        public void PutBack()
        {
            if (busy) return;
            Busy(true);
            Said("Putting Roblox's fonts back...", false);
            InBackground(delegate { return RemoveFont(at); }, delegate(string said)
            {
                Busy(false);
                bool bad = said.StartsWith("!");
                Said(bad ? said.Substring(1) : said, bad);
                if (bad) return;
                Remember("");
                if (shell != null) shell.Say("Looks: " + said);
                ShowNow();
            });
        }

        static void Remember(string fontName)
        {
            if (!Extras.Loaded) return;
            ExtrasSettings s = Extras.Current;
            s.FontName = fontName;
            Extras.Apply(s);
        }
    }
}
