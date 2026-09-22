using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Hunt mode: which accounts, which game, how long in each server - and
    // where each account is up to while it runs. The hunt itself runs in the
    // main window, so closing this leaves it going.
    class HuntDialog : Form
    {
        const int W = 560;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int SET_H = 300;
        const int NOW_Y = TOP + SET_H + 12;
        const int NOW_H = 130;
        const int FOOT_Y = NOW_Y + NOW_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int INNER = W - 24 - Ui.PAD * 2;
        const int FIELD_X = 110;

        readonly WatchKit kit;
        readonly List<ThemedCheckBox> accountChecks = new List<ThemedCheckBox>();
        TextBox gameBox;
        ThemedNumeric numLook;
        ThemedCheckBox chkStay;
        Label lblLines, lblProblem;
        Button btnGo;
        System.Windows.Forms.Timer refresh;

        public HuntDialog(WatchKit kit)
        {
            this.kit = kit;
            WatchUi.Frame(this, "Hunt", W, H);
            Build();
            Fill(kit.Store.Hunt);
            RefreshLive();

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 1000;
            refresh.Tick += delegate { RefreshLive(); };
            refresh.Start();
            FormClosed += delegate { refresh.Stop(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && refresh != null) refresh.Dispose();
            base.Dispose(disposing);
        }

        public int AccountChoices { get { return accountChecks.Count; } }
        public string ProblemText { get { return lblProblem.Text; } }
        public string LinesShown { get { return lblLines.Text; } }
        public string ButtonText { get { return btnGo.Text; } }

        public void SetGame(string link) { gameBox.Text = link; }
        public void SetLook(int seconds) { numLook.Value = seconds; }

        public void TickAccount(string name, bool on)
        {
            foreach (ThemedCheckBox c in accountChecks)
                if (string.Equals((string)c.Tag, name, StringComparison.OrdinalIgnoreCase)) c.Checked = on;
        }

        public bool IsTicked(string name)
        {
            foreach (ThemedCheckBox c in accountChecks)
                if (string.Equals((string)c.Tag, name, StringComparison.OrdinalIgnoreCase)) return c.Checked;
            return false;
        }

        // ---------- layout ----------

        void Build()
        {
            Card set = WatchUi.CardAt(this, "HUNT", 12, TOP, W - 24, SET_H);
            set.Controls.Add(Ui.RowLabel("Game", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
            gameBox = WatchUi.Input(FIELD_X, 42, INNER - FIELD_X + Ui.PAD);
            set.Controls.Add(gameBox);

            set.Controls.Add(Ui.RowLabel("Look for", Ui.PAD, 74, 26, 80, 9f, Theme.Muted));
            numLook = Ui.DarkNumeric(FIELD_X, 74, 64, 10, 600, 60);
            set.Controls.Add(numLook);
            set.Controls.Add(Ui.RowLabel("seconds in each server", FIELD_X + 72, 74, 26, 200, 9f, Theme.Muted));

            chkStay = Ui.DarkCheck("Stay in a server when a watcher finds something there", Ui.PAD, 106, 9f);
            chkStay.AutoSize = true;
            set.Controls.Add(chkStay);

            set.Controls.Add(Ui.CaptionLabel("ACCOUNTS", Ui.PAD, 138));
            ScrollPanel list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 156);
            list.Size = new Size(INNER, 82);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            set.Controls.Add(list);

            IList<string> names = kit.Accounts == null ? null : kit.Accounts();
            int y = 0;
            if (names != null)
                foreach (string n in names)
                {
                    ThemedCheckBox c = Ui.DarkCheck(n, 2, y, 9f);
                    c.ForeColor = Theme.Text;
                    c.Tag = n;
                    list.Controls.Add(c);
                    accountChecks.Add(c);
                    y += 22;
                }
            if (accountChecks.Count == 0)
                list.Controls.Add(Ui.MutedLabel("No accounts saved yet - add them in Accounts on the main window.", 2, 2, 8.25f));

            Label hint = Ui.MutedLabel(
                "Each account's client is closed and opened again in the next server, so only accounts "
                + "saved in Accounts can hunt. The watchers of the setup in use do the looking.", Ui.PAD, 246, 8.25f);
            hint.MaximumSize = new Size(INNER, 0);
            set.Controls.Add(hint);

            Card now = WatchUi.CardAt(this, "NOW", 12, NOW_Y, W - 24, NOW_H);
            lblLines = Ui.MutedLabel("", Ui.PAD, 40, 9f);
            lblLines.MaximumSize = new Size(INNER, NOW_H - 48);
            now.Controls.Add(lblLines);

            // In the card rather than the footer: a reason is often two lines,
            // and the footer's buttons leave room for less than one.
            lblProblem = Ui.MutedLabel("", Ui.PAD, NOW_H - 46, 8.25f);
            lblProblem.ForeColor = Theme.Amber;
            lblProblem.MaximumSize = new Size(INNER, 40);
            now.Controls.Add(lblProblem);

            btnGo = WatchUi.Primary("Start hunting", W - 12 - 110 - 8 - 130, FOOT_Y, 130, 32);
            btnGo.Click += delegate { StartOrStop(); };
            Controls.Add(btnGo);
            Button close = WatchUi.Secondary("Close", W - 12 - 110, FOOT_Y, 110, 32);
            close.BackColor = Theme.Card;
            close.FlatAppearance.MouseOverBackColor = Theme.Inset;
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }

        void Fill(HuntSettings s)
        {
            gameBox.Text = s.GameLink ?? "";
            numLook.Value = Math.Max(10, Math.Min(600, s.LookSeconds));
            chkStay.Checked = s.StayWhenFound;
            if (s.Accounts != null) foreach (string a in s.Accounts) TickAccount(a, true);
        }

        HuntSettings Collect()
        {
            HuntSettings s = new HuntSettings();
            s.GameLink = gameBox.Text.Trim();
            s.LookSeconds = numLook.Value;
            s.StayWhenFound = chkStay.Checked;
            List<string> picked = new List<string>();
            foreach (ThemedCheckBox c in accountChecks) if (c.Checked) picked.Add((string)c.Tag);
            s.Accounts = picked.ToArray();
            return s;
        }

        void RefreshLive()
        {
            bool running = kit.Hunts != null && kit.Hunts.Running;
            string go = running ? "Stop hunting" : "Start hunting";
            if (btnGo.Text != go) btnGo.Text = go;

            IList<string> lines = kit.Hunts == null ? null : kit.Hunts.Lines(DateTime.Now);
            string text = lines == null || lines.Count == 0
                ? "Not hunting. Start moves each ticked account to a server of the game, looks for as long "
                  + "as set above, and moves on."
                : string.Join("\r\n", new List<string>(lines).ToArray());
            if (lblLines.Text != text) lblLines.Text = text;
        }

        // Null when it started or stopped; otherwise why not, also shown.
        public string StartOrStop()
        {
            lblProblem.Text = "";
            if (kit.Hunts == null) return Problem("Hunting runs from the main window.");
            if (kit.Hunts.Running)
            {
                kit.Hunts.Stop();
                RefreshLive();
                return null;
            }

            HuntSettings s = Collect();
            kit.Store.Hunt = s;
            kit.Store.Save();
            string why = kit.Hunts.Start(s);
            RefreshLive();
            return why == null ? null : Problem(why);
        }

        string Problem(string why)
        {
            lblProblem.Text = why;
            return why;
        }
    }
}
