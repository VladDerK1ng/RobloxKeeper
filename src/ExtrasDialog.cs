using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The on-screen extras: a keys display, zoom, a dim round the middle, a
    // read-out of ping, memory and processor, and a key held for you.
    //
    // Each is one tick. Under each tick is a line saying what it reads and
    // what it cannot do, and its choices appear only once it is ticked.
    class ExtrasDialog : Form
    {
        const int W = 628, EDGE = 16, TOP = WatchUi.TITLEBAR_H + 6, CARD_W = W - EDGE * 2, INNER = CARD_W - Ui.PAD * 2;
        const int HEAD_H = 46, OPT_H = 34;

        readonly IShell shell;
        readonly ExtrasSettings working;
        Card card;
        readonly List<Panel> heads = new List<Panel>(), options = new List<Panel>();
        readonly List<ThemedCheckBox> ticks = new List<ThemedCheckBox>();
        ThemedPicker cmbKeysCorner, cmbKeysSize, cmbZoom, cmbDark, cmbHole, cmbStatsCorner, cmbHoldWith;
        Button btnZoomKey, btnHoldKey, btnHoldWith, btnSave, btnCancel;
        Label lblProblem;
        bool building = true;

        public const int KEYS = 0, ZOOM = 1, DIM = 2, PING = 3, MEMORY = 4, HOLD = 5;

        public ExtrasDialog(IShell shell)
        {
            this.shell = shell;
            working = Extras.Current;
            WatchUi.Frame(this, "On-screen extras", W, 500);
            Build();
            building = false;
            Reflow();
        }

        // For tests.
        public ExtrasSettings Working { get { return working; } }
        public void Tick(int which, bool on) { ticks[which].Checked = on; }
        public bool ShowsOptions(int which) { return options[which] != null && shown[which]; }
        public string ProblemText { get { return lblProblem.Text; } }
        readonly bool[] shown = new bool[6];

        // Where a tick's own text starts, across: the box and the gap after it.
        // The grey line under it starts here too.
        const int TEXT_X = 25;

        // The grey explanation under a tick. A plain Label leaves a little
        // room in front of its text that the tick's own text does not, which
        // put it two pixels to the right of what it explains; this one draws
        // with no room added, exactly where the tick's text starts.
        class Note : Label
        {
            const TextFormatFlags HOW = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

            public Note(string text, int x, int y, int width, float size)
            {
                Text = text;
                AutoSize = false;
                Font = Ui.F("Segoe UI", size);
                ForeColor = Theme.Muted;
                BackColor = Theme.Card;
                Location = new Point(x, y);
                Size = new Size(width, TextRenderer.MeasureText(text, Font, new Size(width, int.MaxValue), HOW).Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (SolidBrush bg = new SolidBrush(BackColor)) e.Graphics.FillRectangle(bg, ClientRectangle);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, HOW);
            }
        }

        Panel Head(string title, string says, bool on)
        {
            Panel p = new Panel();
            p.BackColor = Theme.Card;
            p.Size = new Size(INNER, HEAD_H);
            p.Left = Ui.PAD;
            ThemedCheckBox t = Ui.DarkCheck(title, 0, 2, 9f);
            t.AutoSize = true;
            t.ForeColor = Theme.Text;
            t.Checked = on;
            t.CheckedChanged += delegate { Reflow(); };
            p.Controls.Add(t);
            Note l = new Note(says, TEXT_X, 24, INNER - TEXT_X, 8.25f);
            p.Controls.Add(l);
            p.Height = Math.Max(HEAD_H, l.Top + l.Height + 6);
            card.Controls.Add(p);
            heads.Add(p);
            ticks.Add(t);
            return p;
        }

        Panel Options()
        {
            Panel p = new Panel();
            p.BackColor = Theme.Card;
            p.Size = new Size(INNER - 25, OPT_H);
            p.Left = Ui.PAD + 25;
            card.Controls.Add(p);
            options.Add(p);
            return p;
        }

        // Every control in a row of choices is CTRL_H tall and CTRL_TOP down, and
        // its label is given that same box, so the label's text is centred on
        // the same line as the list or button beside it.
        const int CTRL_TOP = 3, CTRL_H = 26;

        static Label RowName(string label, int x, int w)
        {
            return Ui.RowLabel(label, x, CTRL_TOP, CTRL_H, w, 9f, Theme.Muted);
        }

        static ThemedPicker Pick(Panel row, string label, int x, int labelW, int w, string[] items, int chosen)
        {
            row.Controls.Add(RowName(label, x, labelW));
            ThemedPicker c = Ui.DarkCombo(x + labelW, CTRL_TOP, w);
            c.Height = CTRL_H;
            foreach (string s in items) c.Items.Add(s);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, chosen));
            row.Controls.Add(c);
            return c;
        }

        void Build()
        {
            Label how = Ui.MutedLabel(
                "Extras that show on top of the game while a Roblox window is in front. "
                + "They don't read or change anything inside Roblox.", 16, TOP, 8.25f);
            how.MaximumSize = new Size(W - 32, 0);
            how.BackColor = Theme.Bg;
            Controls.Add(how);

            card = WatchUi.CardAt(this, "EXTRAS", EDGE, TOP + 40, CARD_W, 300);

            Head("Show my keys and clicks",
                 "Lights up W A S D, Space, Shift and the mouse buttons as you press them, with clicks per second. "
                 + "Nothing is saved.", working.KeysOn);
            Panel o = Options();
            cmbKeysCorner = Pick(o, "Where", 0, 50, 130, ExtrasSettings.CornerNames, working.KeysCorner);
            cmbKeysSize = Pick(o, "Size", 200, 40, 110, ExtrasSettings.SizeNames, working.KeysSize);

            Head("Zoom in while I hold a key",
                 "Zooms in on the middle of the screen while you hold the key. "
                 + "Roblox sees this key too, so pick one the game doesn't use.", working.ZoomOn);
            o = Options();
            o.Controls.Add(RowName("Key", 0, 50));
            btnZoomKey = WatchUi.Secondary(KeyPick.NameOf(working.ZoomKey, "Choose a key"), 50, CTRL_TOP, 130, CTRL_H);
            btnZoomKey.Click += delegate { ChooseKey("zoom"); };
            o.Controls.Add(btnZoomKey);
            cmbZoom = Pick(o, "How close", 200, 74, 150, ExtrasSettings.ZoomNames, working.ZoomLevel);

            Head("Dim everything but the middle",
                 "Darkens the game except a circle in the middle, to help you focus.",
                 working.DimOn);
            o = Options();
            cmbDark = Pick(o, "How dark", 0, 66, 114, ExtrasSettings.DarkNames, working.DimDark);
            cmbHole = Pick(o, "Clear circle", 200, 84, 110, ExtrasSettings.HoleNames, working.DimHole);

            Head("Show my ping to the game's server",
                 "Shows your ping to the game's server. Some servers don't answer, and then it says so.", working.PingOn);
            options.Add(null);

            Head("Show the game's memory and processor use",
                 "Shows what Roblox is using, like Task Manager. For frames per second, press Shift+F5 in Roblox.", working.MemoryOn);
            o = Options();
            cmbStatsCorner = Pick(o, "Where ping and memory go", 0, 164, 130, ExtrasSettings.CornerNames, working.StatsCorner);

            Head("Hold a key down for me",
                 "Tap once to hold a key down, like sprinting. Tap again to let go. "
                 + "Roblox doesn't see the key that switches it, so pick one the game doesn't need.", working.HoldOn);
            o = Options();
            o.Controls.Add(RowName("Hold", 0, 40));
            btnHoldKey = WatchUi.Secondary(KeyPick.NameOf(working.HoldKey, "Choose a key"), 40, CTRL_TOP, 110, CTRL_H);
            btnHoldKey.Click += delegate { ChooseKey("hold"); };
            o.Controls.Add(btnHoldKey);
            cmbHoldWith = Pick(o, "Switched by", 166, 82, 150, new string[] { "Tapping that key", "Another key" }, working.HoldWith == 0 ? 0 : 1);
            cmbHoldWith.SelectedIndexChanged += delegate { Reflow(); };
            btnHoldWith = WatchUi.Secondary(KeyPick.NameOf(working.HoldWith, "Choose a key"), 410, CTRL_TOP, 110, CTRL_H);
            btnHoldWith.Click += delegate { ChooseKey("holdwith"); };
            o.Controls.Add(btnHoldWith);

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

        void ChooseKey(string what)
        {
            byte vk = KeyPick.Ask(this);
            if (vk == 0) return;
            Collect();
            string why = Extras.KeyProblemLive(vk, working, what, shell == null ? null : shell.Macros, shell == null ? (byte)0 : shell.ShotKey);
            if (why != null) { lblProblem.Text = why; return; }
            lblProblem.Text = "";
            if (what == "zoom") { working.ZoomKey = vk; btnZoomKey.Text = MacroKeys.Name(vk); }
            else if (what == "hold") { working.HoldKey = vk; btnHoldKey.Text = MacroKeys.Name(vk); }
            else { working.HoldWith = vk; btnHoldWith.Text = MacroKeys.Name(vk); }
        }

        void Reflow()
        {
            if (building) return;
            int y = 40;
            for (int i = 0; i < heads.Count; i++)
            {
                heads[i].Top = y;
                y += heads[i].Height;
                Panel o = options[i];
                // Ping has no choices of its own: where it goes is chosen with memory's.
                shown[i] = o != null && (ticks[i].Checked || (i == MEMORY && ticks[PING].Checked));
                if (o != null)
                {
                    o.Visible = shown[i];
                    if (shown[i]) { o.Top = y; y += o.Height; }
                }
                y += 6;
            }
            btnHoldWith.Visible = cmbHoldWith.SelectedIndex == 1;
            card.Height = y + 8;
            int foot = card.Bottom + 12;
            btnSave.Top = btnCancel.Top = foot;
            lblProblem.Top = foot + 2;
            ClientSize = new Size(W, foot + 30 + 14);
        }

        // What the form says, as settings.
        public ExtrasSettings Collect()
        {
            working.KeysOn = ticks[KEYS].Checked;
            working.KeysCorner = cmbKeysCorner.SelectedIndex;
            working.KeysSize = cmbKeysSize.SelectedIndex;
            working.ZoomOn = ticks[ZOOM].Checked;
            working.ZoomLevel = cmbZoom.SelectedIndex;
            working.DimOn = ticks[DIM].Checked;
            working.DimDark = cmbDark.SelectedIndex;
            working.DimHole = cmbHole.SelectedIndex;
            working.PingOn = ticks[PING].Checked;
            working.MemoryOn = ticks[MEMORY].Checked;
            working.StatsCorner = cmbStatsCorner.SelectedIndex;
            working.HoldOn = ticks[HOLD].Checked;
            if (cmbHoldWith.SelectedIndex == 0) working.HoldWith = 0;
            return working;
        }

        // Null when what is chosen can be saved; otherwise what to do first.
        public static string Problem(ExtrasSettings s, bool anotherKey)
        {
            if (s.ZoomOn && s.ZoomKey == 0) return "Choose the key that zooms, or untick zoom.";
            if (s.HoldOn && anotherKey && s.HoldWith == 0) return "Choose the key that switches the held key, or pick Tapping that key.";
            if (s.HoldOn && s.HoldWith != 0 && s.HoldWith == s.HoldKey) return "That is the held key itself. Pick Tapping that key instead.";
            byte trigger = s.HoldOn ? (s.HoldWith != 0 ? s.HoldWith : s.HoldKey) : (byte)0;
            if (s.ZoomOn && trigger != 0 && trigger == s.ZoomKey) return "Zoom and the held key use the same key. Choose another for one of them.";
            if (trigger != 0 && trigger == s.CrossKey) return MacroKeys.Name(trigger) + " already switches the crosshair. Choose another.";
            if (s.ZoomOn && s.ZoomKey == s.CrossKey) return MacroKeys.Name(s.ZoomKey) + " already switches the crosshair. Choose another.";
            return null;
        }

        void Save()
        {
            ExtrasSettings s = Collect();
            string why = Problem(s, cmbHoldWith.SelectedIndex == 1);
            if (why != null) { lblProblem.Text = why; return; }
            // The crosshair's part is left as its own window last saved it.
            ExtrasSettings latest = Extras.Current;
            s.Cross = latest.Cross; s.CrossOn = latest.CrossOn; s.CrossKey = latest.CrossKey; s.Only = latest.Only;
            Extras.Apply(s);
            if (shell != null)
            {
                List<string> on = new List<string>();
                if (s.KeysOn) on.Add("keys and clicks");
                if (s.ZoomOn) on.Add("zoom on " + MacroKeys.Name(s.ZoomKey));
                if (s.DimOn) on.Add("dim");
                if (s.PingOn) on.Add("ping");
                if (s.MemoryOn) on.Add("memory and processor");
                if (s.HoldOn) on.Add("hold " + MacroKeys.Name(s.HoldKey));
                shell.Say(on.Count == 0 ? "On-screen extras: all off." : "On-screen extras on: " + string.Join(", ", on.ToArray()) + ". They show while a Roblox window is in front.");
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
