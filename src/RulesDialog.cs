using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The rules behind the rule editor, kept apart so they can be tested.
    static class RuleForm
    {
        // "The client it happened on" means nothing to a timer or a key.
        public static List<RuleOn> OnChoices(RuleWhen when)
        {
            List<RuleOn> on = new List<RuleOn>();
            if (when == RuleWhen.WatcherFinds || when == RuleWhen.Joins) on.Add(RuleOn.WhereItHappened);
            on.Add(RuleOn.InFront);
            on.Add(RuleOn.EveryClient);
            on.Add(RuleOn.TheseAccounts);
            return on;
        }

        public static string OnName(RuleOn on, RuleWhen when)
        {
            switch (on)
            {
                case RuleOn.InFront: return "The client in front";
                case RuleOn.EveryClient: return "Every client";
                case RuleOn.TheseAccounts: return "These accounts";
                default: return when == RuleWhen.Joins ? "The client that joined" : "The client that saw it";
            }
        }

        // Function keys and the number pad: keys a game rarely needs, so a
        // rule on one doesn't get in the way of playing. Not F8, which ends a
        // recording.
        public static List<byte> HotkeyChoices()
        {
            List<byte> keys = new List<byte>();
            for (byte vk = 0x70; vk <= 0x7B; vk++) if (vk != MacroRecording.STOP_KEY) keys.Add(vk);
            for (byte vk = 0x60; vk <= 0x69; vk++) keys.Add(vk);
            return keys;
        }

        public static readonly string[] WhenNames =
        {
            "A watcher finds something",
            "Every so often",
            "I press a key",
            "A client joins a server"
        };
    }

    // The chosen setup's rules, to add, switch on and off, edit and remove.
    class RulesDialog : Form
    {
        const int W = 720;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int LIST_H = 420;
        const int FOOT_Y = TOP + LIST_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW = 46;

        readonly WatchKit kit;
        ScrollPanel list;
        Label empty;
        readonly List<Label> rowNames = new List<Label>();

        public RulesDialog(WatchKit kit)
        {
            this.kit = kit;
            WatchUi.Frame(this, "Rules", W, H);
            Build();
            Rebuild();
        }

        public int RowCount { get { return kit.Store.Rules.Count; } }

        void Build()
        {
            Card card = WatchUi.CardAt(this, "RULES", 12, TOP, W - 24, LIST_H);
            Label setup = Ui.RowLabel("In the setup " + kit.Store.Chosen.Name, 300, 10, 26, W - 24 - 300 - Ui.PAD, 8.25f, Theme.Muted);
            setup.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(setup);

            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 44);
            list.Size = new Size(W - 24 - Ui.PAD * 2, LIST_H - 44 - 56);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);

            empty = Ui.MutedLabel(
                "No rules in this setup yet. A rule plays a macro when something happens: a watcher finds "
                + "something, a timer comes round, you press a key, or a client joins a server - on the clients "
                + "you choose, as often as you allow.", 4, 8, 8.25f);
            empty.MaximumSize = new Size(list.Width - 16, 0);

            Button add = WatchUi.Primary("Add rule", Ui.PAD, LIST_H - 44, 130, 30);
            add.Click += delegate { Add(); };
            card.Controls.Add(add);
            Label note = Ui.RowLabel("A watcher can also play a macro on its own - Then play, in its editor.",
                                     170, LIST_H - 44, 30, W - 24 - 170 - Ui.PAD, 8.25f, Theme.Muted);
            note.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(note);

            Button close = WatchUi.Primary("Close", W - 12 - 110, FOOT_Y, 110, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }

        void Rebuild()
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, empty)) c.Dispose();
            }
            rowNames.Clear();
            IList<Rule> rules = kit.Store.Rules;
            if (rules.Count == 0) list.Controls.Add(empty);

            int y = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                int index = i;
                Rule r = rules[i];
                list.Controls.Add(RowGrip.For(i, rules.Count, y, ROW, 0, MoveAt, CopyAt));
                ThemedCheckBox on = Ui.DarkCheck("", 18, y, 9f);
                on.Checked = r.Enabled;
                Ui.CenterIn(on, y, ROW);
                on.CheckedChanged += delegate { SetEnabled(index, on.Checked); };
                list.Controls.Add(on);

                Label name = Ui.RowLabel(r.Name, 46, y + 3, 20, 484, 9.5f, r.Enabled ? Theme.Text : Theme.Muted);
                name.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                list.Controls.Add(name);
                rowNames.Add(name);

                string problem = r.Problem();
                if (problem == null && kit.Store.FindMacro(r.Macro) == null)
                    problem = "there is no macro called " + r.Macro + " any more - choose another.";
                Label line = Ui.RowLabel(problem != null ? "Needs attention: " + problem : r.Describe(),
                                         46, y + 23, 20, 504, 8.25f, problem != null ? Theme.Amber : Theme.Muted);
                line.AutoEllipsis = true;
                list.Controls.Add(line);

                LinkLabel edit = Ui.RowLink("Edit", 560, y, ROW, 9f);
                edit.Click += delegate { Edit(index); };
                list.Controls.Add(edit);
                LinkLabel remove = Ui.RowLink("Remove", 600, y, ROW, 9f);
                remove.Click += delegate { AskRemove(index); };
                list.Controls.Add(remove);
                y += ROW;
            }
            list.ResumeLayout();
        }

        void Commit(string said)
        {
            kit.Store.Save();
            kit.Say(said);
        }

        public void AddRule(Rule r)
        {
            kit.Store.Rules.Add(r);
            Commit("Rule " + r.Name + " added.");
            Rebuild();
        }

        public void ReplaceAt(int index, Rule r)
        {
            kit.Store.Rules[index] = r;
            Commit("Rule " + r.Name + " saved.");
            Rebuild();
        }

        // Every rule is checked on every event, so order is only how they read.
        public void MoveAt(int from, int to)
        {
            if (!RowOrder.Move(kit.Store.Rules, from, to)) return;
            kit.Store.Save();
            Rebuild();
        }

        public void CopyAt(int index)
        {
            Rule was = kit.Store.Rules[index];
            Rule copy = was.Copy();
            copy.Name = RowOrder.CopyName(was.Name, delegate(string n)
            {
                foreach (Rule r in kit.Store.Rules)
                    if (string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            });
            kit.Store.Rules.Insert(index + 1, copy);
            Commit("Rule " + was.Name + " copied as " + copy.Name + ".");
            Rebuild();
        }

        public void RemoveAt(int index)
        {
            string name = kit.Store.Rules[index].Name;
            kit.Store.Rules.RemoveAt(index);
            Commit("Rule " + name + " removed.");
            Rebuild();
        }

        // In place: rules are only read on this thread. Not rebuilt, because
        // this runs inside the tick box's own event.
        public void SetEnabled(int index, bool on)
        {
            Rule r = kit.Store.Rules[index];
            if (r.Enabled == on) return;
            r.Enabled = on;
            Commit("Rule " + r.Name + (on ? " is on again." : " switched off."));
            if (index < rowNames.Count) rowNames[index].ForeColor = on ? Theme.Text : Theme.Muted;
        }

        void Add()
        {
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), kit))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) AddRule(d.Result);
        }

        void Edit(int index)
        {
            using (RuleEditDialog d = new RuleEditDialog(kit.Store.Rules[index], kit))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) ReplaceAt(index, d.Result);
        }

        void AskRemove(int index)
        {
            if (MessageBox.Show(this, "Remove the rule " + kit.Store.Rules[index].Name + "?", "Remove rule",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RemoveAt(index);
        }
    }

    // One rule: when, what, on which clients, and how often.
    class RuleEditDialog : Form
    {
        const int W = 784;
        const int COL_W = 374;
        const int X1 = 12, X2 = 12 + COL_W + 12;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int TOP_H = 290;
        const int LOW_Y = TOP + TOP_H + 12;
        const int LOW_H = 100;
        const int FOOT_Y = LOW_Y + LOW_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW_H = 26;
        const int FIELD_X = 120;
        const int FIELD_W = COL_W - FIELD_X - Ui.PAD;
        const int INNER = COL_W - Ui.PAD * 2;

        readonly Rule original;
        readonly WatchKit kit;
        readonly List<string> watcherNames = new List<string>();
        readonly List<string> macroNames = new List<string>();
        readonly List<byte> keys = RuleForm.HotkeyChoices();
        readonly List<RuleOn> onChoices = new List<RuleOn>();
        readonly List<ThemedCheckBox> accountChecks = new List<ThemedCheckBox>();

        TextBox nameBox, foundBox;
        ThemedPicker cmbWhen, cmbWatcher, cmbUnit, cmbKey, cmbMacro, cmbOn;
        ThemedNumeric numEvery, numTimes, numDelay, numGap;
        ThemedCheckBox chkCtrl, chkAlt, chkShift, chkAway;
        Panel watcherPanel, everyPanel, keyPanel, joinPanel;
        ScrollPanel accountsList;
        Label lblProblem;

        public Rule Result { get; private set; }

        public RuleEditDialog(Rule r, WatchKit kit)
        {
            original = r.Copy();
            this.kit = kit;
            WatchUi.Frame(this, string.IsNullOrEmpty(r.Name) ? "New rule" : "Edit " + r.Name, W, H);
            Build();
            Fill(original);
        }

        public string ProblemText { get { return lblProblem.Text; } }

        // ---------- layout ----------

        void Build()
        {
            Card when = WatchUi.CardAt(this, "WHEN", X1, TOP, COL_W, TOP_H);
            when.Controls.Add(Ui.RowLabel("Name", Ui.PAD, 44, ROW_H, 90, 9f, Theme.Muted));
            nameBox = WatchUi.Input(FIELD_X, 46, FIELD_W);
            when.Controls.Add(nameBox);
            when.Controls.Add(Ui.RowLabel("When", Ui.PAD, 78, ROW_H, 90, 9f, Theme.Muted));
            cmbWhen = Ui.DarkCombo(FIELD_X, 78, FIELD_W);
            foreach (string n in RuleForm.WhenNames) cmbWhen.Items.Add(n);
            cmbWhen.SelectedIndexChanged += delegate { ShowWhen(); };
            when.Controls.Add(cmbWhen);

            watcherPanel = WhenPanel(when);
            watcherPanel.Controls.Add(Ui.RowLabel("Watcher", Ui.PAD, 0, ROW_H, 90, 9f, Theme.Muted));
            cmbWatcher = Ui.DarkCombo(FIELD_X, 0, FIELD_W);
            watcherPanel.Controls.Add(cmbWatcher);
            watcherPanel.Controls.Add(Ui.RowLabel("Only if it found", Ui.PAD, 34, ROW_H, 100, 9f, Theme.Muted));
            foundBox = WatchUi.Input(FIELD_X, 36, FIELD_W);
            watcherPanel.Controls.Add(foundBox);
            Hint(watcherPanel, "Words that must be in what it found - an egg's name, say. Blank for anything it finds.", 68);

            everyPanel = WhenPanel(when);
            everyPanel.Controls.Add(Ui.RowLabel("Every", Ui.PAD, 0, ROW_H, 90, 9f, Theme.Muted));
            numEvery = Ui.DarkNumeric(FIELD_X, 0, 64, 1, 999, 5);
            everyPanel.Controls.Add(numEvery);
            cmbUnit = Ui.DarkCombo(FIELD_X + 72, 0, 110);
            cmbUnit.Items.Add("seconds");
            cmbUnit.Items.Add("minutes");
            cmbUnit.Items.Add("hours");
            everyPanel.Controls.Add(cmbUnit);
            Hint(everyPanel, "Over and over, for as long as this setup is in use. Tick \"only while I'm away\" below "
                           + "so it doesn't take the keyboard while you're using the PC.", 36);

            keyPanel = WhenPanel(when);
            keyPanel.Controls.Add(Ui.RowLabel("Key", Ui.PAD, 0, ROW_H, 90, 9f, Theme.Muted));
            cmbKey = Ui.DarkCombo(FIELD_X, 0, 110);
            foreach (byte vk in keys) cmbKey.Items.Add(MacroKeys.Name(vk));
            keyPanel.Controls.Add(cmbKey);
            chkCtrl = Ui.DarkCheck("Ctrl", FIELD_X, 34, 9f);
            chkAlt = Ui.DarkCheck("Alt", FIELD_X + 70, 34, 9f);
            chkShift = Ui.DarkCheck("Shift", FIELD_X + 130, 34, 9f);
            keyPanel.Controls.Add(chkCtrl);
            keyPanel.Controls.Add(chkAlt);
            keyPanel.Controls.Add(chkShift);
            Hint(keyPanel, "Heard wherever you are. With the client in front, press it with the Roblox window "
                         + "you want it played on in front.", 68);

            joinPanel = WhenPanel(when);
            Hint(joinPanel, "When a client arrives in a server - when it starts, after a hop, or after a teleport. "
                          + "Wait first, on the right, gives the game time to load.", 0);

            Card doCard = WatchUi.CardAt(this, "PLAY", X2, TOP, COL_W, TOP_H);
            doCard.Controls.Add(Ui.RowLabel("Macro", Ui.PAD, 44, ROW_H, 90, 9f, Theme.Muted));
            cmbMacro = Ui.DarkCombo(FIELD_X, 44, FIELD_W);
            doCard.Controls.Add(cmbMacro);
            doCard.Controls.Add(Ui.RowLabel("Times", Ui.PAD, 78, ROW_H, 90, 9f, Theme.Muted));
            numTimes = Ui.DarkNumeric(FIELD_X, 78, 56, 1, 20, 1);
            doCard.Controls.Add(numTimes);
            doCard.Controls.Add(Ui.RowLabel("in a row", FIELD_X + 64, 78, ROW_H, 100, 9f, Theme.Muted));
            doCard.Controls.Add(Ui.RowLabel("Wait first", Ui.PAD, 112, ROW_H, 90, 9f, Theme.Muted));
            numDelay = Ui.DarkNumeric(FIELD_X, 112, 64, 0, 600, 0);
            doCard.Controls.Add(numDelay);
            doCard.Controls.Add(Ui.RowLabel("seconds", FIELD_X + 72, 112, ROW_H, 100, 9f, Theme.Muted));
            doCard.Controls.Add(Ui.RowLabel("On", Ui.PAD, 146, ROW_H, 90, 9f, Theme.Muted));
            cmbOn = Ui.DarkCombo(FIELD_X, 146, FIELD_W);
            cmbOn.SelectedIndexChanged += delegate { accountsList.Visible = SelectedOn() == RuleOn.TheseAccounts; };
            doCard.Controls.Add(cmbOn);
            accountsList = new ScrollPanel();
            accountsList.Location = new Point(Ui.PAD, 180);
            accountsList.Size = new Size(INNER, 96);
            accountsList.BackColor = Theme.Card;
            accountsList.AutoScroll = true;
            doCard.Controls.Add(accountsList);

            Card limits = WatchUi.CardAt(this, "HOW OFTEN", X1, LOW_Y, W - 24, LOW_H);
            limits.Controls.Add(Ui.RowLabel("At most once every", Ui.PAD, 40, ROW_H, 130, 9f, Theme.Muted));
            numGap = Ui.DarkNumeric(Ui.PAD + 134, 40, 70, 0, 3600, 15);
            limits.Controls.Add(numGap);
            limits.Controls.Add(Ui.RowLabel("seconds on each client", Ui.PAD + 212, 40, ROW_H, 200, 9f, Theme.Muted));
            chkAway = Ui.DarkCheck("Only while I'm away from the keyboard - a minute without typing or moving the mouse",
                                   Ui.PAD, 70, 9f);
            chkAway.AutoSize = true;
            limits.Controls.Add(chkAway);

            lblProblem = Ui.RowLabel("", X1 + 4, FOOT_Y, 32, 520, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 116 - 8 - 96, FOOT_Y, 96, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button save = WatchUi.Primary("Save", W - 12 - 116, FOOT_Y, 116, 32);
            save.Click += delegate { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            CancelButton = cancel;
        }

        Panel WhenPanel(Card card)
        {
            Panel p = new Panel();
            p.Location = new Point(0, 116);
            p.Size = new Size(COL_W, TOP_H - 124);
            p.BackColor = Theme.Card;
            card.Controls.Add(p);
            return p;
        }

        static void Hint(Panel p, string text, int y)
        {
            Label l = Ui.MutedLabel(text, Ui.PAD, y, 8.25f);
            l.MaximumSize = new Size(INNER, 0);
            p.Controls.Add(l);
        }

        // ---------- filling and collecting ----------

        void Fill(Rule r)
        {
            nameBox.Text = r.Name ?? "";
            cmbWhen.SelectedIndex = (int)r.When;

            watcherNames.Clear();
            foreach (Watcher w in kit.Store.Watchers) AddName(watcherNames, w.Name);
            AddName(watcherNames, r.Watcher);
            cmbWatcher.Items.Clear();
            cmbWatcher.Items.Add("Any watcher");
            cmbWatcher.Items.AddRange(watcherNames);
            cmbWatcher.SelectedIndex = IndexIn(watcherNames, r.Watcher) + 1;
            foundBox.Text = r.FoundContains ?? "";

            int unit = r.EverySeconds % 3600 == 0 ? 2 : r.EverySeconds % 60 == 0 ? 1 : 0;
            cmbUnit.SelectedIndex = unit;
            numEvery.Value = r.EverySeconds / (unit == 2 ? 3600 : unit == 1 ? 60 : 1);

            int key = keys.IndexOf(r.HotkeyVk);
            cmbKey.SelectedIndex = key < 0 ? 0 : key;
            chkCtrl.Checked = r.Ctrl;
            chkAlt.Checked = r.Alt;
            chkShift.Checked = r.Shift;

            macroNames.Clear();
            foreach (Macro m in kit.Store.Macros) AddName(macroNames, m.Name);
            AddName(macroNames, r.Macro);
            cmbMacro.Items.Clear();
            cmbMacro.Items.Add(macroNames.Count == 0 ? "No macros yet - make one in Macros" : "Choose a macro");
            cmbMacro.Items.AddRange(macroNames);
            cmbMacro.SelectedIndex = IndexIn(macroNames, r.Macro) + 1;

            numTimes.Value = Math.Max(1, Math.Min(20, r.Times));
            numDelay.Value = Math.Max(0, Math.Min(600, r.DelaySeconds));

            List<string> names = new List<string>();
            IList<string> saved = kit.Accounts == null ? null : kit.Accounts();
            if (saved != null) foreach (string n in saved) AddName(names, n);
            foreach (WatchedClient c in kit.RunningClients()) AddName(names, c.AccountName);
            if (r.Accounts != null) foreach (string n in r.Accounts) AddName(names, n);
            int y = 0;
            foreach (string n in names)
            {
                ThemedCheckBox c = Ui.DarkCheck(n, 2, y, 9f);
                c.ForeColor = Theme.Text;
                c.Tag = n;
                c.Checked = r.Accounts != null && IndexIn(new List<string>(r.Accounts), n) >= 0;
                accountsList.Controls.Add(c);
                accountChecks.Add(c);
                y += 22;
            }
            if (names.Count == 0) accountsList.Controls.Add(Ui.MutedLabel("No accounts saved yet.", 2, 2, 8.25f));

            FillOn(r.When, r.On);
            numGap.Value = Math.Max(0, Math.Min(3600, r.GapSeconds));
            chkAway.Checked = r.OnlyWhenAway;
            ShowWhen();
        }

        void FillOn(RuleWhen when, RuleOn select)
        {
            onChoices.Clear();
            onChoices.AddRange(RuleForm.OnChoices(when));
            cmbOn.Items.Clear();
            foreach (RuleOn o in onChoices) cmbOn.Items.Add(RuleForm.OnName(o, when));
            int at = onChoices.IndexOf(select);
            // A timer or a key has no "where it happened"; the client in
            // front is the nearest thing.
            cmbOn.SelectedIndex = at >= 0 ? at : onChoices.IndexOf(RuleOn.InFront);
            accountsList.Visible = SelectedOn() == RuleOn.TheseAccounts;
        }

        RuleOn SelectedOn()
        {
            int i = cmbOn.SelectedIndex;
            return i >= 0 && i < onChoices.Count ? onChoices[i] : RuleOn.InFront;
        }

        RuleWhen SelectedWhen() { return (RuleWhen)Math.Max(0, cmbWhen.SelectedIndex); }

        void ShowWhen()
        {
            RuleWhen w = SelectedWhen();
            watcherPanel.Visible = w == RuleWhen.WatcherFinds;
            everyPanel.Visible = w == RuleWhen.Every;
            keyPanel.Visible = w == RuleWhen.Hotkey;
            joinPanel.Visible = w == RuleWhen.Joins;
            if (onChoices.Count > 0) FillOn(w, SelectedOn());
        }

        // The rule as the form stands. Starts from a copy of the one being
        // edited, so anything this kind of rule doesn't show is kept.
        public Rule Collect()
        {
            Rule r = original.Copy();
            r.Name = nameBox.Text.Trim();
            r.When = SelectedWhen();
            if (r.When == RuleWhen.WatcherFinds)
            {
                r.Watcher = cmbWatcher.SelectedIndex <= 0 ? null : watcherNames[cmbWatcher.SelectedIndex - 1];
                r.FoundContains = WatcherForm.Blank(foundBox.Text);
            }
            if (r.When == RuleWhen.Every)
            {
                int unit = cmbUnit.SelectedIndex;
                r.EverySeconds = numEvery.Value * (unit == 2 ? 3600 : unit == 1 ? 60 : 1);
            }
            if (r.When == RuleWhen.Hotkey)
            {
                r.HotkeyVk = keys[Math.Max(0, cmbKey.SelectedIndex)];
                r.Ctrl = chkCtrl.Checked;
                r.Alt = chkAlt.Checked;
                r.Shift = chkShift.Checked;
            }
            r.Macro = cmbMacro.SelectedIndex <= 0 ? null : macroNames[cmbMacro.SelectedIndex - 1];
            r.Times = numTimes.Value;
            r.DelaySeconds = numDelay.Value;
            r.On = SelectedOn();
            if (r.On == RuleOn.TheseAccounts)
            {
                List<string> picked = new List<string>();
                foreach (ThemedCheckBox c in accountChecks) if (c.Checked) picked.Add((string)c.Tag);
                r.Accounts = picked.ToArray();
            }
            r.GapSeconds = numGap.Value;
            r.OnlyWhenAway = chkAway.Checked;
            return r;
        }

        public bool TrySave()
        {
            Rule r = Collect();
            string problem = r.Problem();
            if (problem != null) { lblProblem.Text = problem; return false; }
            Result = r;
            return true;
        }

        // ---------- for tests, and anything else driving the form ----------

        public void SetName(string name) { nameBox.Text = name; }
        public void SetWhen(RuleWhen when) { cmbWhen.SelectedIndex = (int)when; }
        public void SetMacro(string macro) { cmbMacro.SelectedIndex = IndexIn(macroNames, macro) + 1; }
        public void SetOn(RuleOn on) { int i = onChoices.IndexOf(on); if (i >= 0) cmbOn.SelectedIndex = i; }

        public void SetHotkey(byte vk, bool ctrl, bool alt, bool shift)
        {
            int i = keys.IndexOf(vk);
            if (i >= 0) cmbKey.SelectedIndex = i;
            chkCtrl.Checked = ctrl;
            chkAlt.Checked = alt;
            chkShift.Checked = shift;
        }

        static void AddName(List<string> names, string n)
        {
            if (string.IsNullOrEmpty(n)) return;
            if (IndexIn(names, n) < 0) names.Add(n);
        }

        static int IndexIn(List<string> names, string n)
        {
            if (n == null) return -1;
            return names.FindIndex(delegate(string s) { return string.Equals(s, n, StringComparison.OrdinalIgnoreCase); });
        }
    }
}
