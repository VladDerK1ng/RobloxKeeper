using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The crosshair: what it looks like, when it shows, and the key that
    // switches it.
    //
    // Written for someone who has never set one up. Start from a ready-made
    // one, then drag sliders and click colours; the picture on the left is
    // the crosshair exactly as it will be, on the game's own middle when a
    // game is open. A row is only there while the shape chosen has that
    // part. Nothing is typed.
    class CrosshairDialog : Form
    {
        const int W = 628, EDGE = 16, TOP = WatchUi.TITLEBAR_H + 6, CARD_W = W - EDGE * 2;
        const int PREVIEW_W = 220, PREVIEW_H = 196;     // even both ways, so its middle is a line, as on most games
        // How far a row's label is moved down from its row's middle so its
        // words sit on the same line as the words in the control beside it.
        // Measured in the picture of the window: centred in the row, a
        // label's capitals sat a pixel above the slider's knob and the
        // picker's words. (A button's words sit a pixel higher than a
        // picker's, so the key row's label is not moved.)
        const int LABEL_NUDGE = 1;
        // In the "when it shows" card: the "Show it on" picker, and the list of clients under it.
        const int WHEN_ON_Y = 180, WHEN_CLIENTS_Y = WHEN_ON_Y + 34;
        const int COL_X = Ui.PAD + PREVIEW_W + 18, COL_W = CARD_W - COL_X - Ui.PAD, FIELD_X = 92, ROW_H = 32;

        static readonly string[] ShapeNames = { "Cross", "Dot", "Circle", "T shape", "My own picture" };

        readonly IShell shell;
        readonly ExtrasSettings working;
        readonly List<ThemedCheckBox> clientTicks = new List<ThemedCheckBox>();
        Card look, when;
        CrossPreview preview;
        ThemedPicker cmbPreset, cmbShape, cmbOn;
        Panel rowSize, rowGap, rowThick, rowColour, rowChecks, rowOpacity, rowPicture, rowClients;
        GlassSlider sldSize, sldGap, sldThick, sldOpacity;
        GlassSwatches swatches;
        ThemedCheckBox chkOutline, chkDot, chkOn, chkAim;
        Label lblPicture, lblNudge, lblProblem, lblHow;
        Button btnKey, btnSave, btnCancel;
        bool building = true;

        public CrosshairDialog(IShell shell)
        {
            this.shell = shell;
            working = Extras.Current;
            WatchUi.Frame(this, "Crosshair", W, 520);
            Build();
            building = false;
            ShowStyle();
            Reflow();
            Bitmap game = Extras.GameMiddle(new Size(PREVIEW_W, PREVIEW_H));
            if (game != null) preview.SetGame(game);
        }

        // For tests.
        public ExtrasSettings Working { get { return working; } }
        public bool ShowsGap { get; private set; }
        public bool ShowsThick { get; private set; }
        public bool ShowsColour { get; private set; }
        public bool ShowsPicture { get; private set; }
        public bool ShowsClients { get; private set; }
        public string ProblemText { get { return lblProblem.Text; } }
        public void ChooseShape(CrossShape s) { cmbShape.SelectedIndex = (int)s; }
        public void ChoosePreset(int index) { cmbPreset.SelectedIndex = index + 1; }
        public void ChooseOn(bool onlyTicked) { cmbOn.SelectedIndex = onlyTicked ? 1 : 0; }
        public int PresetChoices { get { return cmbPreset.Items.Count; } }

        // ---------- layout ----------

        Panel Row(Control parent, string label, int height)
        {
            Panel p = new Panel();
            p.Size = new Size(COL_W, height);
            p.BackColor = Theme.Card;
            p.Left = COL_X;
            if (label != null) p.Controls.Add(Ui.RowLabel(label, 0, LABEL_NUDGE, height, FIELD_X - 6, 9f, Theme.Muted));
            parent.Controls.Add(p);
            return p;
        }

        GlassSlider Slider(Panel row, int min, int max, Func<int, string> says)
        {
            GlassSlider s = new GlassSlider();
            s.Location = new Point(FIELD_X, 3);
            s.Width = COL_W - FIELD_X;
            s.Minimum = min;
            s.Maximum = max;
            s.Says = says;
            s.ValueChanged += delegate { Edited(); };
            row.Controls.Add(s);
            return s;
        }

        static string Pixels(int v) { return v == 1 ? "1 pixel" : v + " pixels"; }

        // A small filled triangle pointing (dx, dy), on the middle of the button.
        static void Arrow(Graphics g, Control on, int dx, int dy)
        {
            const float LONG = 10f, SHORT = 5f;     // its base, and how far it points
            float cx = on.Width / 2f, cy = on.Height / 2f;
            PointF[] p;
            if (dy != 0)
            {
                float tip = cy + dy * SHORT / 2f, back = cy - dy * SHORT / 2f;
                p = new PointF[] { new PointF(cx - LONG / 2f, back), new PointF(cx + LONG / 2f, back), new PointF(cx, tip) };
            }
            else
            {
                float tip = cx + dx * SHORT / 2f, back = cx - dx * SHORT / 2f;
                p = new PointF[] { new PointF(back, cy - LONG / 2f), new PointF(back, cy + LONG / 2f), new PointF(tip, cy) };
            }
            System.Drawing.Drawing2D.SmoothingMode was = g.SmoothingMode;
            System.Drawing.Drawing2D.PixelOffsetMode wasOffset = g.PixelOffsetMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // Pixel n covers n to n+1, so Width/2 is the button's true middle.
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            using (SolidBrush ink = new SolidBrush(on.Enabled ? Theme.Text : Theme.Muted)) g.FillPolygon(ink, p);
            g.SmoothingMode = was;
            g.PixelOffsetMode = wasOffset;
        }

        void Build()
        {
            lblHow = Ui.MutedLabel(
                "A crosshair in the middle of the game, even when the game hides your mouse. "
                + "Nothing is put into Roblox.", 16, TOP, 8.25f);
            lblHow.MaximumSize = new Size(W - 32, 0);
            lblHow.BackColor = Theme.Bg;
            Controls.Add(lblHow);

            look = WatchUi.CardAt(this, "HOW IT LOOKS", EDGE, TOP + 40, CARD_W, 300);

            preview = new CrossPreview();
            preview.Location = new Point(Ui.PAD, 42);
            preview.Size = new Size(PREVIEW_W, PREVIEW_H);
            look.Controls.Add(preview);

            // Under the picture: nudging it off the exact middle.
            int ny = 42 + PREVIEW_H + 10;
            look.Controls.Add(Ui.RowLabel("Move it", Ui.PAD, ny + LABEL_NUDGE, 26, 56, 9f, Theme.Muted));
            // The arrows are drawn, not typed: the font's arrow characters
            // came out at different sizes and off the middle of their buttons.
            string[] names = { "Move it left", "Move it up", "Move it down", "Move it right" };
            int[] dx = { -1, 0, 0, 1 }, dy = { 0, -1, 1, 0 };
            for (int i = 0; i < 4; i++)
            {
                int ddx = dx[i], ddy = dy[i];
                Button b = WatchUi.Secondary("", Ui.PAD + 58 + i * 32, ny, 28, 26);
                b.AccessibleName = names[i];
                b.Paint += delegate(object o, PaintEventArgs e) { Arrow(e.Graphics, (Control)o, ddx, ddy); };
                b.Click += delegate { working.Cross.NudgeX += ddx; working.Cross.NudgeY += ddy; working.Cross.Tidy(); Edited(); };
                look.Controls.Add(b);
            }
            LinkLabel centre = Ui.RowLink("Middle", Ui.PAD + 58 + 4 * 32 + 4, ny, 26, 8.25f);
            centre.Click += delegate { working.Cross.NudgeX = working.Cross.NudgeY = 0; Edited(); };
            look.Controls.Add(centre);
            lblNudge = Ui.MutedLabel("", Ui.PAD, ny + 30, 8.25f);
            lblNudge.MaximumSize = new Size(PREVIEW_W, 0);
            look.Controls.Add(lblNudge);

            // The column of choices.
            Panel rowPreset = Row(look, "Start from", ROW_H);
            cmbPreset = Ui.DarkCombo(FIELD_X, 3, COL_W - FIELD_X);
            cmbPreset.Items.Add("Choose a ready-made one...");
            foreach (CrossPreset p in CrossPresets.All) cmbPreset.Items.Add(p.Name);
            cmbPreset.SelectedIndex = 0;
            cmbPreset.SelectedIndexChanged += delegate { PresetChosen(); };
            rowPreset.Controls.Add(cmbPreset);
            rowPreset.Top = 42;

            Panel rowShape = Row(look, "Shape", ROW_H);
            cmbShape = Ui.DarkCombo(FIELD_X, 3, COL_W - FIELD_X);
            foreach (string s in ShapeNames) cmbShape.Items.Add(s);
            cmbShape.SelectedIndexChanged += delegate { Edited(); };
            rowShape.Controls.Add(cmbShape);
            rowShape.Top = 42 + ROW_H;

            rowPicture = Row(look, "Picture", ROW_H);
            // Narrow enough to leave "None chosen yet" room on one line.
            Button choose = WatchUi.Secondary("Choose a picture...", FIELD_X, 3, 124, 26);
            choose.Click += delegate { ChoosePicture(); };
            rowPicture.Controls.Add(choose);
            lblPicture = Ui.RowLabel("", FIELD_X + 130, LABEL_NUDGE, ROW_H, COL_W - FIELD_X - 130, 8.25f, Theme.Muted);
            lblPicture.AutoEllipsis = true;
            rowPicture.Controls.Add(lblPicture);

            rowSize = Row(look, "Size", ROW_H);
            // For a picture the slider counts in eights (see ShowStyle); it says the real size.
            sldSize = Slider(rowSize, CrossStyle.MIN_SIZE, CrossStyle.MAX_SIZE,
                delegate(int v) { return Pixels(working.Cross.Shape == CrossShape.Picture ? v * 8 : v); });
            rowGap = Row(look, "Gap", ROW_H);
            sldGap = Slider(rowGap, 0, CrossStyle.MAX_GAP, delegate(int v) { return v == 0 ? "None" : Pixels(v); });
            rowThick = Row(look, "Thickness", ROW_H);
            sldThick = Slider(rowThick, CrossStyle.MIN_THICK, CrossStyle.MAX_THICK, Pixels);

            rowColour = Row(look, "Colour", ROW_H);
            swatches = new GlassSwatches();
            swatches.Colours = CrossPresets.Colours;
            swatches.Location = new Point(FIELD_X, 3);
            swatches.Picked += delegate { Edited(); };
            rowColour.Controls.Add(swatches);
            // Under the colours, not beside them: eight swatches fill the
            // column, and a link after them was drawn past its edge and cut off.
            rowColour.Height = ROW_H + 18;
            LinkLabel other = Ui.RowLink("Other colour...", FIELD_X, ROW_H - 4, 20, 8.25f);
            other.Click += delegate { OtherColour(); };
            rowColour.Controls.Add(other);

            rowChecks = Row(look, null, ROW_H);
            chkOutline = Ui.DarkCheck("Dark edge", FIELD_X, 6, 9f);
            chkOutline.AutoSize = true;
            chkOutline.CheckedChanged += delegate { Edited(); };
            rowChecks.Controls.Add(chkOutline);
            // Beside the first tick by its real width, so it is never cut off.
            chkDot = Ui.DarkCheck("Dot in the middle", 0, 6, 9f);
            chkDot.AutoSize = true;
            chkDot.Left = FIELD_X + chkOutline.PreferredSize.Width + 12;
            chkDot.CheckedChanged += delegate { Edited(); };
            rowChecks.Controls.Add(chkDot);

            rowOpacity = Row(look, "See-through", ROW_H);
            sldOpacity = Slider(rowOpacity, CrossStyle.MIN_OPACITY / 10, CrossStyle.MAX_OPACITY / 10,
                delegate(int v) { return v >= 10 ? "Solid" : v <= 3 ? "Faint" : v <= 6 ? "Half" : "Mostly solid"; });

            // When it shows.
            when = WatchUi.CardAt(this, "WHEN IT SHOWS", EDGE, 0, CARD_W, 150);
            chkOn = Ui.DarkCheck("Show the crosshair over Roblox", Ui.PAD, 42, 9f);
            chkOn.AutoSize = true;
            chkOn.ForeColor = Theme.Text;
            when.Controls.Add(chkOn);

            chkAim = Ui.DarkCheck("Only while I'm aiming", Ui.PAD, 70, 9f);
            chkAim.AutoSize = true;
            chkAim.ForeColor = Theme.Text;
            chkAim.CheckedChanged += delegate { Edited(); };
            when.Controls.Add(chkAim);
            Label aimHint = Ui.MutedLabel("Shows in first person or shift-lock. Hides in menus.", Ui.PAD + 25, 92, 8.25f);
            aimHint.MaximumSize = new Size(CARD_W - Ui.PAD * 2 - 25, 0);
            when.Controls.Add(aimHint);

            when.Controls.Add(Ui.RowLabel("Key to switch it", Ui.PAD, 122, 28, 120, 9f, Theme.Muted));
            btnKey = WatchUi.Secondary("", Ui.PAD + 124, 122, 130, 28);
            btnKey.Click += delegate { ChooseKey(); };
            when.Controls.Add(btnKey);
            LinkLabel noKey = Ui.RowLink("No key", Ui.PAD + 264, 122, 28, 8.25f);
            noKey.Click += delegate { working.CrossKey = 0; ShowKey(); };
            when.Controls.Add(noKey);
            // The key reaches the game too, so one the game uses would do two things at once.
            Label keyHint = Ui.MutedLabel("Roblox sees this key too, so pick one the game doesn't use.", Ui.PAD + 124, 152, 8.25f);
            keyHint.MaximumSize = new Size(CARD_W - Ui.PAD * 2 - 124, 0);
            when.Controls.Add(keyHint);

            // 28 high against the picker's 26: one pixel up puts their middles together.
            when.Controls.Add(Ui.RowLabel("Show it on", Ui.PAD, WHEN_ON_Y - 1 + LABEL_NUDGE, 28, 120, 9f, Theme.Muted));
            cmbOn = Ui.DarkCombo(Ui.PAD + 124, WHEN_ON_Y, 240);
            cmbOn.Items.Add("Every Roblox window");
            cmbOn.Items.Add("Only the ones I tick");
            cmbOn.SelectedIndexChanged += delegate { Reflow(); };
            when.Controls.Add(cmbOn);

            rowClients = new Panel();
            rowClients.BackColor = Theme.Card;
            rowClients.Left = Ui.PAD + 124;
            rowClients.Width = CARD_W - Ui.PAD * 2 - 124;
            when.Controls.Add(rowClients);
            BuildClients();

            lblProblem = Ui.MutedLabel("", 16, 0, 8.25f);
            lblProblem.ForeColor = Theme.Amber;
            lblProblem.BackColor = Theme.Bg;
            lblProblem.MaximumSize = new Size(W - 250, 34);
            Controls.Add(lblProblem);
            btnCancel = WatchUi.Secondary("Cancel", W - EDGE - 90, 0, 90, 30);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);
            btnSave = WatchUi.Primary("Save", W - EDGE - 90 - 8 - 110, 0, 110, 30);
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);
        }

        // The clients open now, and any named before that are not open.
        void BuildClients()
        {
            List<string> names = Extras.ClientNames();
            foreach (string n in working.Only) if (!Has(names, n)) names.Add(n);
            int y = 0;
            foreach (string name in names)
            {
                ThemedCheckBox t = Ui.DarkCheck(name, 0, y, 9f);
                t.AutoSize = true;
                t.Checked = Has(working.Only, name);
                rowClients.Controls.Add(t);
                clientTicks.Add(t);
                y += 24;
            }
            if (names.Count == 0)
            {
                Label none = Ui.MutedLabel("Open Roblox first, then come back to tick its window.", 0, 2, 8.25f);
                rowClients.Controls.Add(none);
                y = 22;
            }
            rowClients.Height = y + 4;
        }

        static bool Has(IList<string> list, string name)
        {
            foreach (string s in list) if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The form set from what is chosen.
        void ShowStyle()
        {
            building = true;
            CrossStyle s = working.Cross;
            cmbShape.SelectedIndex = (int)s.Shape;
            sldSize.Minimum = s.Shape == CrossShape.Picture ? CrossStyle.MIN_PICTURE / 8 : CrossStyle.MIN_SIZE;
            sldSize.Maximum = s.Shape == CrossShape.Picture ? CrossStyle.MAX_PICTURE / 8 : CrossStyle.MAX_SIZE;
            sldSize.Value = s.Shape == CrossShape.Picture ? s.PictureSize / 8 : s.Size;
            sldGap.Value = s.Gap;
            sldThick.Value = s.Thick;
            swatches.Chosen = s.Colour;
            chkOutline.Checked = s.Outline;
            chkDot.Checked = s.Dot;
            sldOpacity.Value = (s.Opacity + 5) / 10;
            lblPicture.Text = string.IsNullOrEmpty(s.Picture) ? "None chosen yet" : Path.GetFileName(s.Picture);
            chkOn.Checked = working.CrossOn;
            chkAim.Checked = s.AimOnly;
            cmbOn.SelectedIndex = working.Only.Count > 0 ? 1 : 0;
            ShowKey();
            building = false;
            ShowNudge();
            preview.Show(s);
        }

        void ShowKey()
        {
            btnKey.Text = KeyPick.NameOf(working.CrossKey, "Choose a key");
        }

        void ShowNudge()
        {
            CrossStyle s = working.Cross;
            if (s.NudgeX == 0 && s.NudgeY == 0)
                lblNudge.Text = "Right in the middle. Use the arrows if the game's middle is a bit off.";
            else
                lblNudge.Text = "Moved " + (s.NudgeX == 0 ? "" : Math.Abs(s.NudgeX) + (s.NudgeX < 0 ? " left" : " right"))
                              + (s.NudgeX != 0 && s.NudgeY != 0 ? ", " : "")
                              + (s.NudgeY == 0 ? "" : Math.Abs(s.NudgeY) + (s.NudgeY < 0 ? " up" : " down")) + ".";
        }

        // Something on the form changed: the style is read back off it.
        void Edited()
        {
            if (building) return;
            CrossStyle s = working.Cross;
            CrossShape was = s.Shape;
            s.Shape = (CrossShape)Math.Max(0, cmbShape.SelectedIndex);
            if (s.Shape != was)
            {
                // The one slider means a different thing for a picture.
                building = true;
                sldSize.Minimum = s.Shape == CrossShape.Picture ? CrossStyle.MIN_PICTURE / 8 : CrossStyle.MIN_SIZE;
                sldSize.Maximum = s.Shape == CrossShape.Picture ? CrossStyle.MAX_PICTURE / 8 : CrossStyle.MAX_SIZE;
                sldSize.Value = s.Shape == CrossShape.Picture ? s.PictureSize / 8 : s.Size;
                building = false;
            }
            if (s.Shape == CrossShape.Picture) s.PictureSize = sldSize.Value * 8;
            else s.Size = sldSize.Value;
            s.Gap = sldGap.Value;
            s.Thick = sldThick.Value;
            s.Colour = swatches.Chosen;
            s.Outline = chkOutline.Checked;
            s.Dot = chkDot.Checked;
            s.Opacity = sldOpacity.Value * 10;
            s.AimOnly = chkAim.Checked;
            s.Tidy();
            ShowNudge();
            preview.Show(s);
            preview.Update();
            lblProblem.Text = s.Shape == CrossShape.Picture && preview.Problem != null && !string.IsNullOrEmpty(s.Picture)
                ? "That picture can't be shown: " + preview.Problem + "." : "";
            Reflow();
        }

        void PresetChosen()
        {
            if (building || cmbPreset.SelectedIndex <= 0) return;
            CrossStyle p = CrossPresets.All[cmbPreset.SelectedIndex - 1].Style.Clone();
            // Where it was moved to, the user's own picture, and when it
            // shows are kept: a ready-made one is only a look.
            p.NudgeX = working.Cross.NudgeX; p.NudgeY = working.Cross.NudgeY;
            p.AimOnly = working.Cross.AimOnly;
            p.Picture = working.Cross.Picture; p.PictureSize = working.Cross.PictureSize;
            working.Cross = p;
            ShowStyle();
            Reflow();
        }

        void OtherColour()
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = swatches.Chosen;
                d.FullOpen = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                swatches.Chosen = Color.FromArgb(255, d.Color.R, d.Color.G, d.Color.B);
                Edited();
            }
        }

        void ChoosePicture()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a picture for your crosshair";
                d.Filter = "Pictures (*.png;*.gif;*.bmp;*.jpg)|*.png;*.gif;*.bmp;*.jpg;*.jpeg";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                UsePicture(d.FileName);
            }
        }

        // The picture is copied beside the app's settings, so the crosshair
        // still shows after the original is moved or deleted.
        public void UsePicture(string file)
        {
            string kept = file;
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(ExtrasSettings.DefaultPath), "crosshair");
                Directory.CreateDirectory(folder);
                kept = Path.Combine(folder, "mine" + Path.GetExtension(file).ToLowerInvariant());
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, kept, true);
            }
            catch { kept = file; }
            working.Cross.Picture = kept;
            lblPicture.Text = Path.GetFileName(file);
            Edited();
        }

        void ChooseKey()
        {
            byte vk = KeyPick.Ask(this);
            if (vk == 0) return;
            string why = Extras.KeyProblemLive(vk, working, "cross", shell == null ? null : shell.Macros, shell == null ? (byte)0 : shell.ShotKey);
            if (why != null) { lblProblem.Text = why; return; }
            lblProblem.Text = "";
            working.CrossKey = vk;
            ShowKey();
        }

        // Rows come and go with the shape; everything under them moves up.
        void Reflow()
        {
            if (building) return;
            CrossShape shape = working.Cross.Shape;
            bool lines = shape == CrossShape.Cross || shape == CrossShape.Tee;
            ShowsPicture = shape == CrossShape.Picture;
            ShowsGap = lines;
            ShowsThick = lines || shape == CrossShape.Circle;
            ShowsColour = shape != CrossShape.Picture;
            bool dot = lines || shape == CrossShape.Circle;

            int y = 42 + ROW_H * 2;
            y = Place(rowPicture, ShowsPicture, y);
            y = Place(rowSize, true, y);
            y = Place(rowGap, ShowsGap, y);
            y = Place(rowThick, ShowsThick, y);
            y = Place(rowColour, ShowsColour, y);
            y = Place(rowChecks, ShowsColour, y);
            chkDot.Visible = dot;
            y = Place(rowOpacity, true, y);

            int leftBottom = lblNudge.Bottom + 12;
            look.Height = Math.Max(y + 12, leftBottom);

            when.Top = look.Bottom + 10;
            ShowsClients = cmbOn.SelectedIndex == 1;
            rowClients.Visible = ShowsClients;
            rowClients.Top = WHEN_CLIENTS_Y;
            when.Height = ShowsClients ? WHEN_CLIENTS_Y + rowClients.Height + 10 : WHEN_CLIENTS_Y + 8;

            int foot = when.Bottom + 12;
            btnSave.Top = btnCancel.Top = foot;
            lblProblem.Top = foot + 2;
            ClientSize = new Size(W, foot + 30 + 14);
        }

        static int Place(Panel row, bool visible, int y)
        {
            row.Visible = visible;
            if (!visible) return y;
            row.Top = y;
            return y + row.Height;
        }

        // What the form says, as settings.
        public ExtrasSettings Collect()
        {
            working.CrossOn = chkOn.Checked;
            working.Only.Clear();
            if (cmbOn.SelectedIndex == 1)
                foreach (ThemedCheckBox t in clientTicks) if (t.Checked) working.Only.Add(t.Text);
            return working;
        }

        void Save()
        {
            ExtrasSettings s = Collect();
            if (s.CrossOn && s.Cross.Shape == CrossShape.Picture && string.IsNullOrEmpty(s.Cross.Picture))
            {
                lblProblem.Text = "Choose a picture first, or pick another shape.";
                return;
            }
            if (cmbOn.SelectedIndex == 1 && s.Only.Count == 0)
            {
                lblProblem.Text = "Tick at least one Roblox window, or choose Every Roblox window.";
                return;
            }
            // Only the crosshair's part is written: the other extras may
            // have been changed in their own window since this one opened.
            ExtrasSettings latest = Extras.Current;
            latest.Cross = s.Cross.Clone();
            latest.CrossOn = s.CrossOn;
            latest.CrossKey = s.CrossKey;
            latest.Only = new List<string>(s.Only);
            Extras.Apply(latest);
            if (shell != null)
                shell.Say(s.CrossOn
                    ? "Crosshair on. It shows while a Roblox window is in front"
                      + (s.CrossKey != 0 ? ". " + MacroKeys.Name(s.CrossKey) + " switches it." : ".")
                    : "Crosshair off.");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
