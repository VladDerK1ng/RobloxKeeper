using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The rules behind the watchers list, kept apart so they can be tested.
    static class WatchList
    {
        // One line under a watcher's name: what, where, and on whom.
        public static string Describe(Watcher w)
        {
            string where = w.WholeWindow ? "the whole window" : "the box called \"" + w.RegionName + "\"";
            string who = w.Accounts == null || w.Accounts.Length == 0 ? "every client" : string.Join(", ", w.Accounts);
            string then = string.IsNullOrEmpty(w.ThenMacro) ? "" : " · then plays " + w.ThenMacro;
            return WatcherForm.KindName(w.Kind) + " · " + where + " · " + who + then;
        }

        // Why a watcher cannot run right now, for its row, or null. Text and
        // chat need Windows' recogniser; pictures are our own code and do not.
        public static string RowProblem(Watcher w, bool canReadText, string engineProblem)
        {
            if (!canReadText && w.Kind != WatchKind.ImageFound)
                return "Windows can't read text on this PC yet.";
            return engineProblem;
        }

        // Null when there is nothing wrong with what was typed.
        public static string WebhookProblem(string url)
        {
            if (string.IsNullOrEmpty(url) || url.Trim().Length == 0) return null;
            return WebhookPost.LooksLikeDiscordWebhook(url.Trim())
                ? null
                : "That isn't a Discord webhook link. In Discord: Edit Channel, Integrations, Webhooks, Copy Webhook URL.";
        }

        public static string WebhookState(string saved)
        {
            return string.IsNullOrEmpty(saved)
                ? "Not set yet. Watchers can still use a pop-up, a sound and the activity list."
                : "Set. It is kept encrypted on this PC and never shown.";
        }
    }

    // Every watcher, every client's state, the pace, and the Discord webhook
    // they all share. Watchers are added and edited through WatcherEditDialog;
    // switching one on or off, editing it or removing it replaces it in the
    // list rather than changing it, because the watch thread may be reading
    // it at that moment.
    class WatchersDialog : Form
    {
        const int W = 720;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int LIST_H = 330;
        const int LOW_Y = TOP + LIST_H + 12;
        const int LOW_H = 186;
        const int FOOT_Y = LOW_Y + LOW_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int HALF = (W - 36) / 2;
        const int ROW = 46;

        readonly WatchKit kit;
        readonly Action changed;

        ScrollPanel list, clientList;
        Label empty, lblPace, lblHook, lblHookSays;
        TextBox hookBox;
        ThemedPicker setupPicker;
        LinkLabel removeSetup;
        bool fillingSetups;
        readonly ToolTip tips = new ToolTip();
        readonly List<Label> rowLines = new List<Label>();
        readonly List<Label> rowNames = new List<Label>();
        readonly List<string> clientTexts = new List<string>();
        System.Windows.Forms.Timer refresh;

        public WatchersDialog(WatchKit kit, Action changed)
        {
            this.kit = kit;
            this.changed = changed;

            WatchUi.Frame(this, "Watchers", W, H);
            Build();
            FillSetups();
            Rebuild();
            RefreshLive();

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 1000;
            refresh.Tick += delegate { RefreshLive(); };
            refresh.Start();
            FormClosed += delegate { refresh.Stop(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (refresh != null) refresh.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }

        public int RowCount { get { return kit.Store.Watchers.Count; } }
        public string WebhookBoxText { get { return hookBox.Text; } }
        public int SetupCount { get { return setupPicker.Items.Count; } }
        public string SetupShown { get { return setupPicker.Text; } }

        // ---------- layout ----------

        void Build()
        {
            Card card = WatchUi.CardAt(this, "WATCHERS", 12, TOP, W - 24, LIST_H);

            // Which setup the list is showing - and the app is watching with.
            Label setupCap = Ui.RowLabel("Setup", 226, 10, 26, 50, 8.25f, Theme.Muted);
            setupCap.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(setupCap);
            setupPicker = Ui.DarkCombo(282, 10, 200);
            setupPicker.SelectedIndexChanged += delegate { if (!fillingSetups) ChooseSetup(setupPicker.Text); };
            card.Controls.Add(setupPicker);
            string explain = "A setup is a set of watchers - one for each game, say. "
                           + "Choosing another setup swaps every watcher at once.";
            tips.SetToolTip(setupCap, explain);
            tips.SetToolTip(setupPicker, explain);

            LinkLabel newSetup = Ui.RowLink("New", 494, 10, 26, 9f);
            newSetup.Click += delegate { AskNewSetup(); };
            card.Controls.Add(newSetup);
            LinkLabel rename = Ui.RowLink("Rename", 532, 10, 26, 9f);
            rename.Click += delegate { AskRenameSetup(); };
            card.Controls.Add(rename);
            removeSetup = Ui.RowLink("Remove", 590, 10, 26, 9f);
            removeSetup.Click += delegate { AskRemoveSetup(); };
            card.Controls.Add(removeSetup);

            int listY = 44;
            if (!kit.CanReadText)
            {
                Label ocr = Ui.RowLabel(
                    "Windows can't read text on this PC yet, so word and chat watchers can't run. Picture watchers still work.",
                    Ui.PAD, 40, 34, W - 24 - Ui.PAD * 2 - 130, 8.25f, Theme.Amber);
                card.Controls.Add(ocr);
                Button install = WatchUi.Primary("Install it", W - 24 - Ui.PAD - 120, 43, 120, 28);
                install.Click += delegate { InstallOcr(); };
                card.Controls.Add(install);
                listY = 82;
            }

            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, listY);
            list.Size = new Size(W - 24 - Ui.PAD * 2, LIST_H - listY - 56);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);

            empty = Ui.MutedLabel(
                "Nothing is being watched yet. Add a watcher to be told when a word, a new chat line "
                + "or a picture turns up on a client's screen - the client is only looked at, never touched.",
                4, 8, 8.25f);
            empty.MaximumSize = new Size(list.Width - 16, 0);

            Button add = WatchUi.Primary("Add watcher", Ui.PAD, LIST_H - 44, 130, 30);
            add.Click += delegate { Add(); };
            card.Controls.Add(add);

            lblPace = Ui.RowLabel("", 170, LIST_H - 44, 30, W - 24 - 170 - Ui.PAD, 8.25f, Theme.Muted);
            lblPace.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(lblPace);

            Card clients = WatchUi.CardAt(this, "CLIENTS", 12, LOW_Y, HALF, LOW_H);
            LinkLabel same = Ui.RowLink("Same size as Client 1", HALF - Ui.PAD - 130, 10, 26, 8.25f);
            same.Click += delegate { SameSize(); };
            tips.SetToolTip(same, "Every client the size of the first, so boxes and macro clicks land exactly "
                + "the same on all of them. Each window stays where it is.");
            clients.Controls.Add(same);
            clientList = new ScrollPanel();
            clientList.Location = new Point(Ui.PAD, 42);
            clientList.Size = new Size(HALF - Ui.PAD * 2, LOW_H - 54);
            clientList.BackColor = Theme.Card;
            clientList.AutoScroll = true;
            clients.Controls.Add(clientList);

            Card hook = WatchUi.CardAt(this, "DISCORD", 24 + HALF, LOW_Y, HALF, LOW_H);
            int inner = HALF - Ui.PAD * 2;
            lblHook = Ui.MutedLabel("", Ui.PAD, 38, 8.25f);
            lblHook.MaximumSize = new Size(inner, 0);
            hook.Controls.Add(lblHook);

            hook.Controls.Add(Ui.CaptionLabel("PASTE A WEBHOOK LINK", Ui.PAD, 76));
            hookBox = WatchUi.SecretInput(Ui.PAD, 94, inner);
            hook.Controls.Add(hookBox);

            Button save = WatchUi.Primary("Save", Ui.PAD, 126, 70, 28);
            save.Click += delegate { SaveWebhook(hookBox.Text); };
            hook.Controls.Add(save);
            Button test = WatchUi.Secondary("Send a test", Ui.PAD + 78, 126, 104, 28);
            test.Click += delegate { SendTest(); };
            hook.Controls.Add(test);
            LinkLabel remove = Ui.RowLink("Remove", Ui.PAD + 190, 126, 28, 8.25f);
            remove.Click += delegate { ClearWebhook(); };
            hook.Controls.Add(remove);

            lblHookSays = Ui.MutedLabel("", Ui.PAD, 160, 8.25f);
            lblHookSays.MaximumSize = new Size(inner, 0);
            hook.Controls.Add(lblHookSays);
            lblHook.Text = WatchList.WebhookState(kit.Store.WebhookUrl);

            Button close = WatchUi.Primary("Close", W - 12 - 110, FOOT_Y, 110, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            Button macros = WatchUi.Secondary("Macros", 12, FOOT_Y, 110, 32);
            macros.BackColor = Theme.Card;
            macros.FlatAppearance.MouseOverBackColor = Theme.Inset;
            macros.Click += delegate { using (MacrosDialog d = new MacrosDialog(kit)) d.ShowDialog(this); };
            tips.SetToolTip(macros, "Keys and clicks played on a client - on their own, or when a watcher finds something.");
            Controls.Add(macros);

            Button rules = WatchUi.Secondary("Rules", 12 + 110 + 8, FOOT_Y, 110, 32);
            rules.BackColor = Theme.Card;
            rules.FlatAppearance.MouseOverBackColor = Theme.Inset;
            rules.Click += delegate
            {
                using (RulesDialog d = new RulesDialog(kit)) d.ShowDialog(this);
                if (changed != null) changed();
            };
            tips.SetToolTip(rules, "Play a macro when you want: when a watcher finds something, every so often, "
                + "when you press a key, or when a client joins a server.");
            Controls.Add(rules);

            if (kit.Hunts != null)
            {
                Button hunt = WatchUi.Secondary("Hunt", 12 + (110 + 8) * 2, FOOT_Y, 110, 32);
                hunt.BackColor = Theme.Card;
                hunt.FlatAppearance.MouseOverBackColor = Theme.Inset;
                hunt.Click += delegate { using (HuntDialog d = new HuntDialog(kit)) d.ShowDialog(this); };
                tips.SetToolTip(hunt, "Move accounts from server to server of a game until a watcher finds something.");
                Controls.Add(hunt);
            }
        }

        // ---------- the list ----------

        void Rebuild()
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, empty)) c.Dispose();
            }
            rowLines.Clear();
            rowNames.Clear();

            IList<Watcher> watchers = kit.Store.Watchers;
            if (watchers.Count == 0) list.Controls.Add(empty);

            int y = 0;
            for (int i = 0; i < watchers.Count; i++)
            {
                int index = i;          // captured per row, not per loop
                Watcher w = watchers[i];

                ThemedCheckBox on = Ui.DarkCheck("", 2, y, 9f);
                on.Checked = w.Enabled;
                Ui.CenterIn(on, y, ROW);
                on.CheckedChanged += delegate { SetEnabled(index, on.Checked); };
                list.Controls.Add(on);

                Label name = Ui.RowLabel(w.Name, 30, y + 3, 20, 470, 9.5f, w.Enabled ? Theme.Text : Theme.Muted);
                name.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                list.Controls.Add(name);
                rowNames.Add(name);

                Label line = Ui.RowLabel("", 30, y + 23, 20, 520, 8.25f, Theme.Muted);
                list.Controls.Add(line);
                rowLines.Add(line);

                LinkLabel edit = Ui.RowLink("Edit", 560, y, ROW, 9f);
                edit.Click += delegate { Edit(index); };
                list.Controls.Add(edit);

                LinkLabel remove = Ui.RowLink("Remove", 600, y, ROW, 9f);
                remove.Click += delegate { AskRemove(index); };
                list.Controls.Add(remove);

                y += ROW;
            }
            list.ResumeLayout();
            RefreshLive();
        }

        // What changes while the window is open: problems, client states, pace.
        void RefreshLive()
        {
            IList<Watcher> watchers = kit.Store.Watchers;
            for (int i = 0; i < rowLines.Count && i < watchers.Count; i++)
            {
                Watcher w = watchers[i];
                string problem = WatchList.RowProblem(w, kit.CanReadText, kit.Engine.ProblemFor(w));
                string text = problem != null ? "Needs attention: " + problem : WatchList.Describe(w);
                if (rowLines[i].Text != text)
                {
                    rowLines[i].Text = text;
                    rowLines[i].ForeColor = problem != null ? Theme.Amber : Theme.Muted;
                }
            }

            int pace = kit.Engine.IntervalMs;
            lblPace.Text = !kit.Engine.Running
                ? "Not looking - no watcher is switched on."
                : "Pace: " + PassPacer.Describe(pace)
                  + (pace > PassPacer.FastestMs ? " - slowed down to keep up" : "");

            RefreshClients();
        }

        void RefreshClients()
        {
            WatchedClient[] clients = kit.RunningClients();
            List<string> texts = new List<string>();
            foreach (WatchedClient c in clients)
            {
                bool assigned = false;
                foreach (Watcher w in kit.Store.Watchers)
                    if (w.Enabled && w.RunsOn(c.AccountName)) { assigned = true; break; }
                WatchState state;
                bool known = kit.Engine.TryStateOf(c.Pid, out state);
                texts.Add(WatchUi.ClientName(c) + " - " + Watching.StateText(assigned, known, state));
            }
            if (texts.Count == 0) texts.Add("No Roblox clients are open.");

            bool same = texts.Count == clientTexts.Count;
            for (int i = 0; same && i < texts.Count; i++) same = texts[i] == clientTexts[i];
            if (same) return;

            clientTexts.Clear();
            clientTexts.AddRange(texts);
            clientList.SuspendLayout();
            while (clientList.Controls.Count > 0)
            {
                Control c = clientList.Controls[0];
                clientList.Controls.Remove(c);
                c.Dispose();
            }
            int y = 0;
            foreach (string t in texts)
            {
                Label l = Ui.MutedLabel(t, 0, y, 8.25f);
                l.MaximumSize = new Size(clientList.Width - 20, 0);
                clientList.Controls.Add(l);
                y += 36;
            }
            clientList.ResumeLayout();
        }

        // ---------- changing the list ----------

        void Commit()
        {
            kit.Store.Save();
            kit.Engine.ForgetImages();
            if (changed != null) changed();
        }

        public void SetEnabled(int index, bool on)
        {
            Watcher w = kit.Store.Watchers[index];
            if (w.Enabled == on) return;
            Watcher copy = WatcherForm.Copy(w);
            copy.Enabled = on;
            kit.Store.Watchers[index] = copy;
            Commit();
            kit.Say(copy.Name + (on ? " is watching again." : " switched off."));

            // Not rebuilt: this runs inside the tick box's own event, and
            // rebuilding would dispose the box while it is still handling it.
            if (index < rowNames.Count) rowNames[index].ForeColor = on ? Theme.Text : Theme.Muted;
            RefreshLive();
        }

        public void RemoveAt(int index)
        {
            string name = kit.Store.Watchers[index].Name;
            kit.Store.Watchers.RemoveAt(index);
            Commit();
            kit.Say("Watcher " + name + " removed.");
            Rebuild();
        }

        void AskRemove(int index)
        {
            string name = kit.Store.Watchers[index].Name;
            if (MessageBox.Show(this, "Remove " + name + "?", "Remove watcher",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RemoveAt(index);
        }

        void Add()
        {
            Watcher w = Watcher.Default("", WatchKind.TextAppears);
            using (WatcherEditDialog d = new WatcherEditDialog(w, kit))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                kit.Store.Watchers.Add(d.Result);
                Commit();
                kit.Say("Watcher " + d.Result.Name + " added.");
            }
            Rebuild();
        }

        void Edit(int index)
        {
            Watcher w = kit.Store.Watchers[index];
            using (WatcherEditDialog d = new WatcherEditDialog(w, kit))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                kit.Store.Watchers[index] = d.Result;
                Commit();
                kit.Say("Watcher " + d.Result.Name + " saved.");
            }
            Rebuild();
        }

        void SameSize()
        {
            string said;
            List<KeyValuePair<IntPtr, Size>> plan = WindowSizer.Plan(WindowSizer.Measure(kit.RunningClients()), out said);
            WindowSizer.Apply(plan);
            kit.Say(said);
            MessageBox.Show(this, said, "Same size", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- setups ----------

        void FillSetups()
        {
            fillingSetups = true;
            try
            {
                setupPicker.Items.Clear();
                foreach (WatchSetup s in kit.Store.Setups) setupPicker.Items.Add(s.Name);
                setupPicker.SelectedIndex = kit.Store.Setups.IndexOf(kit.Store.Chosen);
                setupPicker.Invalidate();       // a new name at the same place
                removeSetup.Visible = kit.Store.Setups.Count > 1;
            }
            finally { fillingSetups = false; }
        }

        // Every watcher in the list is swapped for the other setup's, and the
        // app is told, so the watch thread swaps too.
        public void ChooseSetup(string name)
        {
            WatchSetup s = kit.Store.FindSetup(name);
            if (s == null || ReferenceEquals(s, kit.Store.Chosen)) return;
            kit.Store.Choose(s.Name);
            Commit();
            kit.Say("Now watching with the " + s.Name + " setup.");
            FillSetups();
            Rebuild();
        }

        public bool AddSetup(string name, bool copyWatchers)
        {
            WatchSetup s = kit.Store.AddSetup(name, copyWatchers);
            if (s == null) return false;
            Commit();
            kit.Say("Setup " + s.Name + " added" + (copyWatchers ? ", with a copy of the watchers." : "."));
            FillSetups();
            Rebuild();
            return true;
        }

        public bool RenameSetup(string name)
        {
            string was = kit.Store.Chosen.Name;
            if (!kit.Store.RenameSetup(kit.Store.Chosen, name)) return false;
            Commit();
            kit.Say("Setup " + was + " is now called " + kit.Store.Chosen.Name + ".");
            FillSetups();
            return true;
        }

        // Always the one in use: that is the one whose watchers are on screen,
        // so it is clear what goes with it.
        public bool RemoveSetup()
        {
            WatchSetup gone = kit.Store.Chosen;
            if (!kit.Store.RemoveSetup(gone)) return false;
            Commit();
            kit.Say("Setup " + gone.Name + " removed. Now watching with the " + kit.Store.Chosen.Name + " setup.");
            FillSetups();
            Rebuild();
            return true;
        }

        void AskNewSetup()
        {
            using (SetupNameDialog d = new SetupNameDialog(kit.Store, "New setup", "", null,
                                                           kit.Store.Watchers.Count > 0))
                if (d.ShowDialog(this) == DialogResult.OK) AddSetup(d.ChosenName, d.CopyWatchers);
        }

        void AskRenameSetup()
        {
            using (SetupNameDialog d = new SetupNameDialog(kit.Store, "Rename setup", kit.Store.Chosen.Name,
                                                           kit.Store.Chosen, false))
                if (d.ShowDialog(this) == DialogResult.OK) RenameSetup(d.ChosenName);
        }

        void AskRemoveSetup()
        {
            WatchSetup s = kit.Store.Chosen;
            int n = s.Watchers.Count;
            string with = n == 0 ? "" : n == 1 ? " and its watcher" : " and its " + n + " watchers";
            if (MessageBox.Show(this, "Remove the setup " + s.Name + with + "?", "Remove setup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RemoveSetup();
        }

        // ---------- the webhook ----------

        // Saves a new webhook if it is one. Never logs or shows it: whoever
        // has that link can post into the channel.
        public bool SaveWebhook(string typed)
        {
            string problem = WatchList.WebhookProblem(typed);
            if (problem != null)
            {
                lblHookSays.Text = problem;
                lblHookSays.ForeColor = Theme.Amber;
                return false;
            }
            if (string.IsNullOrEmpty(typed) || typed.Trim().Length == 0)
            {
                lblHookSays.Text = "Paste the link first.";
                lblHookSays.ForeColor = Theme.Muted;
                return false;
            }

            kit.Store.WebhookUrl = typed.Trim();
            kit.Store.Save();
            hookBox.Text = "";
            lblHook.Text = WatchList.WebhookState(kit.Store.WebhookUrl);
            lblHookSays.Text = "Saved.";
            lblHookSays.ForeColor = Theme.Green;
            kit.Say("Discord webhook saved.");
            if (changed != null) changed();
            return true;
        }

        void ClearWebhook()
        {
            kit.Store.WebhookUrl = "";
            kit.Store.Save();
            lblHook.Text = WatchList.WebhookState(kit.Store.WebhookUrl);
            lblHookSays.Text = "Removed.";
            lblHookSays.ForeColor = Theme.Muted;
            kit.Say("Discord webhook removed.");
        }

        void SendTest()
        {
            string url = kit.Store.WebhookUrl;
            if (string.IsNullOrEmpty(url))
            {
                lblHookSays.Text = "Save a webhook link first.";
                lblHookSays.ForeColor = Theme.Muted;
                return;
            }

            DetectionEvent d = new DetectionEvent();
            d.WatcherName = "RobloxKeeper test";
            d.ClientLabel = "No client";
            d.Matched = "If you can read this, watchers can reach this channel.";
            lblHookSays.Text = "Sending...";
            lblHookSays.ForeColor = Theme.Muted;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string failed = WebhookPost.Send(WebhookPost.Build(url, d, null));
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        lblHookSays.Text = failed == null ? "Sent - check the channel." : "Didn't arrive: " + failed + ".";
                        lblHookSays.ForeColor = failed == null ? Theme.Green : Theme.Amber;
                    });
                }
                catch { }   // closed before the answer came back
            });
        }

        // ---------- reading text ----------

        void InstallOcr()
        {
            try
            {
                Process.Start(Watching.InstallOcr());
                MessageBox.Show(this,
                    "Windows is installing it in the window that just opened. When that finishes, restart "
                    + "RobloxKeeper and word and chat watchers will work.",
                    "Installing text reading", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Win32Exception)
            {
                // Declined at the administrator prompt. Nothing to fix; the
                // picture watchers carry on either way.
                kit.Say("Text reading was not installed. Picture watchers still work.");
            }
            catch (Exception ex)
            {
                kit.Say("Couldn't start installing text reading: " + ex.Message);
            }
        }
    }

    // Asks for a setup's name - a new setup's, or a new name for one - and
    // only lets through a name that setup can have.
    class SetupNameDialog : Form
    {
        const int W = 400;
        const int TOP = WatchUi.TITLEBAR_H + 6;

        readonly WatchStore store;
        readonly WatchSetup renaming;
        readonly TextBox box;
        readonly Label problem;
        readonly ThemedCheckBox copy;

        public string ChosenName;

        public SetupNameDialog(WatchStore store, string title, string name, WatchSetup renaming, bool offerCopy)
        {
            this.store = store;
            this.renaming = renaming;

            int cardH = offerCopy ? 130 : 100;
            int footY = TOP + cardH + 12;
            WatchUi.Frame(this, title, W, footY + 32 + 12);

            Card card = WatchUi.CardAt(this, "NAME", 12, TOP, W - 24, cardH);
            box = WatchUi.Input(Ui.PAD, 40, W - 24 - Ui.PAD * 2);
            box.MaxLength = WatchStore.MaxSetupName;
            box.Text = name ?? "";
            card.Controls.Add(box);

            problem = Ui.MutedLabel("The name of a game works well.", Ui.PAD, 68, 8.25f);
            problem.MaximumSize = new Size(W - 24 - Ui.PAD * 2, 0);
            card.Controls.Add(problem);

            if (offerCopy)
            {
                copy = Ui.DarkCheck("Start with a copy of the watchers in use now", Ui.PAD, 94, 8.25f);
                copy.AutoSize = true;
                card.Controls.Add(copy);
            }

            Button save = WatchUi.Primary("Save", W - 12 - 100, footY, 100, 32);
            save.Click += delegate
            {
                if (!Accept(box.Text)) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 208, footY, 100, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }

        public bool CopyWatchers { get { return copy != null && copy.Checked; } }
        public string ProblemText { get { return problem.Text; } }

        public bool Accept(string typed)
        {
            string why = store.SetupNameProblem(typed, renaming);
            if (why != null)
            {
                problem.Text = why;
                problem.ForeColor = Theme.Amber;
                return false;
            }
            ChosenName = typed.Trim();
            return true;
        }
    }
}
