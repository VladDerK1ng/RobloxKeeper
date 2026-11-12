using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Automation: every tool that does something for you, by name, with a
    // line saying what it is for - so nobody has to know that the
    // autoclicker used to live three windows deep.
    class AutomationPage : ShellPage
    {
        // Six tools and the Stop button, then Quick Record, in the page's
        // height: three rows of two tiles, Stop as a strip the width of the
        // page, and Quick Record under it. A fourth row of tiles would not fit.
        const int TILE_W = 424, TILE_H = 108, GAP = 14, ROW_GAP = 10, STOP_H = 76;
        const int BUTTON_Y = TILE_H - 38;

        readonly Timer tick = new Timer();
        readonly List<Label> states = new List<Label>();
        readonly List<Func<string>> stateOf = new List<Func<string>>();
        Button btnStop;
        readonly QuickRecordCard quick;
        int scriptCount;

        public AutomationPage(IShell shell, Size size) : base(shell, size)
        {
            Head("Automation", "Tools that play for you. Nothing runs until you turn it on.");

            Tile(0, "Autoclicker", "Clicks for you: where your mouse is, at a spot you pick, or on a picture.",
                 "Set it up", delegate { shell.OpenClicker(); },
                 delegate
                 {
                     IMacroHost m = shell.Macros;
                     if (m == null) return "";
                     if (m.ClickerOn) return "On - " + MacroKeys.Name(m.Clicker.ToggleVk) + " stops it";
                     return m.Clicker.Enabled ? "Off - press " + MacroKeys.Name(m.Clicker.ToggleVk) + " in Roblox to start" : "Not set up yet";
                 });
            Tile(1, "Macros", "Record what you do once and play it back, or build it step by step. A macro can also click a picture.",
                 "Open", delegate { shell.OpenMacros(); },
                 delegate { return shell.Kit == null ? "" : Count(shell.Kit.Store.Macros.Count, "macro"); });
            Tile(3, "Watchers", "Get an alert here or on Discord when a word, chat message or picture shows up in a Roblox window.",
                 "Open", delegate { shell.OpenWatchers(); },
                 delegate
                 {
                     if (shell.Kit == null) return "";
                     int on = 0;
                     foreach (Watcher w in shell.Kit.Store.Watchers) if (w.Enabled) on++;
                     return on == 0 ? Count(shell.Kit.Store.Watchers.Count, "watcher") + ", none turned on" : on + " turned on";
                 });
            Tile(4, "Rules", "Plays a macro or script for you when you press a key, on a timer, when a watcher finds something, or when a window joins a game.",
                 "Open", delegate { Dialog(delegate(WatchKit k) { return new RulesDialog(k); }); },
                 delegate { return shell.Kit == null ? "" : Count(shell.Kit.Store.Rules.Count, "rule"); });
            Tile(5, "Hunt", "Moves accounts from server to server until a watcher finds what you want, then stays there.",
                 "Open", delegate { Dialog(delegate(WatchKit k) { return new HuntDialog(k); }); },
                 delegate { return shell.Kit != null && shell.Kit.Hunts != null && shell.Kit.Hunts.Running ? "Hunting now" : ""; });

            // The strip across the page: Stop is the one button that must
            // always be easy to find, and it has little to say.
            int fullW = TILE_W * 2 + GAP;
            Card stop = WatchUi.CardAt(this, "STOP EVERYTHING", CardLayout.PAD, HEAD_H + 8 + 3 * (TILE_H + ROW_GAP), fullW, STOP_H);
            Label says = Ui.MutedLabel("Stops all macros, scripts and the autoclicker right away.", Ui.PAD, 34, 8.25f);
            says.MaximumSize = new Size(fullW - Ui.PAD * 2, 0);
            stop.Controls.Add(says);
            btnStop = WatchUi.Primary("Stop", fullW - Ui.PAD - 110, 20, 110, 30);
            btnStop.Click += delegate { if (shell.Macros != null) shell.Macros.StopEverything(); ShowStates(); };
            stop.Controls.Add(btnStop);
            btnStop.BringToFront();
            Label state = Ui.RowLabel("", Ui.PAD, 52, 20, fullW - Ui.PAD * 2 - 130, 8.25f, Theme.Muted);
            stop.Controls.Add(state);
            states.Add(state);
            stateOf.Add(delegate
            {
                IMacroHost m = shell.Macros;
                if (m == null) return "";
                string key = MacroKeys.Name(m.StopKey);
                return m.SessionsRunning == 0 ? "Nothing is playing. " + key + " does this from anywhere."
                                              : Count(m.SessionsRunning, "thing") + " playing. " + key + " stops them too";
            });

            // After Stop, so the states the page and its tests know by number
            // keep their numbers; it sits by Macros, where it belongs.
            Tile(2, "Scripts", "Write a script that plays for you: walk a route, click pictures, chat. Examples to start from.",
                 "Open", delegate { shell.OpenScripts(); CountScripts(); },
                 delegate
                 {
                     IScriptHost h = shell.Scripts;
                     if (h == null) return "";
                     int on = h.ScriptsRunning().Count;
                     return on > 0 ? on + " running" : Count(scriptCount, "script");
                 });

            // Under the tiles: the one tool here that needs no setting up.
            if (shell.Macros != null)
            {
                quick = new QuickRecordCard(shell.Macros, delegate { return shell.Kit; }, delegate { return FindForm(); },
                                            shell.Say, fullW);
                quick.Location = new Point(CardLayout.PAD, HEAD_H + 8 + 3 * (TILE_H + ROW_GAP) + STOP_H + ROW_GAP);
                Controls.Add(quick);
            }

            // Only while the page is being looked at.
            tick.Interval = 1000;
            tick.Tick += delegate { ShowStates(); };
        }

        // The scripts are counted when the page comes into view and when the
        // Scripts window closes - not every second: counting reads the folder.
        void CountScripts()
        {
            try { scriptCount = shell.Scripts == null ? 0 : shell.Scripts.Store.Names().Count; }
            catch { scriptCount = 0; }
        }

        static string Count(int n, string thing)
        {
            return n == 0 ? "No " + thing + "s yet" : n + " " + thing + (n == 1 ? "" : "s");
        }

        Card TileCard(int index, string title)
        {
            int x = CardLayout.PAD + (index % 2) * (TILE_W + GAP);
            int y = HEAD_H + 8 + (index / 2) * (TILE_H + ROW_GAP);
            return WatchUi.CardAt(this, title.ToUpperInvariant(), x, y, TILE_W, TILE_H);
        }

        void Tile(int index, string title, string says, string button, Action open, Func<string> state)
        {
            Card card = TileCard(index, title);
            Label what = Ui.MutedLabel(says, Ui.PAD, 35, 8.25f);
            what.MaximumSize = new Size(TILE_W - Ui.PAD * 2, 0);
            card.Controls.Add(what);
            Button b = WatchUi.Secondary(button, TILE_W - Ui.PAD - 110, BUTTON_Y, 110, 30);
            b.Click += delegate { open(); ShowStates(); };
            card.Controls.Add(b);
            // Two lines of the words above reach the top of the button. The
            // first control added is drawn on top, so the button is put there.
            b.BringToFront();
            Label s = Ui.RowLabel("", Ui.PAD, BUTTON_Y, 30, TILE_W - Ui.PAD * 2 - 120, 8.25f, Theme.Text);
            card.Controls.Add(s);
            states.Add(s);
            stateOf.Add(state);
        }

        void Dialog(Func<WatchKit, Form> make)
        {
            if (shell.Kit == null) return;
            using (Form f = make(shell.Kit)) f.ShowDialog(FindForm());
        }

        public void ShowStates()
        {
            for (int i = 0; i < states.Count; i++)
            {
                string now;
                try { now = stateOf[i]() ?? ""; } catch { now = ""; }
                if (states[i].Text != now) states[i].Text = now;
            }
            btnStop.Enabled = shell.Macros != null && shell.Macros.SessionsRunning > 0;
        }

        public string StateText(int tile) { return states[tile].Text; }

        public QuickRecordCard Quick { get { return quick; } }

        protected override void OnEnterPage() { CountScripts(); ShowStates(); tick.Start(); if (quick != null) quick.Watch(true); }
        protected override void OnLeavePage() { tick.Stop(); if (quick != null) quick.Watch(false); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tick.Dispose();
            base.Dispose(disposing);
        }
    }

    // Quick Record, like TinyTask: record what you do on the screen, then
    // play it back as often and as fast as you like. No Roblox window is
    // needed. The last recording is kept by the main window, so it is still
    // there after a restart.
    class QuickRecordCard : Card
    {
        public const int HEIGHT = 140;

        readonly IMacroHost host;
        readonly Func<WatchKit> kit;
        readonly Func<Form> window;
        readonly Action<string> say;
        readonly Timer poll = new Timer();
        readonly Button btnRecord, btnPlay, btnSave, btnScript;
        readonly ThemedPicker cmbRepeat, cmbSpeed;
        readonly ThemedCheckBox chkMoves;
        readonly Label lblStatus;
        MacroRecorder recorder;
        bool playing;
        bool loading;           // the pickers are being set, not chosen
        string held;            // a line to keep up until the next thing is done
        bool heldWarn;

        public QuickRecordCard(IMacroHost host, Func<WatchKit> kit, Func<Form> window, Action<string> say, int width)
        {
            this.host = host;
            this.kit = kit;
            this.window = window;
            this.say = say;
            Size = new Size(width, HEIGHT);
            Controls.Add(Ui.SectionTitle("QUICK RECORD"));

            Label what = Ui.MutedLabel("Record anything you do on your screen, then play it back. No Roblox window needed.",
                                       Ui.PAD, 34, 8.25f);
            what.MaximumSize = new Size(width - Ui.PAD * 2, 0);
            Controls.Add(what);

            btnRecord = WatchUi.Primary("Record", Ui.PAD, 62, 100, 30);
            btnRecord.Click += delegate { RecordClicked(); };
            Controls.Add(btnRecord);
            btnPlay = WatchUi.Secondary("Play", Ui.PAD + 108, 62, 100, 30);
            btnPlay.Click += delegate { PlayClicked(); };
            Controls.Add(btnPlay);

            Controls.Add(Ui.RowLabel("Repeat", 244, 62, 30, 50, 9f, Theme.Muted));
            cmbRepeat = Ui.DarkCombo(298, 64, 130);
            foreach (int t in MacroForm.RepeatTimes) cmbRepeat.Items.Add(MacroForm.TimesName(t));
            cmbRepeat.SelectedIndex = 0;
            cmbRepeat.SelectedIndexChanged += delegate { ChoicesChanged(); };
            Controls.Add(cmbRepeat);
            Controls.Add(Ui.RowLabel("Speed", 444, 62, 30, 46, 9f, Theme.Muted));
            cmbSpeed = Ui.DarkCombo(492, 64, 120);
            foreach (int p in MacroForm.SpeedPcts) cmbSpeed.Items.Add(MacroForm.SpeedName(p));
            cmbSpeed.SelectedIndex = Array.IndexOf(MacroForm.SpeedPcts, 100);
            cmbSpeed.SelectedIndexChanged += delegate { ChoicesChanged(); };
            Controls.Add(cmbSpeed);

            btnSave = WatchUi.Secondary("Save as macro", width - Ui.PAD - 130, 62, 130, 30);
            btnSave.Click += delegate { AskName(); };
            Controls.Add(btnSave);

            // Under "Save as macro", and the status line stops short of it:
            // a label added first is drawn on top of a button added after.
            btnScript = WatchUi.Secondary("Save as script", width - Ui.PAD - 130, 100, 130, 30);
            btnScript.Click += delegate { AskScriptName(); };
            Controls.Add(btnScript);

            chkMoves = Ui.DarkCheck("Mouse movement too", Ui.PAD, 106, 8.25f);
            chkMoves.AutoSize = true;
            Controls.Add(chkMoves);
            lblStatus = Ui.RowLabel("", Ui.PAD + 170, 100, 28, width - Ui.PAD * 2 - 170 - 138, 8.25f, Theme.Muted);
            Controls.Add(lblStatus);

            poll.Interval = 250;
            poll.Tick += delegate { Tick(); };
            Reload();
        }

        public string StatusText { get { return lblStatus.Text; } }
        public string RecordText { get { return btnRecord.Text; } }
        public string PlayText { get { return btnPlay.Text; } }
        public bool CanPlay { get { return btnPlay.Enabled; } }
        public bool CanSave { get { return btnSave.Enabled; } }
        public bool CanSaveAsScript { get { return btnScript.Visible && btnScript.Enabled; } }

        // Polling only while the page shows; the recorder and a play that
        // are going don't depend on it.
        public void Watch(bool on)
        {
            if (on) { Reload(); poll.Start(); }
            else poll.Stop();
        }

        int RepeatChosen { get { return MacroForm.RepeatTimes[Math.Max(0, cmbRepeat.SelectedIndex)]; } }
        int SpeedChosen { get { return MacroForm.SpeedPcts[Math.Max(0, cmbSpeed.SelectedIndex)]; } }
        static string PlayTag { get { return MacroPlay.PlayTag(MacroPlay.QUICK_NAME); } }
        string StopName { get { return MacroKeys.Name(host.StopKey); } }

        public static string Describe(Macro q)
        {
            if (q == null || q.Steps.Count == 0) return "Nothing recorded yet.";
            int secs = (int)Math.Round(q.DurationMs / 1000.0);
            return "Recorded " + q.Steps.Count + (q.Steps.Count == 1 ? " step" : " steps") + " - "
                 + (secs <= 0 ? "less than a second." : secs + (secs == 1 ? " second." : " seconds."));
        }

        void Status(string text, bool warn)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        // A line that stays until the next thing is done, even if the page is
        // left and come back to.
        void Hold(string text, bool warn)
        {
            held = text;
            heldWarn = warn;
            Status(text, warn);
        }

        // What the buttons and the line say, from what is true now.
        public void Reload()
        {
            Macro q = host.QuickRecording;
            if (q != null)
            {
                loading = true;
                int t = Array.IndexOf(MacroForm.RepeatTimes, q.Times);
                if (t >= 0) cmbRepeat.SelectedIndex = t;
                int s = Array.IndexOf(MacroForm.SpeedPcts, q.SpeedPct);
                if (s >= 0) cmbSpeed.SelectedIndex = s;
                loading = false;
            }
            playing = host.IsPlaying(PlayTag);
            ShowState(q);
        }

        void ShowState(Macro q)
        {
            bool has = q != null && q.Steps.Count > 0;
            bool recording = recorder != null;
            btnRecord.Text = recording ? "Stop" : "Record";
            btnRecord.Enabled = !playing;
            btnPlay.Text = playing ? "Stop" : "Play";
            btnPlay.Enabled = playing || (has && !recording);
            btnSave.Enabled = has && !recording && !playing;
            // Only where there is a main window to make scripts with.
            WatchKit k = kit();
            btnScript.Visible = k != null && k.Scripts != null;
            btnScript.Enabled = btnSave.Enabled;
            cmbRepeat.Enabled = cmbSpeed.Enabled = chkMoves.Enabled = !recording;
            if (recording) Status("Recording. Press F8 when you're done.", false);
            else if (playing) Status("Playing. Press " + StopName + " to stop.", false);
            else if (held != null) Status(held, heldWarn);
            else Status(Describe(q), false);
        }

        void Tick()
        {
            bool now = host.IsPlaying(PlayTag);
            if (now == playing) return;
            playing = now;
            held = null;
            ShowState(host.QuickRecording);
        }

        // Repeat and Speed are kept on the recording itself, so they are
        // what they were set to when the app is opened again.
        void ChoicesChanged()
        {
            if (loading) return;
            Macro q = host.QuickRecording;
            if (q == null) return;
            q.Times = RepeatChosen;
            q.SpeedPct = SpeedChosen;
            host.QuickRecording = q;
        }

        // ---------- record ----------

        void RecordClicked()
        {
            if (recorder != null) { recorder.Stop(); return; }
            if (MacroRecorder.Recording) { Hold("Another recording is already running. Press F8 to end it.", true); return; }
            recorder = MacroRecorder.ForScreen(chkMoves.Checked, delegate(List<MacroStep> got) { TakeRecording(got); });
            if (!recorder.Start())
            {
                recorder.Dispose();
                recorder = null;
                Hold("Windows wouldn't allow recording.", true);
                return;
            }
            held = null;
            ShowState(host.QuickRecording);
            AppWindows.Away(window());
        }

        // A recording ended: it replaces the last one. Public so a test can
        // end one without a keyboard.
        public void TakeRecording(List<MacroStep> got)
        {
            MacroRecorder r = recorder;
            recorder = null;
            if (r != null) r.Dispose();
            if (got.Count == 0)
            {
                held = "Nothing was recorded. Do something on your screen first.";
                heldWarn = true;
            }
            else
            {
                Macro q = new Macro();
                q.Name = MacroPlay.QUICK_NAME;
                q.Anywhere = true;
                q.Times = RepeatChosen;
                q.SpeedPct = SpeedChosen;
                q.Steps.AddRange(got);
                host.QuickRecording = q;
                held = null;
            }
            ShowState(host.QuickRecording);
            // Back in front, so what was recorded is seen.
            AppWindows.Back(window());
        }

        // ---------- play ----------

        void PlayClicked()
        {
            if (playing) { host.StopTag(PlayTag); return; }
            string why = StartPlay();
            if (why != null) Hold(why, true);
        }

        // Null when it started; otherwise why not. The windows get out of
        // the way and are left that way: the stop key ends it.
        public string StartPlay()
        {
            Macro q = host.QuickRecording;
            if (q == null || q.Steps.Count == 0) return "Record something first.";
            q.Times = RepeatChosen;
            q.SpeedPct = SpeedChosen;
            host.QuickRecording = q;
            AppWindows.Away(window());
            string why = host.PlayAnywhere(q);
            if (why != null)
            {
                AppWindows.Back(window());
                return why;
            }
            held = null;
            playing = true;
            ShowState(q);
            return null;
        }

        // ---------- save as a macro ----------

        string NameProblem(string name)
        {
            WatchKit k = kit();
            if (k == null) return "The macros aren't ready yet. Try again in a moment.";
            if (string.Equals((name ?? "").Trim(), MacroPlay.QUICK_NAME, StringComparison.OrdinalIgnoreCase))
                return "Quick Record uses that name itself. Choose another.";
            return MacroForm.NameProblem(k.Store, name, null);
        }

        void AskName()
        {
            using (MacroNameDialog d = new MacroNameDialog("Save as a macro", NameProblem))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK || d.Chosen == null) return;
                string why = SaveAs(d.Chosen);
                if (why != null) Hold(why, true);
            }
        }

        // The recording, kept as a macro of its own: it plays anywhere, and
        // has no key until it is given one in Macros. Null when it was saved;
        // otherwise why not.
        public string SaveAs(string name)
        {
            Macro q = host.QuickRecording;
            if (q == null || q.Steps.Count == 0) return "Record something first.";
            string why = NameProblem(name);
            if (why != null) return why;
            WatchKit k = kit();
            Macro m = q.Copy();
            m.Name = name.Trim();
            m.Anywhere = true;
            m.Key = 0;
            m.KeyMode = MacroKeyMode.Once;
            m.Times = RepeatChosen;
            m.SpeedPct = SpeedChosen;
            why = m.Problem();
            if (why != null) return why;
            k.Store.Macros.Add(m);
            k.Store.Save();
            host.MacrosSaved();
            if (say != null) say("Macro " + m.Name + " saved from Quick Record.");
            Hold("Saved as " + m.Name + ". To give it a key, open Macros.", false);
            ShowState(q);
            return null;
        }

        // ---------- save as a script ----------

        string ScriptNameProblem(string name)
        {
            WatchKit k = kit();
            if (k == null || k.Scripts == null) return "Scripts aren't ready yet. Try again in a moment.";
            return k.Scripts.Store.NameProblem(name, null);
        }

        void AskScriptName()
        {
            WatchKit k = kit();
            if (k == null || k.Scripts == null) { Hold("Scripts aren't ready yet. Try again in a moment.", true); return; }
            string start = ScriptMaking.UniqueName(k.Scripts.Store, "Recording");
            using (ScriptNameDialog d = new ScriptNameDialog("Save as a script", start, ScriptNameProblem))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK || d.Chosen == null) return;
                string why = SaveAsScript(d.Chosen);
                if (why != null) { Hold(why, true); return; }
                // Shown at once, ready to change.
                using (ScriptsDialog w = new ScriptsDialog(k, d.Chosen)) w.ShowDialog(FindForm());
            }
        }

        // The recording as a script of its own, one line a step. Null when
        // it was made; otherwise why not.
        public string SaveAsScript(string name)
        {
            Macro q = host.QuickRecording;
            if (q == null || q.Steps.Count == 0) return "Record something first.";
            WatchKit k = kit();
            string why = ScriptMaking.FromMacro(k == null ? null : k.Scripts, q, name);
            if (why != null) return why;
            if (say != null) say("Script " + name + " saved from Quick Record.");
            Hold("Saved as the script " + name + ".", false);
            ShowState(q);
            return null;
        }

        // For tests: the pickers as someone chose them.
        public void ChooseRepeat(int times)
        {
            int i = Array.IndexOf(MacroForm.RepeatTimes, times);
            if (i >= 0) cmbRepeat.SelectedIndex = i;
        }

        public void ChooseSpeed(int pct)
        {
            int i = Array.IndexOf(MacroForm.SpeedPcts, pct);
            if (i >= 0) cmbSpeed.SelectedIndex = i;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                poll.Dispose();
                if (recorder != null) { recorder.Dispose(); recorder = null; }
            }
            base.Dispose(disposing);
        }
    }

    // Pictures of Roblox windows, saved to this PC and nowhere else.
    static class Shots
    {
        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RobloxKeeper");
            }
        }

        // "RobloxKeeper 2026-11-11 14.03.22 main.png": sorts by when, says whose.
        public static string NameFor(DateTime when, string who)
        {
            System.Text.StringBuilder safe = new System.Text.StringBuilder();
            foreach (char c in who ?? "")
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') safe.Append(c);
            string tail = safe.Length == 0 ? "" : " " + (safe.Length > 30 ? safe.ToString().Substring(0, 30) : safe.ToString());
            return "RobloxKeeper " + when.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) + tail + ".png";
        }

        // Saves a picture of one window. The file's path, or null and why not.
        public static string Take(IntPtr hwnd, string who, string folder, DateTime when, out string problem)
        {
            problem = null;
            if (hwnd == IntPtr.Zero) { problem = "that Roblox window is gone"; return null; }
            if (Native.IsIconic(hwnd)) { problem = "it's minimized, so open it and try again"; return null; }
            Pixels shot = WindowCapture.Grab(hwnd);
            if (shot == null || shot.IsEmpty) { problem = "Windows couldn't capture it"; return null; }
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, NameFor(when, who));
                // Two in the same second: the later one is numbered.
                for (int n = 2; File.Exists(path); n++)
                    path = Path.Combine(folder, Path.GetFileNameWithoutExtension(NameFor(when, who)) + " (" + n + ").png");
                using (Bitmap b = shot.ToBitmap()) b.Save(path, ImageFormat.Png);
                return path;
            }
            catch (Exception ex) { problem = ex.Message; return null; }
        }

        // The newest first, and no more than a page is worth.
        public static List<string> Newest(string folder, int max)
        {
            List<string> files = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return files;
                FileInfo[] all = new DirectoryInfo(folder).GetFiles("*.png");
                Array.Sort(all, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 0; i < all.Length && i < max; i++) files.Add(all[i].FullName);
            }
            catch { }
            return files;
        }
    }

    class ShotsPage : ShellPage
    {
        const int SHOWN = 60;

        readonly CardCanvas canvas = new CardCanvas();
        readonly Dictionary<string, Bitmap> small = new Dictionary<string, Bitmap>();
        Button btnKey;
        Label lblSays;
        bool loading;

        public ShotsPage(IShell shell, Size size) : base(shell, size)
        {
            lblSays = Head("Screenshots", "");

            Button take = WatchUi.Primary("Take one now", Width - 20 - 130, 14, 130, 30);
            take.Click += delegate { shell.TakeScreenshots(); Reload(); };
            Controls.Add(take);
            Button folder = WatchUi.Secondary("Open the folder", Width - 20 - 130 - 8 - 130, 14, 130, 30);
            folder.Click += delegate
            {
                try { Directory.CreateDirectory(Shots.Folder); Process.Start(Shots.Folder); } catch { }
            };
            Controls.Add(folder);
            btnKey = WatchUi.Secondary("", Width - 20 - 130 - 8 - 130 - 8 - 150, 14, 150, 30);
            btnKey.Click += delegate { ChooseKey(); };
            Controls.Add(btnKey);

            canvas.Bounds = Body;
            canvas.ItemClicked += delegate(CardItem item, Point at)
            {
                try { Process.Start((string)item.Tag); } catch { }
            };
            Controls.Add(canvas);
            ShowKey();
        }

        public CardCanvas Canvas { get { return canvas; } }

        void ShowKey()
        {
            byte key = shell.ShotKey;
            btnKey.Text = key == 0 ? "Choose a key" : "Key: " + MacroKeys.Name(key);
            lblSays.Text = (key == 0 ? "Press \"Take one now\"" : "Press " + MacroKeys.Name(key) + " in Roblox")
                         + " to save a screenshot. Screenshots stay on this PC.";
        }

        void ChooseKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK || d.Captured == 0) return;
                string why = shell.SetShotKey(d.Captured);
                if (why != null) MessageBox.Show(FindForm(), why, "Screenshot key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowKey();
            }
        }

        protected override void OnEnterPage() { ShowKey(); Reload(); }

        protected override void OnLeavePage()
        {
            foreach (Bitmap b in small.Values) b.Dispose();
            small.Clear();
        }

        // The pictures, shrunk to a card's size off the window's thread: a
        // full one is eight megabytes decoded, and sixty of them is not
        // something to hold on to.
        public void Reload()
        {
            if (loading) return;
            loading = true;
            InBackground(delegate
            {
                Dictionary<string, Bitmap> got = new Dictionary<string, Bitmap>();
                foreach (string path in Shots.Newest(Shots.Folder, SHOWN))
                {
                    try
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (Image full = Image.FromStream(fs, false, false))
                            got[path] = new Bitmap(full, new Size(CardLayout.CARD_W * 2, Math.Max(1, CardLayout.CARD_W * 2 * full.Height / Math.Max(1, full.Width))));
                    }
                    catch { }
                }
                return got;
            },
            delegate(Dictionary<string, Bitmap> got)
            {
                loading = false;
                if (got == null) return;
                if (!IsShowing) { foreach (Bitmap b in got.Values) b.Dispose(); return; }
                List<Bitmap> old = new List<Bitmap>(small.Values);
                small.Clear();
                foreach (KeyValuePair<string, Bitmap> p in got) small[p.Key] = p.Value;
                Rebuild();
                foreach (Bitmap b in old) b.Dispose();
            });
        }

        void Rebuild()
        {
            CardSection today = new CardSection(), earlier = new CardSection();
            today.Title = "Today";
            earlier.Title = "Earlier";
            today.Empty = "No screenshots yet today.";
            List<string> paths = new List<string>(small.Keys);
            paths.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            foreach (string path in paths)
            {
                DateTime when = File.GetLastWriteTime(path);
                CardItem item = new CardItem();
                item.Title = when.ToString("HH:mm", CultureInfo.InvariantCulture);
                item.Line = when.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
                item.Image = small[path];
                item.Tag = path;
                (when.Date == DateTime.Today ? today : earlier).Items.Add(item);
            }
            List<CardSection> sections = new List<CardSection>();
            sections.Add(today);
            if (earlier.Items.Count > 0) sections.Add(earlier);
            canvas.Show(sections);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Bitmap b in small.Values) b.Dispose();
            base.Dispose(disposing);
        }
    }
}
