using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The autoclicker: what it clicks, how fast, on which clients, and the
    // key that switches it.
    //
    // Written for someone who has never used one. Every choice is a list of
    // plain answers, a place is chosen by clicking it on a picture of the
    // game, and a row is only there while the answer above it makes it mean
    // something - so the window is as short as what was asked for.
    class AutoClickDialog : Form
    {
        const int W = 540;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int CARD_W = W - 24;
        const int FIELD_X = 150;
        const int INNER = CARD_W - Ui.PAD * 2;
        const int ROW_H = 34;
        const int WHO_H = 74;

        // The speeds most people want, by name. Anything else is "my own".
        static readonly int[] SpeedMs = { 1000, 200, 100, 50 };
        static readonly string[] SpeedNames = { "Slow - 1 click a second", "Normal - 5 a second",
                                                "Fast - 10 a second", "Very fast - 20 a second",
                                                "My own speed" };

        readonly WatchKit kit;
        readonly IMacroHost host;
        readonly List<ThemedCheckBox> ticks = new List<ThemedCheckBox>();
        Card card;
        Panel rowTop, rowPlace, rowPicture, rowOn, rowTurns, rowKey;
        ThemedPicker cmbButton, cmbSpeed, cmbWhere, cmbOn;
        TextBox everyBox, turnBox;
        Label lblEvery, lblSeconds, lblPlace, lblHint, lblProblem, lblHow;
        ThemedNumeric numAcross, numDown;
        PictureRows picture;
        Button btnKey, btnSwitch, btnClose, btnSave;
        byte key;
        bool saved, building = true;

        public AutoClickDialog(WatchKit kit)
        {
            this.kit = kit;
            host = kit.Macros;
            key = host.Clicker.ToggleVk;
            WatchUi.Frame(this, "Autoclicker", W, 400);
            Build();
            building = false;
            Reflow();
            ShowState();
            FormClosed += delegate { picture.Tidy(saved); };
        }

        public string ProblemText { get { return lblProblem.Text; } }

        // Which of the rows that come and go are showing, for tests.
        // Kept here rather than asked of the controls: a control says it is
        // not visible for as long as its window has not been shown.
        public bool ShowsPlace { get; private set; }
        public bool ShowsPicture { get; private set; }
        public bool ShowsTurns { get; private set; }
        public bool ShowsOwnSpeed { get; private set; }

        // For tests: the answers, as if chosen from their lists.
        public void ChooseWhere(ClickWhere where) { cmbWhere.SelectedIndex = (int)where; Reflow(); }
        public void ChooseOn(ClickOn on) { cmbOn.SelectedIndex = (int)on; Reflow(); }
        public void ChooseSpeed(int index) { cmbSpeed.SelectedIndex = index; Reflow(); }
        public int SpeedChoices { get { return cmbSpeed.Items.Count; } }

        // ---------- layout ----------

        Panel Row(int height)
        {
            Panel p = new Panel();
            p.Size = new Size(CARD_W, height);
            p.BackColor = Theme.Card;
            card.Controls.Add(p);
            return p;
        }

        void Build()
        {
            AutoClick c = host.Clicker;
            card = WatchUi.CardAt(this, "AUTOCLICKER", 12, TOP, CARD_W, 300);

            lblHow = Ui.MutedLabel("", Ui.PAD, 38, 8.25f);
            lblHow.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(lblHow);

            // Always there: which button, how fast, where.
            rowTop = Row(ROW_H * 3);
            rowTop.Controls.Add(Ui.RowLabel("Mouse button", Ui.PAD, 0, 26, 120, 9f, Theme.Muted));
            cmbButton = Ui.DarkCombo(FIELD_X, 0, 130);
            cmbButton.Items.Add("Left");
            cmbButton.Items.Add("Right");
            cmbButton.SelectedIndex = c.Right ? 1 : 0;
            rowTop.Controls.Add(cmbButton);

            rowTop.Controls.Add(Ui.RowLabel("How fast", Ui.PAD, ROW_H, 26, 120, 9f, Theme.Muted));
            cmbSpeed = Ui.DarkCombo(FIELD_X, ROW_H, 200);
            foreach (string name in SpeedNames) cmbSpeed.Items.Add(name);
            int speed = Array.IndexOf(SpeedMs, c.EveryMs);
            cmbSpeed.SelectedIndex = speed < 0 ? SpeedMs.Length : speed;
            cmbSpeed.SelectedIndexChanged += delegate { Reflow(); };
            rowTop.Controls.Add(cmbSpeed);
            lblEvery = Ui.RowLabel("every", FIELD_X + 208, ROW_H, 26, 40, 9f, Theme.Muted);
            rowTop.Controls.Add(lblEvery);
            everyBox = WatchUi.Input(FIELD_X + 250, ROW_H + 2, 50);
            everyBox.Text = MacroStep.Seconds(c.EveryMs).TrimEnd('s');
            rowTop.Controls.Add(everyBox);
            lblSeconds = Ui.RowLabel("sec", FIELD_X + 304, ROW_H, 26, 30, 9f, Theme.Muted);
            rowTop.Controls.Add(lblSeconds);

            rowTop.Controls.Add(Ui.RowLabel("Click where", Ui.PAD, ROW_H * 2, 26, 120, 9f, Theme.Muted));
            cmbWhere = Ui.DarkCombo(FIELD_X, ROW_H * 2, 280);
            cmbWhere.Items.Add("Wherever my mouse is");
            cmbWhere.Items.Add("One spot I pick");
            cmbWhere.Items.Add("On a picture, wherever it appears");
            cmbWhere.SelectedIndex = (int)c.Where;
            cmbWhere.SelectedIndexChanged += delegate { Reflow(); };
            rowTop.Controls.Add(cmbWhere);

            // One spot: picked on a picture of the game. The numbers are
            // still there for anyone who wants them exact.
            rowPlace = Row(66);
            rowPlace.Controls.Add(Ui.RowLabel("The spot", Ui.PAD, 0, 28, 120, 9f, Theme.Muted));
            Button pick = WatchUi.Primary("Pick it on the game", FIELD_X, 0, 160, 28);
            pick.Click += delegate
            {
                PlacePick.Ask(this, kit, pick, false, new double[] { numAcross.Value / 100.0, numDown.Value / 100.0 },
                              delegate(double[] at) { ChoosePlace(at[0], at[1]); },
                              delegate(string why) { Say(why, true); });
            };
            rowPlace.Controls.Add(pick);
            lblPlace = Ui.RowLabel("", FIELD_X + 170, 0, 28, INNER - FIELD_X - 170 + Ui.PAD, 8.25f, Theme.Muted);
            rowPlace.Controls.Add(lblPlace);
            rowPlace.Controls.Add(Ui.RowLabel("or type it", Ui.PAD, 34, 26, 120, 8.25f, Theme.Muted));
            numAcross = Ui.DarkNumeric(FIELD_X, 34, 60, 0, 100, (int)Math.Round(c.X * 100));
            rowPlace.Controls.Add(numAcross);
            rowPlace.Controls.Add(Ui.RowLabel("% across", FIELD_X + 66, 34, 26, 70, 8.25f, Theme.Muted));
            numDown = Ui.DarkNumeric(FIELD_X + 140, 34, 60, 0, 100, (int)Math.Round(c.Y * 100));
            rowPlace.Controls.Add(numDown);
            rowPlace.Controls.Add(Ui.RowLabel("% down", FIELD_X + 206, 34, 26, 70, 8.25f, Theme.Muted));
            numAcross.ValueChanged += delegate { ShowPlace(); };
            numDown.ValueChanged += delegate { ShowPlace(); };

            rowPicture = Row(PictureRows.HEIGHT);
            picture = new PictureRows(this, kit, rowPicture, 0, FIELD_X, INNER, c.Picture, c.Mask, c.Tolerance, c.Box);

            rowOn = Row(ROW_H);
            rowOn.Controls.Add(Ui.RowLabel("Which windows", Ui.PAD, 0, 26, 120, 9f, Theme.Muted));
            cmbOn = Ui.DarkCombo(FIELD_X, 0, 280);
            cmbOn.Items.Add("The Roblox window I'm in");
            cmbOn.Items.Add("Several windows, one after another");
            cmbOn.SelectedIndex = (int)c.On;
            cmbOn.SelectedIndexChanged += delegate { Reflow(); };
            rowOn.Controls.Add(cmbOn);

            rowTurns = Row(ROW_H + WHO_H + 26);
            rowTurns.Controls.Add(Ui.RowLabel("Each one gets", Ui.PAD, 0, 26, 120, 9f, Theme.Muted));
            turnBox = WatchUi.Input(FIELD_X, 2, 50);
            turnBox.Text = c.TurnSeconds.ToString();
            rowTurns.Controls.Add(turnBox);
            rowTurns.Controls.Add(Ui.RowLabel("seconds, then the next one", FIELD_X + 58, 0, 26, 220, 9f, Theme.Muted));
            rowTurns.Controls.Add(Ui.RowLabel("Which ones", Ui.PAD, ROW_H, 26, 120, 9f, Theme.Muted));
            ScrollPanel who = new ScrollPanel();
            who.Location = new Point(FIELD_X, ROW_H);
            who.Size = new Size(INNER - FIELD_X + Ui.PAD, WHO_H);
            who.BackColor = Theme.Card;
            who.AutoScroll = true;
            rowTurns.Controls.Add(who);
            IList<string> names = kit.Accounts == null ? new List<string>() : kit.Accounts();
            int y = 0;
            foreach (string name in names)
            {
                ThemedCheckBox t = Ui.DarkCheck(name, 0, y, 9f);
                t.AutoSize = true;
                t.Tag = name;
                if (c.Accounts != null)
                    foreach (string a in c.Accounts)
                        if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) t.Checked = true;
                who.Controls.Add(t);
                ticks.Add(t);
                y += 24;
            }
            rowTurns.Controls.Add(Ui.MutedLabel(
                names.Count == 0 ? "Every open Roblox window."
                                 : "Tick none to use every open Roblox window.",
                FIELD_X, ROW_H + WHO_H + 4, 8.25f));

            rowKey = Row(ROW_H + 2);
            rowKey.Controls.Add(Ui.RowLabel("On / off key", Ui.PAD, 0, 28, 120, 9f, Theme.Muted));
            btnKey = WatchUi.Secondary(MacroKeys.Name(key), FIELD_X, 0, 90, 28);
            btnKey.Click += delegate { ChooseKey(); };
            rowKey.Controls.Add(btnKey);
            rowKey.Controls.Add(Ui.RowLabel("Click the button to change it", FIELD_X + 98, 0, 28, 220, 8.25f, Theme.Muted));

            lblHint = Ui.MutedLabel("", Ui.PAD, 0, 8.25f);
            lblHint.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(lblHint);

            lblProblem = Ui.RowLabel("", 16, 0, 32, W - 16 - 330, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);
            btnSwitch = WatchUi.Secondary("Switch on", W - 12 - 100 - 8 - 90 - 8 - 100, 0, 100, 32);
            btnSwitch.Click += delegate { Switch(); };
            Controls.Add(btnSwitch);
            btnClose = WatchUi.Secondary("Close", W - 12 - 100 - 8 - 90, 0, 90, 32);
            btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnClose);
            btnSave = WatchUi.Primary("Save", W - 12 - 100, 0, 100, 32);
            btnSave.Click += delegate { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(btnSave);
            CancelButton = btnClose;

            ShowPlace();
        }

        // Shows the rows the answers call for, closes the gaps the others
        // leave, and makes the window fit.
        void Reflow()
        {
            if (building) return;
            bool ownSpeed = cmbSpeed.SelectedIndex < 0 || cmbSpeed.SelectedIndex >= SpeedMs.Length;
            ShowsOwnSpeed = ownSpeed;
            lblEvery.Visible = everyBox.Visible = lblSeconds.Visible = ownSpeed;
            if (!ownSpeed) everyBox.Text = MacroStep.Seconds(SpeedMs[cmbSpeed.SelectedIndex]).TrimEnd('s');

            bool turns = cmbOn.SelectedIndex == (int)ClickOn.TakeTurns;
            ShowsPlace = cmbWhere.SelectedIndex == (int)ClickWhere.Place;
            ShowsPicture = cmbWhere.SelectedIndex == (int)ClickWhere.Picture;
            ShowsTurns = turns;
            rowPlace.Visible = ShowsPlace;
            rowPicture.Visible = ShowsPicture;
            rowTurns.Visible = turns;

            string k = MacroKeys.Name(key);
            lblHow.Text = turns
                ? "Set it up and press Save. Then press " + k + " (or Switch on) to start, and " + k + " again to stop."
                : "Set it up and press Save. Then click into Roblox and press " + k + " to start. Press " + k + " again to stop.";
            lblHint.Text = turns
                ? "Each Roblox window comes to the front for its turn, so this takes over your PC while it runs. "
                  + "Use it when you're away. It needs a spot or a picture to click."
                : "It only clicks the Roblox window you're in, and stops if you switch to another window. "
                  + "Some games ignore fast clicks. If clicks go missing, pick a slower speed.";

            int y = lblHow.Bottom + 10;
            Panel[] rows = { rowTop, rowPlace, rowPicture, rowOn, rowTurns, rowKey };
            bool[] shown = { true, ShowsPlace, ShowsPicture, true, turns, true };
            for (int i = 0; i < rows.Length; i++)
            {
                if (!shown[i]) continue;
                rows[i].Location = new Point(0, y);
                y += rows[i].Height;
            }
            lblHint.Location = new Point(Ui.PAD, y + 4);
            card.Height = lblHint.Bottom + 14;

            int foot = card.Bottom + 12;
            lblProblem.Top = btnSwitch.Top = btnClose.Top = btnSave.Top = foot;
            ClientSize = new Size(W, foot + 32 + 12);
            Invalidate();
        }

        void ShowPlace()
        {
            lblPlace.Text = PlacePick.Describe(numAcross.Value / 100.0, numDown.Value / 100.0);
        }

        // The spot, as picked on the picture.
        public void ChoosePlace(double fx, double fy)
        {
            numAcross.Value = (int)Math.Round(fx * 100);
            numDown.Value = (int)Math.Round(fy * 100);
            exactX = fx; exactY = fy; exact = true;
            ShowPlace();
            Say("", false);
        }

        // A picked spot is kept to the pixel; the boxes only show whole
        // percents, and typing in them takes over again.
        double exactX, exactY;
        bool exact;

        void Say(string text, bool warn)
        {
            lblProblem.Text = text;
            lblProblem.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        void ShowState()
        {
            btnSwitch.Text = host.ClickerOn ? "Switch off" : "Switch on";
        }

        void ChooseKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                key = d.Captured;
                btnKey.Text = MacroKeys.Name(key);
                Reflow();
            }
        }

        // ---------- saving ----------

        // The form as an autoclicker. Null, and the reason shown, when it
        // can't be one.
        public AutoClick Collect()
        {
            AutoClick c = host.Clicker.Copy();
            c.Right = cmbButton.SelectedIndex == 1;
            c.Where = (ClickWhere)Math.Max(0, cmbWhere.SelectedIndex);
            c.X = numAcross.Value / 100.0;
            c.Y = numDown.Value / 100.0;
            // The picked spot itself, unless the boxes were changed after.
            if (exact && (int)Math.Round(exactX * 100) == numAcross.Value && (int)Math.Round(exactY * 100) == numDown.Value)
            {
                c.X = exactX;
                c.Y = exactY;
            }
            c.Picture = picture.Picture;
            c.Mask = picture.Mask;
            c.Tolerance = picture.ChosenTolerance;
            c.Box = picture.Box;
            c.On = (ClickOn)Math.Max(0, cmbOn.SelectedIndex);
            c.ToggleVk = key;

            List<string> who = new List<string>();
            foreach (ThemedCheckBox t in ticks) if (t.Checked) who.Add((string)t.Tag);
            c.Accounts = who.Count == 0 ? null : who.ToArray();

            string problem = null;
            if (cmbSpeed.SelectedIndex >= 0 && cmbSpeed.SelectedIndex < SpeedMs.Length) c.EveryMs = SpeedMs[cmbSpeed.SelectedIndex];
            else
            {
                int ms;
                problem = MacroForm.ParseSeconds(everyBox.Text, out ms);
                if (problem == null) c.EveryMs = ms;
            }
            if (problem == null && c.On == ClickOn.TakeTurns)
            {
                int turn;
                if (!int.TryParse(turnBox.Text.Trim(), out turn) || turn < 1 || turn > 600)
                    problem = "Give each window between 1 and 600 seconds.";
                else c.TurnSeconds = turn;
            }
            if (problem == null) problem = c.Problem();
            if (problem == null && key == host.StopKey)
                problem = MacroKeys.Name(key) + " is the key that stops macros. Choose another.";
            if (problem == null && key == MacroRecording.STOP_KEY)
                problem = "F8 ends a recording. Choose another.";
            // And everything else that has a key: the screenshot key, the
            // rules' keys, every macro's own key.
            if (problem == null) problem = host.KeyUseProblem(key, KeyUse.CLICKER, null);

            Say(problem ?? "", true);
            return problem == null ? c : null;
        }

        public bool TrySave()
        {
            AutoClick c = Collect();
            if (c == null) return false;
            AutoClick into = host.Clicker;
            into.Right = c.Right; into.EveryMs = c.EveryMs; into.Where = c.Where; into.X = c.X; into.Y = c.Y;
            into.Picture = c.Picture; into.Mask = c.Mask; into.Tolerance = c.Tolerance; into.Box = c.Box;
            into.On = c.On; into.Accounts = c.Accounts;
            into.TurnSeconds = c.TurnSeconds; into.ToggleVk = c.ToggleVk;
            // Saved once, it is set up: from now its key is listened for.
            into.Enabled = true;
            host.SaveClicker();
            // The settings name these files now, whatever is cut after.
            picture.Keep();
            saved = true;
            return true;
        }

        // Saved first, so what is switched on is what the window shows.
        void Switch()
        {
            if (!host.ClickerOn && !TrySave()) return;
            string problem = host.ToggleClicker();
            // On the window in front, this says to use the key instead: the
            // window in front just now is this one.
            Say(problem == null ? "" : "Saved. Now " + problem, problem != null);
            ShowState();
        }
    }
}
