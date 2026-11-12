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
            if (ms <= 0) return "Use more than 0 seconds. 0.01 is the smallest.";
            if (ms > Macro.MAX_MS) return "Keep it under a minute.";
            return null;
        }

        public static string Summary(Macro m)
        {
            int n = m.Steps.Count;
            // One that repeats until stopped has no length to give.
            if (m.IsSession) return n + (n == 1 ? " step" : " steps") + " · until stopped";
            return n + (n == 1 ? " step" : " steps") + " · about " + MacroStep.Seconds(m.DurationMs);
        }

        // Macros to start from: the things people most often want, already
        // built, to change and save - so nobody has to work out what a
        // "repeat until stopped" is before they have seen one.
        public static readonly string[] ExampleNames =
        {
            "Jump every few seconds",
            "Click one spot over and over",
            "Click a picture whenever it shows up",
            "Hold a key down until I stop it"
        };

        public static Macro Example(int index)
        {
            Macro m = new Macro();
            m.Name = ExampleNames[index];
            m.Steps.Add(MacroStep.Repeat(RepeatMode.Forever, 0));
            switch (index)
            {
                case 0:
                    m.Steps.Add(MacroStep.Key(0x20, 50));
                    m.Steps.Add(MacroStep.Wait(5000));
                    break;
                case 1:
                    // Its place is the middle of the window until Edit and
                    // "Pick it on the game" say otherwise.
                    m.Steps.Add(MacroStep.Click(0.5, 0.5, false));
                    m.Steps.Add(MacroStep.Wait(500));
                    break;
                case 2:
                    // No picture yet: the editor says to choose one before
                    // it will save.
                    m.Steps.Add(MacroStep.ClickPicture(null, null, 0));
                    m.Steps.Add(MacroStep.Wait(1000));
                    break;
                default:
                    m.Steps.Add(MacroStep.KeyDown(0x57));
                    m.Steps.Add(MacroStep.Wait(1000));
                    break;
            }
            m.Steps.Add(MacroStep.EndRepeat());
            return m;
        }

        // What "play it by itself" is called in the Play menu, in the order
        // people ask for them.
        public static readonly RuleWhen[] StartWhens = { RuleWhen.Hotkey, RuleWhen.Every, RuleWhen.WatcherFinds, RuleWhen.Joins };
        public static readonly string[] StartNames =
        {
            "When I press a key...",
            "Every few minutes...",
            "When a watcher finds something...",
            "When a window joins a game..."
        };

        // A rule that plays this macro, filled in so that it could be saved
        // as it is: a key nothing else is on, and every client - which is
        // what "press a key and it plays on all of them" asks for. The rule
        // editor opens on it, so every other way of starting a macro and
        // every choice of clients is one list away.
        //
        // taken: keys this app already listens for, or null.
        public static Rule StartRule(WatchStore store, string macro, RuleWhen when, Predicate<byte> taken)
        {
            Rule r = new Rule();
            r.Macro = macro;
            r.When = when;
            r.On = r.HappensOnAClient ? RuleOn.WhereItHappened : RuleOn.EveryClient;
            string kind = when == RuleWhen.Hotkey ? "key" : when == RuleWhen.Every ? "timer"
                        : when == RuleWhen.Joins ? "on joining" : "when found";
            string name = macro + " - " + kind;
            for (int n = 2; FindRule(store, name) != null; n++) name = macro + " - " + kind + " " + n;
            r.Name = name;

            if (when == RuleWhen.Hotkey)
                foreach (byte vk in RuleForm.HotkeyChoices())
                {
                    if (taken != null && taken(vk)) continue;
                    bool used = false;
                    foreach (Rule other in store.Rules)
                        if (other != null && other.When == RuleWhen.Hotkey && other.HotkeyVk == vk
                            && !other.Ctrl && !other.Alt && !other.Shift) { used = true; break; }
                    if (used) continue;
                    r.HotkeyVk = vk;
                    break;
                }
            return r;
        }

        static Rule FindRule(WatchStore store, string name)
        {
            foreach (Rule r in store.Rules)
                if (r != null && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        // What starts a macro by itself, for its row: "F2, every 5 minutes".
        // Empty when nothing does.
        public static string Starts(WatchStore store, Macro m)
        {
            List<string> by = new List<string>();
            foreach (Rule r in store.Rules)
            {
                if (r == null || !r.Enabled || !string.Equals(r.Macro, m.Name, StringComparison.OrdinalIgnoreCase)) continue;
                string how = r.When == RuleWhen.Hotkey ? r.HotkeyText()
                           : r.When == RuleWhen.Every ? "every " + Rule.Duration(r.EverySeconds)
                           : r.When == RuleWhen.Joins ? "on joining" : "a watcher";
                if (!by.Contains(how)) by.Add(how);
            }
            return string.Join(", ", by.ToArray());
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

        // ---------- how many times, and how fast ----------

        // The choices the Repeat and Speed pickers offer. 0 times is "until I
        // stop it". The macro's own value is added after these when it is
        // none of them, so opening and saving a macro never changes it.
        public static readonly int[] RepeatTimes = { 1, 3, 5, 10, 0 };
        public static readonly int[] SpeedPcts = { 50, 100, 200, 400 };

        public static string TimesName(int times)
        {
            if (times == 1) return "Once";
            if (times <= 0) return "Until I stop it";
            return times + " times";
        }

        public static string SpeedName(int pct)
        {
            if (pct == 100) return "Normal";
            if (pct == 50) return "Half speed";
            if (pct > 100 && pct % 100 == 0) return (pct / 100) + "x faster";
            return pct + "% speed";
        }

        public static List<int> WithValue(int[] usual, int value)
        {
            List<int> all = new List<int>(usual);
            if (!all.Contains(value)) all.Add(value);
            return all;
        }

        // What a macro's key does, in the words the picker uses.
        public static readonly string[] KeyModeNames =
        {
            "Play it once",
            "Press once to start, again to stop",
            "Play while I hold it"
        };

        // The short muted word or two on a macro's row: where it plays, when
        // that isn't a Roblox window, and its key, when it has one.
        public static string RowTag(Macro m)
        {
            List<string> bits = new List<string>();
            if (m.Anywhere) bits.Add("anywhere");
            if (m.Key != 0) bits.Add(MacroKeys.Name(m.Key));
            return string.Join(" · ", bits.ToArray());
        }
    }

    // The app's windows get out of the way while something plays or records
    // on the screen, and come back afterwards. They all hang from the main
    // window, so putting that one away takes the rest with it.
    static class AppWindows
    {
        public static Form Root(Form f)
        {
            while (f != null && f.Owner != null) f = f.Owner;
            return f;
        }

        // Nothing happens for a window that was never shown (a test, say).
        public static void Away(Form from)
        {
            Form root = Root(from);
            if (root == null || !root.IsHandleCreated) return;
            try { root.WindowState = FormWindowState.Minimized; } catch { }
        }

        public static void Back(Form from)
        {
            Form root = Root(from);
            if (root == null || !root.IsHandleCreated) return;
            try
            {
                // Minimised, the main window hides itself in the tray.
                root.Show();
                if (root.WindowState == FormWindowState.Minimized) root.WindowState = FormWindowState.Normal;
                root.Activate();
                InputSender.FocusWindow(root.Handle);
                if (from != null && !ReferenceEquals(from, root) && from.IsHandleCreated) from.Activate();
            }
            catch { }
        }
    }

    // What to call a macro that is about to be saved.
    class MacroNameDialog : Form
    {
        const int W = 380;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        readonly TextBox box;
        readonly Label says;
        readonly Func<string, string> problemWith;

        public string Chosen;

        // problemWith: why a name can't be used, or null when it can.
        public MacroNameDialog(string title, Func<string, string> problemWith)
        {
            this.problemWith = problemWith;
            int footY = TOP + 100 + 12;
            WatchUi.Frame(this, title, W, footY + 32 + 12);
            Card card = WatchUi.CardAt(this, "NAME", 12, TOP, W - 24, 100);
            box = WatchUi.Input(Ui.PAD, 40, W - 24 - Ui.PAD * 2);
            box.MaxLength = 40;
            card.Controls.Add(box);
            says = Ui.MutedLabel("Name it after what it does, like \"Open the shop\". You can change it later.", Ui.PAD, 68, 8.25f);
            says.MaximumSize = new Size(W - 24 - Ui.PAD * 2, 0);
            card.Controls.Add(says);

            Button save = WatchUi.Primary("Save", W - 12 - 100, footY, 100, 32);
            save.Click += delegate { if (Accept(box.Text)) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 208, footY, 100, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Shown += delegate { box.Focus(); };
        }

        public bool Accept(string typed)
        {
            string name = (typed ?? "").Trim();
            string why = name.Length == 0 ? "Give the macro a name." : problemWith == null ? null : problemWith(name);
            if (why != null)
            {
                says.Text = why;
                says.ForeColor = Theme.Amber;
                return false;
            }
            Chosen = name;
            return true;
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
                "No macros yet. A macro replays your keys and clicks, in a Roblox window "
                + "or anywhere on your screen.", 4, 8, 8.25f);
            empty.MaximumSize = new Size(list.Width - 16, 0);

            Button add = WatchUi.Primary("Add macro", Ui.PAD, LIST_H - 44, 130, 30);
            add.Click += delegate { Add(); };
            card.Controls.Add(add);

            Button examples = WatchUi.Secondary("Start from an example", Ui.PAD + 138, LIST_H - 44, 180, 30);
            examples.Click += delegate
            {
                ContextMenuStrip menu = DarkMenu.Make();
                for (int i = 0; i < MacroForm.ExampleNames.Length; i++)
                {
                    int index = i;
                    menu.Items.Add(MacroForm.ExampleNames[i], null, delegate { AddFrom(MacroForm.Example(index)); });
                }
                menu.Show(examples, new Point(0, examples.Height));
            };
            card.Controls.Add(examples);

            Button close = WatchUi.Primary("Close", W - 12 - 110, FOOT_Y, 110, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            // Only with a main window behind it: these are its keys.
            if (kit.Macros == null) return;
            Button clicker = WatchUi.Secondary("Autoclicker", 12, FOOT_Y, 110, 32);
            clicker.Click += delegate { using (AutoClickDialog d = new AutoClickDialog(kit)) d.ShowDialog(this); };
            Controls.Add(clicker);

            Button stop = WatchUi.Secondary("", 12 + 118, FOOT_Y, 150, 32);
            stop.Text = "Stop key: " + MacroKeys.Name(kit.Macros.StopKey);
            stop.Click += delegate
            {
                using (KeyCaptureDialog d = new KeyCaptureDialog(true))
                {
                    if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                    string why = kit.Macros.StopKeyProblem(d.Captured);
                    if (why != null)
                    {
                        MessageBox.Show(this, why + " Choose another key to stop macros.", "Stop key",
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    kit.Macros.StopKey = d.Captured;
                    stop.Text = "Stop key: " + MacroKeys.Name(kit.Macros.StopKey);
                }
            };
            Controls.Add(stop);
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
                list.Controls.Add(RowGrip.For(i, kit.Store.Macros.Count, y, ROW, 0, MoveAt, CopyAt));
                Label name = Ui.RowLabel(m.Name, 20, y + 3, 20, 198, 9.5f, Theme.Text);
                name.Font = Ui.F("Segoe UI", 9.5f, FontStyle.Bold);
                name.AutoEllipsis = true;
                list.Controls.Add(name);
                // Where it plays and its key, off to the side of its name.
                string tag = MacroForm.RowTag(m);
                if (tag.Length > 0)
                {
                    Label where = Ui.RowLabel(tag, 222, y + 3, 20, 128, 8.25f, Theme.Muted);
                    where.TextAlign = ContentAlignment.MiddleRight;
                    where.AutoEllipsis = true;
                    list.Controls.Add(where);
                }
                // What starts it, where something does - so a key set up
                // last week is not a thing to be remembered.
                string starts = MacroForm.Starts(kit.Store, m);
                list.Controls.Add(Ui.RowLabel(MacroForm.Summary(m) + (starts.Length > 0 ? " · starts with " + starts : ""),
                                              20, y + 23, 20, 324, 8.25f, Theme.Muted));

                LinkLabel play = Ui.RowLink("Play", 354, y, ROW, 9f);
                play.Click += delegate { ShowPlay(play, index); };
                list.Controls.Add(play);

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
            if (kit.Macros != null) kit.Macros.MacrosSaved();       // a macro's own key may have changed
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

        // Only where it shows: watchers and rules name the macro they play.
        public void MoveAt(int from, int to)
        {
            if (!RowOrder.Move(kit.Store.Macros, from, to)) return;
            kit.Store.Save();
            Rebuild();
        }

        // A starting point for one that is nearly the same.
        public void CopyAt(int index)
        {
            Macro was = kit.Store.Macros[index];
            Macro copy = was.Copy();
            // Two macros on one key would play together.
            copy.Key = 0;
            copy.Name = RowOrder.CopyName(was.Name, delegate(string n) { return kit.Store.FindMacro(n) != null; });
            kit.Store.Macros.Insert(index + 1, copy);
            Commit("Macro " + was.Name + " copied as " + copy.Name + ".");
        }

        // Watchers that played it keep watching and telling you; they just
        // play nothing now.
        public void RemoveAt(int index)
        {
            string name = kit.Store.Macros[index].Name;
            kit.Store.Macros.RemoveAt(index);
            int n = kit.Store.RenameMacroUses(name, null);
            Commit("Macro " + name + " removed" + (n == 0 ? "." : ". " + n + (n == 1 ? " watcher or rule" : " watchers and rules") + " used it and now do nothing."));
        }

        // ---------- playing one ----------

        // Everything that plays a macro, in one place: now, on every client
        // or on one of them - and by itself, on a key, a timer, a watcher or
        // a join, each of which is a rule made here rather than hunted for.
        void ShowPlay(Control under, int index)
        {
            Macro m = kit.Store.Macros[index];
            ContextMenuStrip menu = DarkMenu.Make();
            if (m.Anywhere)
            {
                ShowPlayAnywhere(menu, m);
                AddSaveAsScript(menu, index);
                menu.Show(under, new Point(0, under.Height));
                return;
            }
            IMacroPlay host = kit.Macros as IMacroPlay;
            WatchedClient[] clients = kit.RunningClients();
            if (host != null)
            {
                if (clients.Length == 0)
                {
                    menu.Items.Add("Now - open a Roblox window first").Enabled = false;
                }
                else
                {
                    if (clients.Length > 1)
                        menu.Items.Add("Now, on every window (" + clients.Length + ")", null, delegate { PlayNow(m, clients); });
                    foreach (WatchedClient c in clients)
                    {
                        WatchedClient one = c;
                        menu.Items.Add("Now, on " + WatchUi.ClientName(c), null, delegate { PlayNow(m, new WatchedClient[] { one }); });
                    }
                }
                menu.Items.Add(new ToolStripSeparator());
            }
            for (int i = 0; i < MacroForm.StartWhens.Length; i++)
            {
                RuleWhen when = MacroForm.StartWhens[i];
                menu.Items.Add(MacroForm.StartNames[i], null, delegate { AskStart(index, when); });
            }
            AddSaveAsScript(menu, index);
            menu.Show(under, new Point(0, under.Height));
        }

        // The same steps as a Lua script, one line each, to change and run
        // from the Scripts window. Only with a main window behind it.
        void AddSaveAsScript(ContextMenuStrip menu, int index)
        {
            if (kit.Scripts == null) return;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Save as script...", null, delegate { AskSaveAsScript(index); });
        }

        void AskSaveAsScript(int index)
        {
            Macro m = kit.Store.Macros[index];
            string start = ScriptMaking.UniqueName(kit.Scripts.Store, m.Name);
            using (ScriptNameDialog d = new ScriptNameDialog("Save as a script", start,
                       delegate(string n) { return kit.Scripts.Store.NameProblem(n, null); }))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                string why = SaveAsScript(index, d.Chosen);
                if (why != null) { MessageBox.Show(this, why, "Save as script", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                using (ScriptsDialog w = new ScriptsDialog(kit, d.Chosen)) w.ShowDialog(this);
            }
        }

        // The macro as a new script under this name. Null when it was made;
        // otherwise why not. The macro itself is left as it is.
        public string SaveAsScript(int index, string name)
        {
            Macro m = kit.Store.Macros[index];
            string why = ScriptMaking.FromMacro(kit.Scripts, m, name);
            if (why == null) kit.Say("Script " + name + " saved from the macro " + m.Name + ".");
            return why;
        }

        // One that plays on the screen needs no Roblox window, and a rule
        // would play it on one - so what starts it by itself is its own key,
        // set in Edit, and the menu is only whether it is playing.
        void ShowPlayAnywhere(ContextMenuStrip menu, Macro m)
        {
            if (kit.Macros == null)
            {
                menu.Items.Add("Play now - open the main window first").Enabled = false;
                return;
            }
            if (IsPlayingNow(m)) menu.Items.Add("Stop", null, delegate { StopAnywhere(m); });
            else menu.Items.Add("Play now", null, delegate { PlayAnywhereNow(m); });
        }

        bool IsPlayingNow(Macro m)
        {
            IMacroHost host = kit.Macros;
            return host != null && (host.IsPlaying(MacroPlay.PlayTag(m.Name)) || host.IsPlaying(MacroPlay.KeyTag(m.Name)));
        }

        public void StopAnywhere(Macro m)
        {
            IMacroHost host = kit.Macros;
            if (host == null) return;
            host.StopTag(MacroPlay.PlayTag(m.Name));
            host.StopTag(MacroPlay.KeyTag(m.Name));
        }

        // The windows get out of the way first: it plays on whatever is on
        // the screen, and they would be in the way of its clicks.
        public void PlayAnywhereNow(Macro m)
        {
            IMacroHost host = kit.Macros;
            if (host == null) return;
            string why = m.Problem();
            if (why != null) { kit.Say("Didn't play " + m.Name + ": " + why); return; }
            AppWindows.Away(this);
            why = host.PlayAnywhere(m);
            if (why != null)
            {
                AppWindows.Back(this);
                kit.Say("Didn't play " + m.Name + ": " + why);
            }
        }

        void PlayNow(Macro m, WatchedClient[] on)
        {
            IMacroPlay host = kit.Macros as IMacroPlay;
            if (host == null) return;
            string why = m.Problem();
            if (why != null) { kit.Say("Didn't play " + m.Name + ": " + why); return; }
            int sent = host.PlayNow(m, on);
            string stop = kit.Macros == null ? "the stop key" : MacroKeys.Name(kit.Macros.StopKey);
            kit.Say(sent == 0 ? m.Name + " is already playing there."
                  : "Playing " + m.Name + " on " + (sent == 1 ? "1 window" : sent + " windows, one after another")
                    + ". " + stop + " stops it.");
        }

        // The rule that would start it, as it would be saved if nothing in
        // the editor were touched.
        public Rule StartRuleFor(int index, RuleWhen when)
        {
            IMacroHost host = kit.Macros;
            return MacroForm.StartRule(kit.Store, kit.Store.Macros[index].Name, when, delegate(byte vk)
            {
                return host != null && (vk == host.StopKey || (host.Clicker != null && host.Clicker.Enabled && vk == host.Clicker.ToggleVk)
                                        || host.KeyUseProblem(vk, KeyUse.RULE, null) != null);
            });
        }

        public void AddStartRule(Rule r)
        {
            kit.Store.Rules.Add(r);
            Commit(r.Describe() + ".");
        }

        void AskStart(int index, RuleWhen when)
        {
            using (RuleEditDialog d = new RuleEditDialog(StartRuleFor(index, when), kit, "Play " + kit.Store.Macros[index].Name + " by itself"))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) AddStartRule(d.Result);
        }

        void Add()
        {
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), kit))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) AddMacro(d.Result);
        }

        // Opened in the editor to be changed, and only kept if it is saved.
        void AddFrom(Macro example)
        {
            using (MacroEditDialog d = new MacroEditDialog(example, kit))
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

    // One macro: its name, where and how it plays, its steps, and recording
    // or trying it - on a Roblox window, or on the screen.
    class MacroEditDialog : Form
    {
        const int W = 620;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        // The three cards share this much height. The settings card takes
        // what its rows need and the steps card gives up the rest, so the
        // window never grows past a short screen (768 high is common).
        const int CARDS_H = 584;
        const int PLAY_Y = 108;         // where the rows that come and go start
        const int PLAY_ROW = 34;
        const int TRY_H = 116;
        const int FOOT_Y = TOP + CARDS_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW = 28;
        const int INDENT = 16;
        const int INNER = W - 24 - Ui.PAD * 2;
        // Two columns for the settings: a label and its box, twice.
        const int LEFT_LBL = 16, LEFT_X = 112, LEFT_W = 200;
        const int RIGHT_LBL = 340, RIGHT_X = 412, RIGHT_W = 168;

        // How tall the window is, for the test that keeps it on a short screen.
        public const int HEIGHT = H;

        readonly Macro original;
        readonly WatchKit kit;
        readonly List<MacroStep> steps = new List<MacroStep>();

        // What the settings say now. They are kept here, not read back from
        // the boxes, so a row that is hidden still keeps its value.
        bool anywhere;
        byte key;
        MacroKeyMode keyMode;
        int times, speed;
        readonly List<int> repeatValues, speedValues;
        // What is showing, kept here because a control reports itself hidden
        // whenever its window is, which is not what a test wants to know.
        bool repeatShown, keyModeShown, clientRowShown;

        TextBox nameBox;
        ScrollPanel list;
        Card top, mid, low;
        Label lblSummary, lblProblem, lblStatus, emptySteps, lblKeyHint, lblClient;
        Label lblWhen, lblRepeat, lblSpeed;
        ThemedPicker cmbWhere, cmbClient, cmbKeyMode, cmbRepeat, cmbSpeed;
        ThemedCheckBox chkMoves;
        Button btnRecord, btnTry, btnKey;
        LinkLabel lnkNoKey;
        readonly List<Control> midButtons = new List<Control>();
        WatchedClient[] clients = new WatchedClient[0];
        MacroRecorder recorder;
        string tryTag;                  // a try on the screen that is going, by its tag
        readonly System.Windows.Forms.Timer tryPoll = new System.Windows.Forms.Timer();

        public Macro Result { get; private set; }

        public MacroEditDialog(Macro m, WatchKit kit)
        {
            original = m;
            this.kit = kit;
            anywhere = m.Anywhere;
            key = m.Key;
            keyMode = m.KeyMode;
            times = m.Times;
            speed = m.SpeedPct;
            repeatValues = MacroForm.WithValue(MacroForm.RepeatTimes, times);
            speedValues = MacroForm.WithValue(MacroForm.SpeedPcts, speed);
            foreach (MacroStep s in m.Steps) steps.Add(s.Copy());

            WatchUi.Frame(this, string.IsNullOrEmpty(m.Name) ? "New macro" : "Edit " + m.Name, W, H);
            Build();
            nameBox.Text = m.Name ?? "";
            FillClients();
            Flow();
            RebuildSteps();

            tryPoll.Interval = 250;
            tryPoll.Tick += delegate { CheckTry(); };
            FormClosed += delegate
            {
                if (recorder != null) { recorder.Dispose(); recorder = null; }
                tryPoll.Stop();
                if (tryTag != null && kit != null && kit.Macros != null) kit.Macros.StopTag(tryTag);
                tryPoll.Dispose();
            };
        }

        public int StepCount { get { return steps.Count; } }
        public string ProblemText { get { return lblProblem.Text; } }
        public string StatusText { get { return lblStatus.Text; } }

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
            top = WatchUi.CardAt(this, "MACRO", 12, TOP, W - 24, 150);
            top.Controls.Add(Ui.RowLabel("Name", LEFT_LBL, 40, 26, 96, 9f, Theme.Muted));
            nameBox = WatchUi.Input(LEFT_X, 42, 216);
            top.Controls.Add(nameBox);

            top.Controls.Add(Ui.RowLabel("Plays on", RIGHT_LBL, 40, 26, 70, 9f, Theme.Muted));
            cmbWhere = Ui.DarkCombo(RIGHT_X, 40, RIGHT_W);
            cmbWhere.Items.Add("A Roblox window");
            cmbWhere.Items.Add("Anywhere on my screen");
            cmbWhere.SelectedIndex = anywhere ? 1 : 0;
            cmbWhere.SelectedIndexChanged += delegate { ChangeWhere(cmbWhere.SelectedIndex == 1); };
            top.Controls.Add(cmbWhere);

            top.Controls.Add(Ui.RowLabel("Key", LEFT_LBL, 74, 28, 96, 9f, Theme.Muted));
            btnKey = WatchUi.Secondary("", LEFT_X, 74, 130, 28);
            btnKey.Click += delegate { ChooseKey(); };
            top.Controls.Add(btnKey);
            lnkNoKey = Ui.RowLink("No key", LEFT_X + 138, 74, 28, 8.25f);
            lnkNoKey.Click += delegate { SetKey(0); };
            top.Controls.Add(lnkNoKey);
            lblKeyHint = Ui.MutedLabel("Roblox gets the key too, so pick one the game doesn't use.", LEFT_X + 198, 72, 8.25f);
            lblKeyHint.MaximumSize = new Size(W - 24 - Ui.PAD - (LEFT_X + 198), 0);
            top.Controls.Add(lblKeyHint);

            // These come and go, and sit in the next free place: see Flow.
            lblWhen = Ui.RowLabel("When I press it", 0, 0, 26, 96, 9f, Theme.Muted);
            cmbKeyMode = Ui.DarkCombo(0, 0, LEFT_W);
            foreach (string n in MacroForm.KeyModeNames) cmbKeyMode.Items.Add(n);
            cmbKeyMode.SelectedIndex = (int)keyMode;
            cmbKeyMode.SelectedIndexChanged += delegate
            {
                keyMode = (MacroKeyMode)Math.Max(0, cmbKeyMode.SelectedIndex);
                Flow();
            };
            lblRepeat = Ui.RowLabel("Repeat", 0, 0, 26, 96, 9f, Theme.Muted);
            cmbRepeat = Ui.DarkCombo(0, 0, LEFT_W);
            foreach (int t in repeatValues) cmbRepeat.Items.Add(MacroForm.TimesName(t));
            cmbRepeat.SelectedIndex = repeatValues.IndexOf(times);
            cmbRepeat.SelectedIndexChanged += delegate { if (cmbRepeat.SelectedIndex >= 0) times = repeatValues[cmbRepeat.SelectedIndex]; };
            lblSpeed = Ui.RowLabel("Speed", 0, 0, 26, 96, 9f, Theme.Muted);
            cmbSpeed = Ui.DarkCombo(0, 0, LEFT_W);
            foreach (int p in speedValues) cmbSpeed.Items.Add(MacroForm.SpeedName(p));
            cmbSpeed.SelectedIndex = speedValues.IndexOf(speed);
            cmbSpeed.SelectedIndexChanged += delegate { if (cmbSpeed.SelectedIndex >= 0) speed = speedValues[cmbSpeed.SelectedIndex]; };
            foreach (Control c in new Control[] { lblWhen, cmbKeyMode, lblRepeat, cmbRepeat, lblSpeed, cmbSpeed }) top.Controls.Add(c);

            mid = WatchUi.CardAt(this, "STEPS", 12, TOP + 150 + 12, W - 24, 260);
            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 40);
            list.Size = new Size(INNER, 168);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            mid.Controls.Add(list);
            emptySteps = Ui.MutedLabel("No steps yet. Record them below, or add them one at a time.", 4, 6, 8.25f);

            lblSummary = Ui.RowLabel("", W - 24 - Ui.PAD - 96 - 8 - 190, 8, 24, 190, 8.25f, Theme.Muted);
            lblSummary.TextAlign = ContentAlignment.MiddleRight;
            mid.Controls.Add(lblSummary);

            // The same steps as lines of text: quicker for someone who would
            // rather type, and how a macro is given to somebody else.
            LinkLabel asText = Ui.RowLink("Write it as text", W - 24 - Ui.PAD - 96, 8, 24, 8.25f);
            asText.Click += delegate { AskText(); };
            mid.Controls.Add(asText);

            string[] adds = { "Add a key", "Add a click", "Add a wait", "Add text" };
            MacroStepKind[] kinds = { MacroStepKind.Key, MacroStepKind.Click, MacroStepKind.Wait, MacroStepKind.Type };
            for (int i = 0; i < adds.Length; i++)
            {
                MacroStepKind kind = kinds[i];
                Button b = WatchUi.Secondary(adds[i], Ui.PAD + i * 108, 216, 100, 30);
                b.Click += delegate { AskNewStep(kind); };
                mid.Controls.Add(b);
                midButtons.Add(b);
            }
            Button more = WatchUi.Secondary("More...", Ui.PAD + adds.Length * 108, 216, 100, 30);
            more.Click += delegate { ShowMore(more); };
            mid.Controls.Add(more);
            midButtons.Add(more);

            low = WatchUi.CardAt(this, "RECORD IT OR TRY IT", 12, TOP + CARDS_H - TRY_H, W - 24, TRY_H);
            btnRecord = WatchUi.Primary("Record", Ui.PAD, 40, 100, 28);
            btnRecord.Click += delegate { if (recorder != null) recorder.Stop(); else Record(); };
            low.Controls.Add(btnRecord);
            btnTry = WatchUi.Secondary("Try it", Ui.PAD + 108, 40, 100, 28);
            btnTry.Click += delegate { TryIt(); };
            low.Controls.Add(btnTry);
            lblClient = Ui.RowLabel("Roblox window", 244, 40, 28, 90, 9f, Theme.Muted);
            low.Controls.Add(lblClient);
            cmbClient = Ui.DarkCombo(336, 41, 244);
            low.Controls.Add(cmbClient);

            chkMoves = Ui.DarkCheck("Mouse movement too", Ui.PAD, 80, 8.25f);
            chkMoves.AutoSize = true;
            low.Controls.Add(chkMoves);
            lblStatus = Ui.MutedLabel("", Ui.PAD + 160, 76, 8.25f);
            lblStatus.MaximumSize = new Size(INNER - 160, 0);
            low.Controls.Add(lblStatus);

            lblProblem = Ui.RowLabel("", 16, FOOT_Y, 32, 360, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);

            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 116 - 8 - 96, FOOT_Y, 96, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button save = WatchUi.Primary("Save", W - 12 - 116, FOOT_Y, 116, 32);
            save.Click += delegate { if (TrySave(nameBox.Text)) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            CancelButton = cancel;
        }

        // Lays out the settings that come and go, and then the cards under
        // them: the first card is as tall as its rows, the steps card takes
        // what is left.
        void Flow()
        {
            bool hasKey = key != 0;
            // Once, or no key at all: the count applies. A key that starts
            // and stops it, or holds it, goes round until it is stopped.
            bool repeatApplies = !hasKey || keyMode == MacroKeyMode.Once;

            btnKey.Text = hasKey ? MacroKeys.Name(key) : "Choose a key";
            lnkNoKey.Visible = lblKeyHint.Visible = hasKey;
            lblWhen.Visible = cmbKeyMode.Visible = hasKey;
            lblRepeat.Visible = cmbRepeat.Visible = repeatApplies;
            repeatShown = repeatApplies;
            keyModeShown = hasKey;

            List<Control[]> rows = new List<Control[]>();
            if (hasKey) rows.Add(new Control[] { lblWhen, cmbKeyMode });
            if (repeatApplies) rows.Add(new Control[] { lblRepeat, cmbRepeat });
            rows.Add(new Control[] { lblSpeed, cmbSpeed });
            for (int i = 0; i < rows.Count; i++)
            {
                int y = PLAY_Y + (i / 2) * PLAY_ROW;
                bool right = i % 2 == 1;
                rows[i][0].Bounds = right ? new Rectangle(RIGHT_LBL, y, 70, 26) : new Rectangle(LEFT_LBL, y, 96, 26);
                rows[i][1].Bounds = right ? new Rectangle(RIGHT_X, y, RIGHT_W, 26) : new Rectangle(LEFT_X, y, LEFT_W, 26);
            }

            int topH = PLAY_Y + ((rows.Count + 1) / 2) * PLAY_ROW + 8;
            top.Height = topH;
            int midH = CARDS_H - topH - 12 - TRY_H - 12;
            mid.Top = TOP + topH + 12;
            mid.Height = midH;
            list.Height = midH - 40 - 52;
            foreach (Control b in midButtons) b.Top = midH - 44;
            low.Top = mid.Bottom + 12;
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

            int y = 0, depth = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                int index = i;
                // Steps inside a repeat sit in from it, so what repeats can
                // be seen at a glance.
                if (steps[i].Kind == MacroStepKind.EndRepeat && depth > 0) depth--;
                int inset = Math.Min(depth, Macro.MAX_DEPTH) * INDENT;
                bool marker = steps[i].Kind == MacroStepKind.Repeat || steps[i].Kind == MacroStepKind.EndRepeat;
                list.Controls.Add(RowGrip.For(i, steps.Count, y, ROW, 0, MoveStepTo, CopyStep));
                list.Controls.Add(Ui.RowLabel((i + 1) + ".", 16, y, ROW, 24, 8.25f, Theme.Muted));
                list.Controls.Add(Ui.RowLabel(steps[i].Describe(), 40 + inset, y, ROW, 328 - inset, 9f,
                                              marker ? Theme.AccentHover : Theme.Text));
                if (steps[i].Kind == MacroStepKind.Repeat) depth++;

                // An end has nothing to edit.
                if (steps[i].Kind != MacroStepKind.EndRepeat)
                {
                    LinkLabel edit = Ui.RowLink("Edit", 372, y, ROW, 8.25f);
                    edit.Click += delegate { AskEditStep(index); };
                    list.Controls.Add(edit);
                }
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
            // The minute is for a Roblox window held in front the whole time;
            // on the screen there is half an hour.
            int most = anywhere ? Macro.MAX_ANYWHERE_MS : Macro.MAX_MS;
            lblSummary.ForeColor = !m.IsSession && m.DurationMs > most ? Theme.Amber : Theme.Muted;
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

        // Dragged, or moved from the grip's menu - anywhere in one go.
        public void MoveStepTo(int from, int to)
        {
            if (RowOrder.Move(steps, from, to)) RebuildSteps();
        }

        public void CopyStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            steps.Insert(index + 1, steps[index].Copy());
            RebuildSteps();
        }

        public void RemoveStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            steps.RemoveAt(index);
            RebuildSteps();
        }

        // ---------- as text ----------

        PictureShelf Shelf()
        {
            return PictureShelf.For(kit.Store, steps, kit.Macros == null ? null : WatchStore.TemplatesDir);
        }

        public string AsText()
        {
            return MacroText.Write(steps, Shelf());
        }

        // The steps a text stands for, in place of the ones there are. Null
        // when it was understood; otherwise why not, and nothing changes.
        public string TakeText(string text)
        {
            List<MacroStep> got = new List<MacroStep>();
            string problem = MacroText.Read(text, Shelf(), got);
            if (problem != null) return problem;
            UseSteps(got);
            return null;
        }

        void UseSteps(List<MacroStep> got)
        {
            steps.Clear();
            steps.AddRange(got);
            RebuildSteps();
        }

        void AskText()
        {
            using (MacroTextDialog d = new MacroTextDialog(steps, Shelf(), kit.Say))
                if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) UseSteps(d.Result);
        }

        // The steps there is no room to give a button each.
        void ShowMore(Control under)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            menu.Items.Add("Click a picture, wherever it is", null, delegate { AskNewStep(MacroStepKind.ClickPicture); });
            menu.Items.Add("Wait for a picture to appear or disappear", null, delegate { AskNewStep(MacroStepKind.WaitPicture); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Move the pointer", null, delegate { AskNewStep(MacroStepKind.Move); });
            menu.Items.Add("Drag", null, delegate { AskNewStep(MacroStepKind.Drag); });
            menu.Items.Add("Scroll", null, delegate { AskNewStep(MacroStepKind.Scroll); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Repeat these steps...", null, delegate { AskNewStep(MacroStepKind.Repeat); });
            menu.Items.Add("End of repeat", null, delegate { AddStep(MacroStep.EndRepeat()); });
            menu.Show(under, new Point(0, under.Height));
        }

        // A new step of each kind, as its window first shows it.
        public static MacroStep NewStep(MacroStepKind kind)
        {
            switch (kind)
            {
                case MacroStepKind.Key: return MacroStep.Key(0, 50);
                case MacroStepKind.Click: return MacroStep.Click(0.5, 0.5, false);
                case MacroStepKind.Wait: return MacroStep.Wait(1000);
                case MacroStepKind.ClickPicture: return MacroStep.ClickPicture(null, null, 3000);
                case MacroStepKind.WaitPicture: return MacroStep.WaitPicture(null, null, 10000, false);
                case MacroStepKind.Move: return MacroStep.Move(0.5, 0.5);
                case MacroStepKind.Drag: return MacroStep.Drag(0.4, 0.5, 0.6, 0.5, 500, true);
                case MacroStepKind.Scroll: return MacroStep.Scroll(0.5, 0.5, 3);
                case MacroStepKind.Repeat: return MacroStep.Repeat(RepeatMode.Times, 5);
                default:
                {
                    // Typing is nearly always something to say in chat.
                    MacroStep type = MacroStep.Type("");
                    type.InChat = true;
                    return type;
                }
            }
        }

        void AskNewStep(MacroStepKind kind)
        {
            using (MacroStepDialog d = new MacroStepDialog(NewStep(kind), kit, anywhere))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                if (kind == MacroStepKind.Repeat) AddRepeat(d.Result);
                else AddStep(d.Result);
            }
        }

        // A repeat goes round what is there already - the usual wish is "all
        // of this, over again" - with its end after the last step. Both can
        // be dragged to take in less.
        public void AddRepeat(MacroStep repeat)
        {
            steps.Insert(0, repeat);
            steps.Add(MacroStep.EndRepeat());
            RebuildSteps();
        }

        void AskEditStep(int index)
        {
            using (MacroStepDialog d = new MacroStepDialog(steps[index], kit, anywhere))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                steps[index] = d.Result;
                RebuildSteps();
            }
        }

        // ---------- where it plays, and its key ----------

        void ChangeWhere(bool onScreen)
        {
            anywhere = onScreen;
            ApplyWhere();
            RebuildSteps();         // the summary's limit is not the same
        }

        bool CanRun { get { return anywhere || clients.Length > 0; } }

        // The Roblox window picker is for Roblox windows only; on the screen
        // there is nothing to choose.
        void ApplyWhere()
        {
            lblClient.Visible = cmbClient.Visible = !anywhere;
            clientRowShown = !anywhere;
            btnRecord.Enabled = btnTry.Enabled = CanRun;
            ShowReady();
        }

        // What the line under Record says while nothing is going on.
        void ShowReady()
        {
            if (anywhere) Status("Records what you do on your screen. Press F8 to stop.", false);
            else if (clients.Length == 0)
                Status("Open a Roblox window to record or try this, or set Plays on to \"Anywhere on my screen\".", true);
            else Status("Records what you press and click in the Roblox window. Press F8 to stop.", false);
        }

        // While something is recording or playing, nothing else is started.
        void Busy(bool recording, bool trying)
        {
            bool going = recording || trying;
            cmbWhere.Enabled = !going;
            btnTry.Enabled = !going && CanRun;
            btnRecord.Enabled = recording || (!going && CanRun);
        }

        // Null when the key is free for this macro; otherwise why not. Its
        // own present key is no clash, and the name it was opened with is
        // the one the rest of the app knows it by.
        string KeyProblem(byte vk)
        {
            string editing = original.Name;
            if (kit.Macros != null) return kit.Macros.KeyUseProblem(vk, KeyUse.MACRO, editing);
            return KeyUse.Problem(vk, KeyUse.MACRO, editing, null);
        }

        void ChooseKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                SetKey(d.Captured);
            }
        }

        // 0 takes the key away. Null when it was set; otherwise why not, and
        // the same line is on the status line.
        public string SetKey(byte vk)
        {
            string why = vk == 0 ? null : KeyProblem(vk);
            if (why != null) { Status(why, true); return why; }
            key = vk;
            Flow();
            RebuildSteps();
            ShowReady();
            return null;
        }

        // ---------- record and try ----------

        void FillClients()
        {
            clients = kit.RunningClients();
            cmbClient.Items.Clear();
            foreach (WatchedClient c in clients) cmbClient.Items.Add(WatchUi.ClientName(c));
            if (clients.Length > 0) cmbClient.SelectedIndex = 0;
            ApplyWhere();
        }

        IntPtr WindowOfChosen()
        {
            int i = cmbClient.SelectedIndex;
            if (i < 0 || i >= clients.Length) { Status("Choose a Roblox window first.", true); return IntPtr.Zero; }
            IntPtr hwnd = WindowCapture.GameWindow(clients[i].Pid);
            if (hwnd == IntPtr.Zero) Status("That Roblox window is hidden. It may be in the tray.", true);
            return hwnd;
        }

        void Status(string text, bool warn)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        void Record()
        {
            if (anywhere) { RecordScreen(); return; }
            IntPtr hwnd = WindowOfChosen();
            if (hwnd == IntPtr.Zero) return;
            InputSender.ShowClient(hwnd);

            recorder = new MacroRecorder(hwnd, chkMoves.Checked, delegate(List<MacroStep> got) { Recorded(got); });
            if (!recorder.Start())
            {
                recorder.Dispose();
                recorder = null;
                Status("Windows wouldn't let it record.", true);
                return;
            }
            btnRecord.Text = "Stop";
            Busy(true, false);
            Status("Recording - do it in the Roblox window, then press F8.", false);
        }

        // Anything on the screen. The app's windows get out of the way
        // first, and come back when F8 ends it.
        void RecordScreen()
        {
            if (MacroRecorder.Recording) { Status("Another recording is already running. Press F8 to end it.", true); return; }
            recorder = MacroRecorder.ForScreen(chkMoves.Checked, delegate(List<MacroStep> got) { Recorded(got); });
            if (!recorder.Start())
            {
                recorder.Dispose();
                recorder = null;
                Status("Windows wouldn't let it record.", true);
                return;
            }
            btnRecord.Text = "Stop";
            Busy(true, false);
            Status("Recording your screen. Press F8 when you are done.", false);
            AppWindows.Away(this);
        }

        // A recording ended: its steps go on the end of the list. Public so a
        // test can end one without a keyboard.
        public void Recorded(List<MacroStep> got)
        {
            MacroRecorder r = recorder;
            recorder = null;
            if (r != null) r.Dispose();
            steps.AddRange(got);
            RebuildSteps();
            btnRecord.Text = "Record";
            Busy(false, false);
            Status(got.Count == 0
                ? (anywhere ? "Nothing was recorded. Do something on your screen first."
                            : "Nothing was recorded. Press and click inside the Roblox window.")
                : "Recorded " + got.Count + (got.Count == 1 ? " step." : " steps."), got.Count == 0);
            if (anywhere) AppWindows.Back(this);
            else { try { Activate(); } catch { } }
        }

        void TryIt()
        {
            Macro m = Current(nameBox.Text.Length == 0 ? "Try" : nameBox.Text);
            if (m.Steps.Count == 0) { Status("Add a step first.", true); return; }
            string block = m.BlockProblem();
            if (block != null) { Status(block, true); return; }
            if (anywhere) { TryOnScreen(m); return; }
            if (!m.IsSession && m.DurationMs > Macro.MAX_MS) { Status("Keep it under a minute.", true); return; }
            IntPtr hwnd = WindowOfChosen();
            if (hwnd == IntPtr.Zero) return;

            PlayOptions o = new PlayOptions();
            o.Regions = new List<WatchRegion>(kit.Store.Regions);
            int chosen = cmbClient.SelectedIndex;
            if (chosen >= 0 && chosen < clients.Length) o.PlaceId = clients[chosen].PlaceId;

            btnTry.Enabled = false;
            Status(m.IsSession
                ? "Playing until you press " + (kit.Macros == null ? "the stop key" : MacroKeys.Name(kit.Macros.StopKey))
                  + " or switch to another window."
                : "Playing...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = MacroPlayer.Play(m, hwnd, o);
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

        // On the screen it is the main window that plays it, so this one
        // only watches for the end. The windows are out of the way meanwhile.
        void TryOnScreen(Macro m)
        {
            IMacroHost host = kit.Macros;
            if (host == null) { Status("Playing on your screen needs the main window open.", true); return; }
            string why = m.Problem();
            if (why != null) { Status(why, true); return; }
            AppWindows.Away(this);
            why = host.PlayAnywhere(m);
            if (why != null)
            {
                AppWindows.Back(this);
                Status(why, true);
                return;
            }
            tryTag = MacroPlay.PlayTag(m.Name);
            Busy(false, true);
            Status("Playing - press " + MacroKeys.Name(host.StopKey) + " to stop.", false);
            tryPoll.Start();
        }

        // Public so a test can see a play end without waiting a quarter of a second.
        public void CheckTry()
        {
            if (tryTag == null) { tryPoll.Stop(); return; }
            if (kit.Macros != null && kit.Macros.IsPlaying(tryTag)) return;
            tryPoll.Stop();
            tryTag = null;
            Busy(false, false);
            Status("Done playing.", false);
            AppWindows.Back(this);
        }

        // ---------- save ----------

        // The macro as the window stands, settings and all. Hidden rows keep
        // what they had.
        Macro Current(string name)
        {
            Macro m = new Macro();
            m.Name = (name ?? "").Trim();
            foreach (MacroStep s in steps) m.Steps.Add(s.Copy());
            m.Anywhere = anywhere;
            m.Key = key;
            m.KeyMode = keyMode;
            m.Times = times;
            m.SpeedPct = speed;
            return m;
        }

        public bool TrySave(string name)
        {
            Macro m = Current(name);
            string problem = MacroForm.NameProblem(kit.Store, m.Name, original) ?? m.Problem();
            // Another feature may have taken the key since it was chosen.
            if (problem == null && m.Key != 0) problem = KeyProblem(m.Key);
            if (problem != null)
            {
                lblProblem.Text = problem;
                return false;
            }
            Result = m;
            return true;
        }

        // ---------- for tests, and anything else driving the form ----------

        public Macro Draft { get { return Current(nameBox.Text); } }
        public void SetName(string name) { nameBox.Text = name; }
        public void SetWhere(bool onScreen) { cmbWhere.SelectedIndex = onScreen ? 1 : 0; }
        public void SetKeyMode(MacroKeyMode mode) { cmbKeyMode.SelectedIndex = (int)mode; }
        public void SetRepeat(int count) { int i = repeatValues.IndexOf(count); if (i >= 0) cmbRepeat.SelectedIndex = i; }
        public void SetSpeed(int pct) { int i = speedValues.IndexOf(pct); if (i >= 0) cmbSpeed.SelectedIndex = i; }
        public bool RepeatShown { get { return repeatShown; } }
        public bool KeyModeShown { get { return keyModeShown; } }
        public bool ClientRowShown { get { return clientRowShown; } }
        public bool CanRecord { get { return btnRecord.Enabled; } }
        public bool CanTry { get { return btnTry.Enabled; } }
    }
}
