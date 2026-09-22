using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The rules behind the macro windows, kept apart so they can be tested.
    static class MacroForm
    {
        // Seconds as people type them: "1.5", "1,5", "2s". Null when fine.
        public static string ParseSeconds(string text, out int ms)
        {
            ms = 0;
            string t = (text ?? "").Trim().ToLowerInvariant();
            if (t.EndsWith("s")) t = t.Substring(0, t.Length - 1).Trim();
            t = t.Replace(',', '.');
            double v;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return "That isn't a number of seconds.";
            ms = (int)Math.Round(v * 1000);
            if (ms <= 0) return "Make it more than nothing - at least 0.01 seconds.";
            if (ms > Macro.MAX_MS) return "Keep it under a minute.";
            return null;
        }

        public static string Summary(Macro m)
        {
            int n = m.Steps.Count;
            return n + (n == 1 ? " step" : " steps") + " · about " + MacroStep.Seconds(m.DurationMs);
        }

        // Watchers name the macro they run, so two with one name could not be
        // told apart.
        public static string NameProblem(WatchStore store, string name, Macro editing)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return "Give the macro a name.";
            Macro same = store.FindMacro(name);
            if (same != null && !ReferenceEquals(same, editing))
                return "There's already a macro called \"" + same.Name + "\".";
            return null;
        }
    }

    // Every macro, to add, edit and remove.
    class MacrosDialog : Form
    {
        const int W = 560;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int LIST_H = 360;
        const int FOOT_Y = TOP + LIST_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW = 46;

        readonly WatchKit kit;
        ScrollPanel list;
        Label empty;

        public MacrosDialog(WatchKit kit)
        {
            this.kit = kit;
            WatchUi.Frame(this, "Macros", W, H);
            Build();
            Rebuild();
        }

        public int RowCount { get { return kit.Store.Macros.Count; } }

        void Build()
        {
            Card card = WatchUi.CardAt(this, "MACROS", 12, TOP, W - 24, LIST_H);
            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 44);
            list.Size = new Size(W - 24 - Ui.PAD * 2, LIST_H - 44 - 56);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);

            empty = Ui.MutedLabel(
                "No macros yet. A macro is a few keys and clicks played on a client - to buy something "
                + "when a watcher sees it, say. The client comes to the front while it plays, then "
                + "whatever you were using comes back.", 4, 8, 8.25f);
            empty.MaximumSize = new Size(list.Width - 16, 0);

            Button add = WatchUi.Primary("Add macro", Ui.PAD, LIST_H - 44, 130, 30);
            add.Click += delegate { Add(); };
            card.Controls.Add(add);

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
            if (kit.Store.Macros.Count == 0) list.Controls.Add(empty);

            int y = 0;
            for (int i = 0; i < kit.Store.Macros.Count; i++)
            {
                int index = i;
                Macro m = kit.Store.Macros[i];
                Label name = Ui.RowLabel(m.Name, 4, y + 3, 20, 380, 9.5f, Theme.Text);
                name.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                list.Controls.Add(name);
                list.Controls.Add(Ui.RowLabel(MacroForm.Summary(m), 4, y + 23, 20, 380, 8.25f, Theme.Muted));

                LinkLabel edit = Ui.RowLink("Edit", 400, y, ROW, 9f);
                edit.Click += delegate { Edit(index); };
                list.Controls.Add(edit);
                LinkLabel remove = Ui.RowLink("Remove", 440, y, ROW, 9f);
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
            Rebuild();
        }

        public void AddMacro(Macro m)
        {
            kit.Store.Macros.Add(m);
            Commit("Macro " + m.Name + " added.");
        }

        public void ReplaceAt(int index, Macro m)
        {
            string was = kit.Store.Macros[index].Name;
            kit.Store.Macros[index] = m;
            // Watchers name the macro they play; a rename carries them along.
            if (!string.Equals(was, m.Name, StringComparison.Ordinal)) kit.Store.RenameMacroUses(was, m.Name);
            Commit("Macro " + m.Name + " saved.");
        }

        // Watchers that played it keep watching and telling you; they just
        // play nothing now.
        public void RemoveAt(int index)
        {
            string name = kit.Store.Macros[index].Name;
            kit.Store.Macros.RemoveAt(index);
            int n = kit.Store.RenameMacroUses(name, null);
            Commit("Macro " + name + " removed" + (n == 0 ? "." : " - " + n + (n == 1 ? " watcher or rule" : " watchers and rules") + " played it and now play nothing."));
        }

        void Add()
        {
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), kit))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) AddMacro(d.Result);
        }

        void Edit(int index)
        {
            using (MacroEditDialog d = new MacroEditDialog(kit.Store.Macros[index], kit))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) ReplaceAt(index, d.Result);
        }

        void AskRemove(int index)
        {
            string name = kit.Store.Macros[index].Name;
            if (MessageBox.Show(this, "Remove the macro " + name + "?", "Remove macro",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RemoveAt(index);
        }
    }

    // One macro: its name, its steps, and recording or trying it on a client.
    class MacroEditDialog : Form
    {
        const int W = 620;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int NAME_H = 78;
        const int STEPS_Y = TOP + NAME_H + 12;
        const int STEPS_H = 300;
        const int TRY_Y = STEPS_Y + STEPS_H + 12;
        const int TRY_H = 118;
        const int FOOT_Y = TRY_Y + TRY_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW = 28;
        const int INNER = W - 24 - Ui.PAD * 2;

        readonly Macro original;
        readonly WatchKit kit;
        readonly List<MacroStep> steps = new List<MacroStep>();

        TextBox nameBox;
        ScrollPanel list;
        Label lblSummary, lblProblem, lblStatus, emptySteps;
        ThemedPicker cmbClient;
        Button btnRecord, btnTry;
        WatchedClient[] clients = new WatchedClient[0];
        MacroRecorder recorder;

        public Macro Result { get; private set; }

        public MacroEditDialog(Macro m, WatchKit kit)
        {
            original = m;
            this.kit = kit;
            foreach (MacroStep s in m.Steps) steps.Add(s.Copy());

            WatchUi.Frame(this, string.IsNullOrEmpty(m.Name) ? "New macro" : "Edit " + m.Name, W, H);
            Build();
            nameBox.Text = m.Name ?? "";
            FillClients();
            RebuildSteps();
            FormClosed += delegate { if (recorder != null) { recorder.Dispose(); recorder = null; } };
        }

        public int StepCount { get { return steps.Count; } }
        public string ProblemText { get { return lblProblem.Text; } }

        public string StepsSaid
        {
            get
            {
                List<string> said = new List<string>();
                foreach (MacroStep s in steps) said.Add(s.Describe());
                return string.Join(" | ", said.ToArray());
            }
        }

        // ---------- layout ----------

        void Build()
        {
            Card top = WatchUi.CardAt(this, "MACRO", 12, TOP, W - 24, NAME_H);
            top.Controls.Add(Ui.RowLabel("Name", Ui.PAD, 40, 26, 70, 9f, Theme.Muted));
            nameBox = WatchUi.Input(100, 42, 280);
            top.Controls.Add(nameBox);
            lblSummary = Ui.RowLabel("", 396, 40, 26, W - 24 - 396 - Ui.PAD, 8.25f, Theme.Muted);
            lblSummary.TextAlign = ContentAlignment.MiddleRight;
            top.Controls.Add(lblSummary);

            Card mid = WatchUi.CardAt(this, "STEPS", 12, STEPS_Y, W - 24, STEPS_H);
            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 40);
            list.Size = new Size(INNER, STEPS_H - 40 - 52);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            mid.Controls.Add(list);
            emptySteps = Ui.MutedLabel("No steps yet. Record them from a client below, or add them one at a time.",
                                       4, 6, 8.25f);

            string[] adds = { "Add a key", "Add a click", "Add a wait", "Add typing" };
            MacroStepKind[] kinds = { MacroStepKind.Key, MacroStepKind.Click, MacroStepKind.Wait, MacroStepKind.Type };
            for (int i = 0; i < adds.Length; i++)
            {
                MacroStepKind kind = kinds[i];
                Button b = WatchUi.Secondary(adds[i], Ui.PAD + i * 108, STEPS_H - 44, 100, 30);
                b.Click += delegate { AskNewStep(kind); };
                mid.Controls.Add(b);
            }

            Card low = WatchUi.CardAt(this, "RECORD IT OR TRY IT", 12, TRY_Y, W - 24, TRY_H);
            low.Controls.Add(Ui.RowLabel("Client", Ui.PAD, 40, 26, 70, 9f, Theme.Muted));
            cmbClient = Ui.DarkCombo(100, 40, 280);
            low.Controls.Add(cmbClient);

            btnRecord = WatchUi.Primary("Record", Ui.PAD, 76, 100, 28);
            btnRecord.Click += delegate { if (recorder != null) recorder.Stop(); else Record(); };
            low.Controls.Add(btnRecord);
            btnTry = WatchUi.Secondary("Try it", Ui.PAD + 108, 76, 100, 28);
            btnTry.Click += delegate { TryIt(); };
            low.Controls.Add(btnTry);

            lblStatus = Ui.MutedLabel(
                "Record brings the client to the front and keeps what you press and click in it, "
                + "until you press F8. Nothing typed in any other window is seen.", Ui.PAD + 220, 70, 8.25f);
            lblStatus.MaximumSize = new Size(INNER - 220, 0);
            low.Controls.Add(lblStatus);

            lblProblem = Ui.RowLabel("", 16, FOOT_Y, 32, 360, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);

            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 116 - 8 - 96, FOOT_Y, 96, 32);
            cancel.BackColor = Theme.Card;
            cancel.FlatAppearance.MouseOverBackColor = Theme.Inset;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button save = WatchUi.Primary("Save", W - 12 - 116, FOOT_Y, 116, 32);
            save.Click += delegate { if (TrySave(nameBox.Text)) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            CancelButton = cancel;
        }

        void RebuildSteps()
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, emptySteps)) c.Dispose();
            }
            if (steps.Count == 0) list.Controls.Add(emptySteps);

            int y = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                int index = i;
                list.Controls.Add(Ui.RowLabel((i + 1) + ".", 2, y, ROW, 28, 8.25f, Theme.Muted));
                list.Controls.Add(Ui.RowLabel(steps[i].Describe(), 30, y, ROW, 330, 9f, Theme.Text));

                LinkLabel edit = Ui.RowLink("Edit", 372, y, ROW, 8.25f);
                edit.Click += delegate { AskEditStep(index); };
                list.Controls.Add(edit);
                LinkLabel up = Ui.RowLink("Up", 408, y, ROW, 8.25f);
                up.Click += delegate { MoveStep(index, -1); };
                list.Controls.Add(up);
                LinkLabel down = Ui.RowLink("Down", 436, y, ROW, 8.25f);
                down.Click += delegate { MoveStep(index, 1); };
                list.Controls.Add(down);
                LinkLabel remove = Ui.RowLink("Remove", 478, y, ROW, 8.25f);
                remove.Click += delegate { RemoveStep(index); };
                list.Controls.Add(remove);
                y += ROW;
            }
            list.ResumeLayout();

            Macro m = Current(nameBox.Text);
            lblSummary.Text = MacroForm.Summary(m);
            lblSummary.ForeColor = m.DurationMs > Macro.MAX_MS ? Theme.Amber : Theme.Muted;
        }

        // ---------- steps ----------

        public void AddStep(MacroStep s)
        {
            steps.Add(s);
            RebuildSteps();
        }

        public void MoveStep(int index, int by)
        {
            int to = index + by;
            if (index < 0 || index >= steps.Count || to < 0 || to >= steps.Count) return;
            MacroStep s = steps[index];
            steps.RemoveAt(index);
            steps.Insert(to, s);
            RebuildSteps();
        }

        public void RemoveStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            steps.RemoveAt(index);
            RebuildSteps();
        }

        void AskNewStep(MacroStepKind kind)
        {
            MacroStep s = kind == MacroStepKind.Key ? MacroStep.Key(0, 50)
                        : kind == MacroStepKind.Click ? MacroStep.Click(0.5, 0.5, false)
                        : kind == MacroStepKind.Wait ? MacroStep.Wait(1000)
                        : MacroStep.Type("");
            // Typing is nearly always something to say in chat.
            if (kind == MacroStepKind.Type) s.InChat = true;
            using (MacroStepDialog d = new MacroStepDialog(s))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) AddStep(d.Result);
        }

        void AskEditStep(int index)
        {
            using (MacroStepDialog d = new MacroStepDialog(steps[index]))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                steps[index] = d.Result;
                RebuildSteps();
            }
        }

        // ---------- record and try ----------

        void FillClients()
        {
            clients = kit.RunningClients();
            cmbClient.Items.Clear();
            foreach (WatchedClient c in clients) cmbClient.Items.Add(WatchUi.ClientName(c));
            if (clients.Length > 0) cmbClient.SelectedIndex = 0;
            btnRecord.Enabled = btnTry.Enabled = clients.Length > 0;
            if (clients.Length == 0) Status("Open a Roblox client to record or try a macro.", false);
        }

        IntPtr WindowOfChosen()
        {
            int i = cmbClient.SelectedIndex;
            if (i < 0 || i >= clients.Length) { Status("Choose a client first.", true); return IntPtr.Zero; }
            IntPtr hwnd = WindowCapture.GameWindow(clients[i].Pid);
            if (hwnd == IntPtr.Zero) Status("That client has no window - it may be in the tray.", true);
            return hwnd;
        }

        void Status(string text, bool warn)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        void Record()
        {
            IntPtr hwnd = WindowOfChosen();
            if (hwnd == IntPtr.Zero) return;
            InputSender.ShowClient(hwnd);

            recorder = new MacroRecorder(hwnd, delegate(List<MacroStep> got)
            {
                MacroRecorder r = recorder;
                recorder = null;
                if (r != null) r.Dispose();
                steps.AddRange(got);
                RebuildSteps();
                btnRecord.Text = "Record";
                Status(got.Count == 0 ? "Nothing was recorded - press and click in the client itself."
                                      : "Recorded " + got.Count + (got.Count == 1 ? " step." : " steps."), got.Count == 0);
                try { Activate(); } catch { }
            });
            if (!recorder.Start())
            {
                recorder.Dispose();
                recorder = null;
                Status("Windows wouldn't let it record.", true);
                return;
            }
            btnRecord.Text = "Stop";
            Status("Recording - do it in the client, then press F8.", false);
        }

        void TryIt()
        {
            Macro m = Current(nameBox.Text.Length == 0 ? "Try" : nameBox.Text);
            if (m.Steps.Count == 0) { Status("Add a step first.", true); return; }
            if (m.DurationMs > Macro.MAX_MS) { Status("Keep it under a minute.", true); return; }
            IntPtr hwnd = WindowOfChosen();
            if (hwnd == IntPtr.Zero) return;

            btnTry.Enabled = false;
            Status("Playing...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = MacroPlayer.Play(m, hwnd);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        btnTry.Enabled = true;
                        Status(problem == null ? "Played to the end." : "Didn't finish: " + problem + ".", problem != null);
                    });
                }
                catch { }   // closed while it played
            });
        }

        // ---------- save ----------

        Macro Current(string name)
        {
            Macro m = new Macro();
            m.Name = (name ?? "").Trim();
            foreach (MacroStep s in steps) m.Steps.Add(s.Copy());
            return m;
        }

        public bool TrySave(string name)
        {
            Macro m = Current(name);
            string problem = MacroForm.NameProblem(kit.Store, m.Name, original) ?? m.Problem();
            if (problem != null)
            {
                lblProblem.Text = problem;
                return false;
            }
            Result = m;
            return true;
        }
    }

    // One step, typed in.
    class MacroStepDialog : Form
    {
        const int W = 380;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int CARD_H = 176;
        const int FOOT_Y = TOP + CARD_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int FIELD_X = 110;

        readonly MacroStep step;
        byte vk;
        Button btnKey;
        TextBox secondsBox, textBox;
        ThemedNumeric numAcross, numDown;
        ThemedCheckBox chkRight, chkChat;
        Label lblProblem;

        public MacroStep Result { get; private set; }

        public MacroStepDialog(MacroStep s)
        {
            step = s.Copy();
            vk = s.Vk;
            WatchUi.Frame(this, Title(s.Kind), W, H);
            Build();
        }

        static string Title(MacroStepKind k)
        {
            switch (k)
            {
                case MacroStepKind.Click: return "A click";
                case MacroStepKind.Wait: return "A wait";
                case MacroStepKind.Type: return "Typing";
                case MacroStepKind.KeyDown: return "Hold a key down";
                case MacroStepKind.KeyUp: return "Let go of a key";
                default: return "A key";
            }
        }

        void Build()
        {
            Card card = WatchUi.CardAt(this, "STEP", 12, TOP, W - 24, CARD_H);
            int inner = W - 24 - Ui.PAD * 2;
            switch (step.Kind)
            {
                case MacroStepKind.Click:
                    card.Controls.Add(Ui.RowLabel("Across", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
                    numAcross = Ui.DarkNumeric(FIELD_X, 40, 64, 0, 100, (int)Math.Round(step.X * 100));
                    card.Controls.Add(numAcross);
                    card.Controls.Add(Ui.RowLabel("%", FIELD_X + 70, 40, 26, 30, 9f, Theme.Muted));
                    card.Controls.Add(Ui.RowLabel("Down", Ui.PAD, 72, 26, 80, 9f, Theme.Muted));
                    numDown = Ui.DarkNumeric(FIELD_X, 72, 64, 0, 100, (int)Math.Round(step.Y * 100));
                    card.Controls.Add(numDown);
                    card.Controls.Add(Ui.RowLabel("%", FIELD_X + 70, 72, 26, 30, 9f, Theme.Muted));
                    chkRight = Ui.DarkCheck("The right mouse button", Ui.PAD, 104, 9f);
                    chkRight.Checked = step.RightButton;
                    card.Controls.Add(chkRight);
                    Hint(card, "0% is the game's left or top edge, 100% its right or bottom. "
                             + "Easiest is to record it: press Record and click it in the game.", 130, inner);
                    break;

                case MacroStepKind.Wait:
                    card.Controls.Add(Ui.RowLabel("Seconds", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
                    secondsBox = WatchUi.Input(FIELD_X, 42, 80);
                    secondsBox.Text = MacroStep.Seconds(step.Ms).TrimEnd('s');
                    card.Controls.Add(secondsBox);
                    Hint(card, "Long enough for the game to catch up - a shop opening, a menu sliding in.", 76, inner);
                    break;

                case MacroStepKind.Type:
                    card.Controls.Add(Ui.RowLabel("Text", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
                    textBox = WatchUi.Input(FIELD_X, 42, inner - FIELD_X + Ui.PAD);
                    textBox.Text = step.Text ?? "";
                    card.Controls.Add(textBox);
                    chkChat = Ui.DarkCheck("Say it in chat - open the chat first and send it after", Ui.PAD, 76, 9f);
                    chkChat.AutoSize = true;
                    chkChat.Checked = step.InChat;
                    card.Controls.Add(chkChat);
                    Hint(card, "Ticked, it presses / to open the chat, types this, and presses Enter to send it. "
                             + "Untick it to type into a box that is already open.", 104, inner);
                    break;

                default:
                    card.Controls.Add(Ui.RowLabel("Key", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
                    btnKey = WatchUi.Secondary(vk == 0 ? "Choose a key" : MacroKeys.Name(vk), FIELD_X, 40, 140, 28);
                    btnKey.Click += delegate { ChooseKey(); };
                    card.Controls.Add(btnKey);
                    if (step.Kind == MacroStepKind.Key)
                    {
                        card.Controls.Add(Ui.RowLabel("Hold for", Ui.PAD, 76, 26, 80, 9f, Theme.Muted));
                        secondsBox = WatchUi.Input(FIELD_X, 78, 80);
                        secondsBox.Text = MacroStep.Seconds(step.HoldMs).TrimEnd('s');
                        card.Controls.Add(secondsBox);
                        card.Controls.Add(Ui.RowLabel("seconds", FIELD_X + 88, 76, 26, 80, 9f, Theme.Muted));
                        Hint(card, "A quick press is 0.05. Hold longer to walk or charge something up.", 110, inner);
                    }
                    break;
            }

            lblProblem = Ui.RowLabel("", 16, FOOT_Y, 32, 150, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 90 - 8 - 90, FOOT_Y, 90, 32);
            cancel.BackColor = Theme.Card;
            cancel.FlatAppearance.MouseOverBackColor = Theme.Inset;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button save = WatchUi.Primary("Save", W - 12 - 90, FOOT_Y, 90, 32);
            save.Click += delegate { if (Accept()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
        }

        static void Hint(Card card, string text, int y, int width)
        {
            Label l = Ui.MutedLabel(text, Ui.PAD, y, 8.25f);
            l.MaximumSize = new Size(width, 0);
            card.Controls.Add(l);
        }

        void ChooseKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                vk = d.Captured;
                btnKey.Text = MacroKeys.Name(vk);
            }
        }

        bool Accept()
        {
            MacroStep s = step.Copy();
            string problem = null;
            int ms;
            switch (s.Kind)
            {
                case MacroStepKind.Click:
                    s.X = numAcross.Value / 100.0;
                    s.Y = numDown.Value / 100.0;
                    s.RightButton = chkRight.Checked;
                    break;
                case MacroStepKind.Wait:
                    problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                    s.Ms = ms;
                    break;
                case MacroStepKind.Type:
                    s.Text = textBox.Text;
                    s.InChat = chkChat.Checked;
                    if (s.Text.Length == 0) problem = "Type something.";
                    break;
                default:
                    s.Vk = vk;
                    if (vk == 0) problem = "Choose a key.";
                    else if (s.Kind == MacroStepKind.Key)
                    {
                        problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                        s.HoldMs = ms;
                    }
                    break;
            }
            if (problem != null) { lblProblem.Text = problem; return false; }
            Result = s;
            return true;
        }
    }
}
