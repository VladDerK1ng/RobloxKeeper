using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // What to call a script. Asked when one is made or renamed.
    class ScriptNameDialog : Form
    {
        const int W = 380;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        readonly TextBox box;
        readonly Label says;
        readonly Func<string, string> problemWith;

        public string Chosen;

        // problemWith: why a name can't be used, or null when it can.
        public ScriptNameDialog(string title, string start, Func<string, string> problemWith)
        {
            this.problemWith = problemWith;
            int footY = TOP + 100 + 12;
            WatchUi.Frame(this, title, W, footY + 32 + 12);
            Card card = WatchUi.CardAt(this, "NAME", 12, TOP, W - 24, 100);
            box = WatchUi.Input(Ui.PAD, 40, W - 24 - Ui.PAD * 2);
            box.MaxLength = ScriptStore.MAX_NAME;
            box.Text = start ?? "";
            card.Controls.Add(box);
            says = Ui.MutedLabel("Name it after what it does, like \"Farm coins\".", Ui.PAD, 68, 8.25f);
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
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }

        public string Says { get { return says.Text; } }

        public bool Accept(string typed)
        {
            string name = (typed ?? "").Trim();
            string why = problemWith == null ? ScriptStore.NameProblem(name) : problemWith(name);
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

    // The licences of what is built in, in a box that scrolls.
    class NoticesDialog : Form
    {
        const int W = 600, H = 480;
        const int TOP = WatchUi.TITLEBAR_H + 6;

        public NoticesDialog(string notices)
        {
            WatchUi.Frame(this, "Licences", W, H);
            int footY = H - 32 - 12;
            Card card = WatchUi.CardAt(this, "WHAT IS BUILT IN", 12, TOP, W - 24, footY - 12 - TOP);
            TextBox text = WatchUi.ReadOnlyBox(Ui.PAD, 38, W - 24 - Ui.PAD * 2, footY - 12 - TOP - 38 - 12);
            text.Text = string.IsNullOrEmpty(notices) ? "There are no licences in this build." : WatchUi.Lines(notices);
            text.Select(0, 0);
            card.Controls.Add(text);
            Button close = WatchUi.Primary("Close", W - 12 - 110, footY, 110, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }
    }

    // One function in the list: its name, and a line on what it does. A
    // click asks for it to be put into the script.
    class FunctionRow : Control
    {
        public readonly ScriptFunction Function;
        bool hot;
        readonly Font nameFont, saysFont;
        readonly int saysHeight;

        public FunctionRow(ScriptFunction f, int width)
        {
            Function = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            nameFont = Ui.F("Consolas", 9.5f, FontStyle.Bold);
            saysFont = Ui.F("Segoe UI", 8f);
            saysHeight = TextRenderer.MeasureText(f.Says, saysFont, new Size(width - 16, int.MaxValue),
                                                  TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Size = new Size(width, 24 + saysHeight + 6);
            Cursor = Cursors.Hand;
            BackColor = Theme.Card;
            TabStop = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { nameFont.Dispose(); saysFont.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(hot ? Theme.Button : Theme.Card);
            TextRenderer.DrawText(g, Function.Name, nameFont, new Rectangle(8, 3, Width - 16, 18), LuaEditor.FunctionColour,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Function.Says, saysFont, new Rectangle(8, 24, Width - 16, saysHeight), Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    // The Functions list, by heading.
    class FunctionList : ScrollPanel
    {
        public event Action<ScriptFunction> Picked;
        public readonly List<FunctionRow> Rows = new List<FunctionRow>();

        // A click on a function. Public so a test can make one.
        public void Pick(ScriptFunction f)
        {
            if (Picked != null) Picked(f);
        }

        public FunctionList(int width, int height)
        {
            Size = new Size(width, height);
            BackColor = Theme.Card;
            AutoScroll = true;
            HandleCreated += delegate { DarkScrollbars.Apply(this); };
            int inner = width - SystemInformation.VerticalScrollBarWidth - 2;
            string group = null;
            int y = 0;
            foreach (ScriptFunction f in ScriptFunctions.All)
            {
                if (f.Group != group)
                {
                    group = f.Group;
                    Label head = Ui.RowLabel(group.ToUpperInvariant(), 8, y + 6, 20, inner - 8, 7.5f, Theme.Muted);
                    head.Font = Ui.F("Segoe UI", 7.5f, FontStyle.Bold);
                    Controls.Add(head);
                    y += 28;
                }
                FunctionRow row = new FunctionRow(f, inner);
                row.Location = new Point(0, y);
                ScriptFunction pick = f;
                row.Click += delegate { Pick(pick); };
                Controls.Add(row);
                Rows.Add(row);
                y += row.Height + 2;
            }
        }
    }

    // Scripts: the list, an editor, what a script can do, and how it runs.
    class ScriptsDialog : Form
    {
        const int W = 1000, H = 690;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int ROW1_H = 400;
        const int ROW2_Y = TOP + ROW1_H + 8;
        const int ROW2_H = H - 12 - ROW2_Y;
        const int LEFT_W = 212, MID_X = 12 + LEFT_W + 8, MID_W = 528, RIGHT_X = MID_X + MID_W + 8, RIGHT_W = W - 12 - RIGHT_X;
        const int RUN_W = 560, OUT_X = 12 + RUN_W + 8, OUT_W = W - 12 - OUT_X;
        const int MAX_OUTPUT = 500;
        const int BANNER_H = 62;
        const string BANNER_TEXT = "This script came from someone else. Read it before you run it - a script controls your keyboard and mouse.";
        const string KEY_HINT = "Roblox gets the key too, so pick one the game doesn't use.";
        const string FRONT_NAME = "The Roblox window in front", SCREEN_NAME = "Anywhere on my screen";
        const string TEMPLATE = "-- Write your script here.\r\n-- Click a function on the right to add it. Press Ctrl+S to save, then Run.\r\n\r\n";

        public static readonly Color ProblemColour = Color.FromArgb(243, 139, 168);

        class OutLine
        {
            public string Text;
            public bool Problem, Note;
        }

        readonly WatchKit kit;
        readonly IScriptHost host;
        readonly ScriptStore store;
        readonly Dictionary<string, List<OutLine>> outputs = new Dictionary<string, List<OutLine>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> runLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> playsValues = new List<string>();
        readonly Timer tick = new Timer();

        ListBox list;
        LuaEditor editor;
        Label lblName, lblStatus, lblKeyLine, bannerSays;
        LinkLabel lnkSave, lnkRename, lnkRemove, lnkExport, lnkNoKey;
        Button btnRun, btnPicture, btnKey;
        ThemedPicker cmbPlays;
        Panel banner;
        Button btnTrust;
        ThemedCheckBox chkDiscord;
        RichTextBox output;
        FunctionList functions;

        string current;
        bool loading;           // controls being filled, not chosen
        bool subscribed;
        bool bannerOn;          // Visible reads false until the window is shown
        string statusText = "";
        bool statusWarn;

        // How unsaved changes are asked about: Yes saves, No leaves them,
        // Cancel stays. A test answers for itself.
        public Func<string, DialogResult> Ask;

        public ScriptsDialog(WatchKit kit) : this(kit, null) { }

        // openOn: the script to show first, or null for the first in the list.
        public ScriptsDialog(WatchKit kit, string openOn)
        {
            this.kit = kit;
            host = kit.Scripts;
            store = host.Store;
            Ask = AskWithBox;

            WatchUi.Frame(this, "Scripts", W, H);
            BuildList();
            BuildEditor();
            BuildFunctions();
            BuildRun();
            BuildOutput();

            host.ScriptOutput += OnOutput;
            host.ScriptLine += OnLine;
            host.ScriptStatus += OnStatus;
            host.ScriptEnded += OnEnded;
            subscribed = true;

            tick.Interval = 1000;
            tick.Tick += delegate { RefreshRunning(); };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!ConfirmLeave()) e.Cancel = true; };
            FormClosed += delegate { Unhook(); };

            ReloadList();
            string first = openOn != null ? store.Find(openOn) : null;
            if (first == null && list.Items.Count > 0) first = (string)list.Items[0];
            LoadCurrent(first);
            if (first != null) SelectInList(first);
            RefreshRunning();
            tick.Start();
            Shown += delegate { editor.Focus(); };
        }

        void Unhook()
        {
            if (!subscribed) return;
            subscribed = false;
            tick.Stop();
            host.ScriptOutput -= OnOutput;
            host.ScriptLine -= OnLine;
            host.ScriptStatus -= OnStatus;
            host.ScriptEnded -= OnEnded;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Unhook(); tick.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------- what a test can look at ----------

        public string Current { get { return current; } }
        public bool Dirty { get { return editor.Dirty; } }
        public LuaEditor Editor { get { return editor; } }
        public string StatusText { get { return lblStatus.Text; } }
        public bool StatusIsWarning { get { return lblStatus.ForeColor == Theme.Amber; } }
        public string KeyLine { get { return lblKeyLine.Text; } }
        public bool KeyLineIsWarning { get { return lblKeyLine.ForeColor == Theme.Amber; } }
        public string KeyText { get { return btnKey.Text; } }
        public string RunText { get { return btnRun.Text; } }
        public string PlaysOnText { get { return cmbPlays.Text; } }
        public bool SummaryToDiscord { get { return chkDiscord.Checked; } }
        public string NameText { get { return lblName.Text; } }
        public bool BannerShown { get { return bannerOn; } }
        public string BannerText { get { return bannerSays.Text; } }
        public string TrustText { get { return btnTrust.Text; } }
        public FunctionList Functions { get { return functions; } }

        public List<string> Listed
        {
            get
            {
                List<string> names = new List<string>();
                foreach (object o in list.Items) names.Add((string)o);
                return names;
            }
        }

        public List<string> OutputLines
        {
            get
            {
                List<string> lines = new List<string>();
                List<OutLine> all;
                if (current != null && outputs.TryGetValue(current, out all)) foreach (OutLine l in all) lines.Add(l.Text);
                return lines;
            }
        }

        // ---------- building ----------

        void BuildList()
        {
            Card card = WatchUi.CardAt(this, "SCRIPTS", 12, TOP, LEFT_W, ROW1_H);
            list = new ListBox();
            list.Location = new Point(12, 38);
            list.Size = new Size(LEFT_W - 24, 204);
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Inset;
            list.ForeColor = Theme.Text;
            list.Font = Ui.F("Segoe UI", 9f);
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 26;
            list.IntegralHeight = false;
            list.HandleCreated += delegate { DarkScrollbars.Apply(list); };
            list.DrawItem += DrawScriptRow;
            list.SelectedIndexChanged += delegate { ListChosen(); };
            card.Controls.Add(list);

            Button add = WatchUi.Primary("New script", 12, 250, LEFT_W - 24, 30);
            add.Click += delegate { AskNew(); };
            card.Controls.Add(add);
            Button examples = WatchUi.Secondary("Start from an example", 12, 286, LEFT_W - 24, 30);
            examples.Click += delegate
            {
                ContextMenuStrip menu = DarkMenu.Make();
                for (int i = 0; i < ScriptExamples.Names.Length; i++)
                {
                    int index = i;
                    menu.Items.Add(ScriptExamples.Names[i], null, delegate { AddExample(index); });
                }
                menu.Show(examples, new Point(0, examples.Height));
            };
            card.Controls.Add(examples);

            lnkRename = Ui.RowLink("Rename", 12, 322, 22, 8.25f);
            lnkRename.Click += delegate { AskRename(); };
            card.Controls.Add(lnkRename);
            lnkRemove = Ui.RowLink("Remove", 84, 322, 22, 8.25f);
            lnkRemove.Click += delegate { AskRemove(); };
            card.Controls.Add(lnkRemove);
            LinkLabel import = Ui.RowLink("Import...", 12, 344, 22, 8.25f);
            import.Click += delegate { AskImport(); };
            card.Controls.Add(import);
            lnkExport = Ui.RowLink("Export...", 84, 344, 22, 8.25f);
            lnkExport.Click += delegate { AskExport(); };
            card.Controls.Add(lnkExport);
            LinkLabel licences = Ui.RowLink("Licences", 12, 366, 22, 8.25f);
            licences.Click += delegate { using (NoticesDialog d = new NoticesDialog(host.Notices)) d.ShowDialog(this); };
            card.Controls.Add(licences);
        }

        void BuildEditor()
        {
            Card card = WatchUi.CardAt(this, "EDITOR", MID_X, TOP, MID_W, ROW1_H);
            lblName = Ui.RowLabel("", 100, 8, 22, MID_W - 100 - 80, 9f, Theme.Text);
            lblName.AutoEllipsis = true;
            card.Controls.Add(lblName);
            lnkSave = Ui.RowLink("Save", MID_W - 64, 8, 22, 9f);
            lnkSave.Click += delegate { Save(); };
            card.Controls.Add(lnkSave);

            // A warning for a script that came from someone else. It sits above
            // the editor and pushes it down while it shows, so no code is covered.
            banner = new Panel();
            banner.Location = new Point(12, 38);
            banner.Size = new Size(MID_W - 24, BANNER_H);
            banner.BackColor = Theme.Inset;
            bannerSays = Ui.RowLabel(BANNER_TEXT, 10, 6, BANNER_H - 12, MID_W - 24 - 10 - 140 - 10, 8.25f, Theme.Amber);
            banner.Controls.Add(bannerSays);
            btnTrust = WatchUi.Secondary("I trust this script", MID_W - 24 - 10 - 130, (BANNER_H - 30) / 2, 130, 30);
            btnTrust.Click += delegate { Trust(); };
            banner.Controls.Add(btnTrust);
            banner.Visible = false;
            card.Controls.Add(banner);

            editor = new LuaEditor(ScriptFunctions.Names);
            editor.Location = new Point(12, 38);
            editor.Size = new Size(MID_W - 24, ROW1_H - 38 - 12);
            editor.DirtyChanged += delegate { ShowName(); };
            editor.SaveRequested += delegate { Save(); };
            card.Controls.Add(editor);
        }

        void BuildFunctions()
        {
            Card card = WatchUi.CardAt(this, "FUNCTIONS", RIGHT_X, TOP, RIGHT_W, ROW1_H);
            functions = new FunctionList(RIGHT_W - 16, ROW1_H - 38 - 12);
            functions.Location = new Point(8, 38);
            functions.Picked += delegate(ScriptFunction f) { editor.InsertAtCursor(f.Insert); };
            card.Controls.Add(functions);
        }

        void BuildRun()
        {
            Card card = WatchUi.CardAt(this, "RUN", 12, ROW2_Y, RUN_W, ROW2_H);

            btnRun = WatchUi.Primary("Run", Ui.PAD, 40, 100, 32);
            btnRun.Click += delegate { RunOrStop(); };
            card.Controls.Add(btnRun);
            btnPicture = WatchUi.Secondary("Add a picture", Ui.PAD + 108, 40, 130, 32);
            btnPicture.Click += delegate { ChoosePicture(btnPicture); };
            card.Controls.Add(btnPicture);
            lblStatus = Ui.RowLabel("", Ui.PAD + 250, 38, 36, RUN_W - Ui.PAD * 2 - 250, 8.25f, Theme.Muted);
            card.Controls.Add(lblStatus);

            card.Controls.Add(Ui.RowLabel("Plays on", Ui.PAD, 86, 26, 70, 9f, Theme.Muted));
            cmbPlays = Ui.DarkCombo(Ui.PAD + 76, 86, 300);
            cmbPlays.SelectedIndexChanged += delegate { PlaysOnChosen(); };
            card.Controls.Add(cmbPlays);

            card.Controls.Add(Ui.RowLabel("Key", Ui.PAD, 122, 28, 70, 9f, Theme.Muted));
            btnKey = WatchUi.Secondary("Choose a key", Ui.PAD + 76, 122, 130, 28);
            btnKey.Click += delegate { ChooseKey(); };
            card.Controls.Add(btnKey);
            lnkNoKey = Ui.RowLink("No key", Ui.PAD + 76 + 138, 122, 28, 8.25f);
            lnkNoKey.Click += delegate { ClearKey(); };
            card.Controls.Add(lnkNoKey);

            lblKeyLine = Ui.RowLabel(KEY_HINT, Ui.PAD, 154, 32, RUN_W - Ui.PAD * 2, 8.25f, Theme.Muted);
            card.Controls.Add(lblKeyLine);

            chkDiscord = Ui.DarkCheck("Send the summary to Discord", Ui.PAD, 192, 8.25f);
            chkDiscord.AutoSize = true;
            chkDiscord.CheckedChanged += delegate { DiscordChosen(); };
            card.Controls.Add(chkDiscord);
        }

        void BuildOutput()
        {
            Card card = WatchUi.CardAt(this, "OUTPUT", OUT_X, ROW2_Y, OUT_W, ROW2_H);
            LinkLabel clear = Ui.RowLink("Clear", OUT_W - 64, 8, 22, 8.25f);
            clear.Click += delegate { ClearOutput(); };
            card.Controls.Add(clear);

            output = new RichTextBox();
            output.Location = new Point(12, 38);
            output.Size = new Size(OUT_W - 24, ROW2_H - 38 - 12);
            output.BorderStyle = BorderStyle.None;
            output.BackColor = Theme.Inset;
            output.ForeColor = Theme.Text;
            output.Font = Ui.F("Consolas", 8.25f);
            output.ReadOnly = true;
            output.WordWrap = true;
            output.DetectUrls = false;
            output.HideSelection = false;
            output.ScrollBars = RichTextBoxScrollBars.Vertical;
            output.TabStop = false;
            output.HandleCreated += delegate { DarkScrollbars.Apply(output); };
            card.Controls.Add(output);
        }

        void DrawScriptRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            string name = (string)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(selected ? Theme.Button : Theme.Inset))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (running.Contains(name))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush dot = new SolidBrush(Theme.Green))
                    e.Graphics.FillEllipse(dot, e.Bounds.Left + 8, e.Bounds.Top + (e.Bounds.Height - 8) / 2, 8, 8);
            }
            TextRenderer.DrawText(e.Graphics, name, list.Font, new Rectangle(e.Bounds.Left + 24, e.Bounds.Top, e.Bounds.Width - 28, e.Bounds.Height),
                                  selected ? Theme.Text : Color.FromArgb(180, 188, 220),
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // ---------- the list ----------

        bool listing;

        void ReloadList()
        {
            listing = true;
            try
            {
                list.Items.Clear();
                foreach (string n in store.Names()) list.Items.Add(n);
            }
            finally { listing = false; }
        }

        void SelectInList(string name)
        {
            listing = true;
            try
            {
                list.SelectedIndex = -1;
                for (int i = 0; i < list.Items.Count; i++)
                    if (string.Equals((string)list.Items[i], name, StringComparison.OrdinalIgnoreCase)) { list.SelectedIndex = i; break; }
            }
            finally { listing = false; }
        }

        void ListChosen()
        {
            if (listing) return;
            string name = list.SelectedItem as string;
            if (name == null || string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) return;
            Open(name);
        }

        // Shows another script, after asking about changes to this one. False
        // when the person chose to stay.
        public bool Open(string name)
        {
            string real = store.Find(name);
            if (real == null) return false;
            if (!ConfirmLeave())
            {
                SelectInList(current);
                return false;
            }
            LoadCurrent(real);
            SelectInList(real);
            return true;
        }

        // True when it is fine to move away from this script: nothing is
        // unsaved, or it was saved, or the changes were let go.
        public bool ConfirmLeave()
        {
            if (current == null || !editor.Dirty) return true;
            DialogResult answer = Ask(current);
            if (answer == DialogResult.Yes) return Save();
            return answer == DialogResult.No;
        }

        DialogResult AskWithBox(string name)
        {
            return MessageBox.Show(this, "Save your changes to " + name + "?", "Scripts",
                                   MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        void LoadCurrent(string name)
        {
            current = name;
            loading = true;
            try
            {
                if (name == null)
                {
                    editor.Load("");
                    editor.IsReadOnly = true;
                    editor.RunLine = 0;
                }
                else
                {
                    editor.IsReadOnly = false;
                    editor.Load(store.Load(name) ?? "");
                    int line;
                    editor.RunLine = running.Contains(name) && runLines.TryGetValue(name, out line) ? line : 0;
                }
                ShowSettings();
                ShowTrust();
            }
            finally { loading = false; }
            ShowName();
            ShowOutput();
            RefreshRunning();
            string said;
            if (name == null) Say("Press New script to write one, or start from an example.", false);
            else if (statuses.TryGetValue(name, out said) && running.Contains(name)) Say(said ?? "", false);
            else Say("", false);
        }

        // The banner shows for a script that is not trusted yet; the editor
        // gives up the room it needs.
        void ShowTrust()
        {
            bool show = current != null && !host.IsScriptTrusted(current);
            banner.Visible = bannerOn = show;
            int top = 38 + (show ? BANNER_H + 6 : 0);
            editor.Location = new Point(12, top);
            editor.Size = new Size(MID_W - 24, ROW1_H - top - 12);
        }

        // The person has read it and says it is fine to run.
        public void Trust()
        {
            if (current == null) return;
            host.TrustScript(current);
            ShowTrust();
        }

        void ShowName()
        {
            bool has = current != null;
            lblName.Text = !has ? "" : current + (editor.Dirty ? "  -  not saved" : "");
            lblName.ForeColor = editor.Dirty ? Theme.Amber : Theme.Text;
            lnkSave.Visible = has;
            lnkRename.Visible = lnkRemove.Visible = lnkExport.Visible = has;
        }

        // ---------- what a script is set to do ----------

        void ShowSettings()
        {
            bool has = current != null;
            btnRun.Enabled = btnPicture.Enabled = btnKey.Enabled = cmbPlays.Enabled = chkDiscord.Enabled = has;
            ScriptSettings s = has ? store.Settings(current) : new ScriptSettings();

            cmbPlays.Items.Clear();
            playsValues.Clear();
            cmbPlays.Items.Add(FRONT_NAME);
            playsValues.Add(ScriptSettings.FRONT);
            IList<string> accounts = kit.Accounts == null ? null : kit.Accounts();
            bool listed = false;
            if (accounts != null)
                foreach (string a in accounts)
                {
                    if (string.IsNullOrEmpty(a)) continue;
                    cmbPlays.Items.Add(a);
                    playsValues.Add(a);
                    if (string.Equals(a, s.Account, StringComparison.OrdinalIgnoreCase)) listed = true;
                }
            // An account this PC no longer lists is kept, not quietly changed.
            if (s.Account != null && !listed) { cmbPlays.Items.Add(s.Account); playsValues.Add(s.Account); }
            cmbPlays.Items.Add(SCREEN_NAME);
            playsValues.Add(ScriptSettings.SCREEN);
            int at = s.OnScreen ? playsValues.Count - 1 : s.OnFront ? 0 : playsValues.FindIndex(delegate(string v) { return string.Equals(v, s.Account, StringComparison.OrdinalIgnoreCase); });
            cmbPlays.SelectedIndex = at < 0 ? 0 : at;

            chkDiscord.Checked = s.SummaryToDiscord;
            ShowKey(s.Key);
        }

        void ShowKey(byte vk)
        {
            btnKey.Text = vk == 0 ? "Choose a key" : MacroKeys.Name(vk);
            lnkNoKey.Visible = vk != 0;
            lblKeyLine.Text = KEY_HINT;
            lblKeyLine.ForeColor = Theme.Muted;
        }

        void PlaysOnChosen()
        {
            if (loading || current == null || cmbPlays.SelectedIndex < 0 || cmbPlays.SelectedIndex >= playsValues.Count) return;
            ScriptSettings s = store.Settings(current);
            s.PlaysOn = playsValues[cmbPlays.SelectedIndex];
            KeepSettings(s);
        }

        void DiscordChosen()
        {
            if (loading || current == null) return;
            ScriptSettings s = store.Settings(current);
            s.SummaryToDiscord = chkDiscord.Checked;
            KeepSettings(s);
        }

        void KeepSettings(ScriptSettings s)
        {
            if (!store.SaveSettings(current, s)) { Say("Couldn't save the settings. Is the scripts folder open somewhere else?", true); return; }
            host.ScriptsSaved();
        }

        void ChooseKey()
        {
            if (current == null) return;
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                SetKey(d.Captured);
            }
        }

        // Null when the key is now this script's; otherwise why not, which is
        // also said under the key.
        public string SetKey(byte vk)
        {
            if (current == null) return "Pick a script first.";
            string why = host.ScriptKeyProblem(vk, current);
            if (why != null)
            {
                lblKeyLine.Text = why;
                lblKeyLine.ForeColor = Theme.Amber;
                return why;
            }
            ScriptSettings s = store.Settings(current);
            s.Key = vk;
            KeepSettings(s);
            ShowKey(vk);
            return null;
        }

        public void ClearKey()
        {
            if (current == null) return;
            ScriptSettings s = store.Settings(current);
            s.Key = 0;
            KeepSettings(s);
            ShowKey(0);
        }

        // ---------- saving ----------

        // False when it couldn't be written.
        public bool Save()
        {
            if (current == null) return false;
            if (!store.Save(current, editor.Code))
            {
                Say("Couldn't save " + current + ". Is it open in another program?", true);
                return false;
            }
            editor.MarkSaved();
            Say("Saved.", false);
            return true;
        }

        // ---------- new, example, rename, remove ----------

        // The name is asked for first, and unsaved changes after it: nothing
        // is lost by answering the name and then deciding to stay.
        void AskNew()
        {
            using (ScriptNameDialog d = new ScriptNameDialog("New script", ScriptMaking.UniqueName(store, "New script"), NameProblem))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                NewScript(d.Chosen);
            }
        }

        string NameProblem(string name) { return store.NameProblem(name, null); }

        // A script with only a short note in it. Null when made; otherwise why not.
        public string NewScript(string name)
        {
            return Make(name, TEMPLATE);
        }

        public string AddExample(int index)
        {
            if (index < 0 || index >= ScriptExamples.Names.Length) return "There's no such example.";
            return Make(ScriptMaking.UniqueName(store, ScriptExamples.Names[index]), ScriptExamples.Code(index));
        }

        string Make(string name, string code)
        {
            if (!ConfirmLeave()) return "";
            string why = store.Create(name, code);
            if (why != null) { Say(why, true); return why; }
            ReloadList();
            LoadCurrent(store.Find(name));
            SelectInList(current);
            Say("Made " + current + ".", false);
            return null;
        }

        void AskRename()
        {
            if (current == null) return;
            string from = current;
            using (ScriptNameDialog d = new ScriptNameDialog("Rename script", from, delegate(string n) { return store.NameProblem(n, from); }))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                Rename(d.Chosen);
            }
        }

        public string Rename(string to)
        {
            if (current == null) return "Pick a script first.";
            // It is shown again from its file under the new name.
            if (!ConfirmLeave()) return "";
            string from = current;
            string why = host.RenameScript(from, to);
            if (why != null) { Say(why, true); return why; }
            MoveState(from, to);
            ReloadList();
            LoadCurrent(store.Find(to));
            SelectInList(current);
            Say("Renamed to " + current + ".", false);
            return null;
        }

        // What was said under the old name belongs to the new one.
        void MoveState(string from, string to)
        {
            List<OutLine> o;
            if (outputs.TryGetValue(from, out o)) { outputs.Remove(from); outputs[to] = o; }
            string s;
            if (statuses.TryGetValue(from, out s)) { statuses.Remove(from); statuses[to] = s; }
            int line;
            if (runLines.TryGetValue(from, out line)) { runLines.Remove(from); runLines[to] = line; }
        }

        void AskRemove()
        {
            if (current == null) return;
            if (MessageBox.Show(this, "Remove the script " + current + "? You can't get it back.", "Remove script",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Remove();
        }

        public string Remove()
        {
            if (current == null) return "Pick a script first.";
            string name = current;
            int at = list.SelectedIndex;
            string why = host.RemoveScript(name);
            if (why != null) { Say(why, true); return why; }
            outputs.Remove(name); statuses.Remove(name); runLines.Remove(name);
            ReloadList();
            string next = list.Items.Count == 0 ? null : (string)list.Items[Math.Min(Math.Max(0, at), list.Items.Count - 1)];
            LoadCurrent(next);
            if (next != null) SelectInList(next);
            Say("Removed " + name + ".", false);
            return null;
        }

        // ---------- import and export ----------

        void AskImport()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Import a script";
                d.Filter = "RobloxKeeper scripts (*" + ScriptPack.EXT + ")|*" + ScriptPack.EXT;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ImportFrom(d.FileName);
            }
        }

        public void ImportFrom(string path)
        {
            if (!ConfirmLeave()) return;
            ScriptImport r = host.ImportScript(path);
            if (r == null || r.Name == null)
            {
                Say(r == null || string.IsNullOrEmpty(r.Problem) ? "Couldn't read that file." : r.Problem, true);
                return;
            }
            ReloadList();
            LoadCurrent(store.Find(r.Name));
            SelectInList(current);
            // The status under Run is only two short lines, so it says what
            // matters most; the rest goes in the output box, which has room.
            string current2 = current;
            if (r.ScriptRenamed) AddOutput(current2, "A script with its name was already here, so it has a new name.", false, true);
            if (r.Renamed.Count > 0) AddOutput(current2, r.Renamed.Count + (r.Renamed.Count == 1 ? " picture was" : " pictures were") + " renamed because those names were taken.", false, true);
            // An import never brings the key, and we can't tell whether it had
            // one, so this is always said.
            AddOutput(current2, "Its key was left off; choose one if you want.", false, true);
            AddOutput(current2, "Read it, then trust it, before it runs.", false, true);
            Say("Imported " + r.Name + ". Read it, then trust it, before it runs.", true);
            ShowKey(store.Settings(current).Key);
        }

        void AskExport()
        {
            if (current == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Export a script";
                d.FileName = current + ScriptPack.EXT;
                d.Filter = "RobloxKeeper scripts (*" + ScriptPack.EXT + ")|*" + ScriptPack.EXT;
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ExportTo(d.FileName);
            }
        }

        public bool ExportTo(string path)
        {
            if (current == null) return false;
            // What is sent is what is on disk.
            if (editor.Dirty && !Save()) return false;
            string why = host.ExportScript(current, path);
            if (why != null) { Say(why, true); return false; }
            Say("Saved " + Path.GetFileName(path) + ", with its pictures.", false);
            return true;
        }

        // ---------- running ----------

        // Null when it started or stopped; otherwise why not.
        public string RunOrStop()
        {
            if (current == null) return "Pick a script first.";
            if (host.IsScriptRunning(current))
            {
                host.StopScript(current);
                Say("Stopping...", false);
                RefreshRunning();
                return null;
            }
            // It runs what is on disk.
            if (editor.Dirty && !Save()) return "Couldn't save it.";
            string why = host.StartScript(current);
            if (why != null) { Say(why, true); return why; }
            runLines.Remove(current);
            statuses.Remove(current);
            editor.RunLine = 0;
            Say("Running.", false);
            RefreshRunning();
            return null;
        }

        // Once a second, and after anything that starts or stops one: which
        // scripts are running - a key or a rule can start one without this
        // window - and what the one shown is up to.
        void RefreshRunning()
        {
            List<ScriptRunInfo> info = host.ScriptsRunning();
            HashSet<string> now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptRunInfo i in info)
            {
                now.Add(i.Name);
                // What the window was not told as it happened.
                if (!runLines.ContainsKey(i.Name) && i.Line > 0) runLines[i.Name] = i.Line;
                if (!statuses.ContainsKey(i.Name) && i.Status != null) statuses[i.Name] = i.Status;
            }
            bool changed = !now.SetEquals(running);
            running.Clear();
            foreach (string n in now) running.Add(n);
            if (changed) list.Invalidate();

            bool on = current != null && running.Contains(current);
            string text = on ? "Stop" : "Run";
            if (btnRun.Text != text) btnRun.Text = text;
            if (current != null)
            {
                int line = 0;
                if (on) runLines.TryGetValue(current, out line);
                if (editor.RunLine != line) editor.RunLine = line;
                string said;
                if (changed && on && statuses.TryGetValue(current, out said)) Say(said ?? "", false);
            }
        }

        // ---------- what a script says ----------

        void OnUi(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) a(); }); } catch { }
            }
            else a();
        }

        void OnOutput(string name, string line)
        {
            OnUi(delegate { AddOutput(name, line, false, false); });
        }

        void OnLine(string name, int line)
        {
            OnUi(delegate
            {
                runLines[name] = line;
                if (current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) editor.RunLine = line;
            });
        }

        void OnStatus(string name, string text)
        {
            OnUi(delegate
            {
                if (text == null) statuses.Remove(name); else statuses[name] = text;
                if (current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) Say(text ?? "", false);
            });
        }

        void OnEnded(string name, ScriptResult r)
        {
            OnUi(delegate
            {
                runLines.Remove(name);
                statuses.Remove(name);
                // The mistake on a line of its own, then how long it ran and what
                // it counted - without the mistake said a second time.
                bool problem = r != null && !string.IsNullOrEmpty(r.Problem);
                string summary = r == null ? "" : ScriptSummary.Line(name, r.Ms, r.Counters, null);
                if (problem) AddOutput(name, r.Problem, true, false);
                if (summary.Length > 0) AddOutput(name, summary, false, true);
                if (current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                {
                    editor.RunLine = 0;
                    Say(problem ? r.Problem : summary, problem);
                }
                RefreshRunning();
            });
        }

        // A mistake in a script is said as "Line 12: ..."
        static readonly Regex Mistake = new Regex(@"^Line \d+:", RegexOptions.Compiled);

        public static bool IsProblemLine(string text)
        {
            return text != null && Mistake.IsMatch(text);
        }

        public void AddOutput(string name, string text, bool problem, bool note)
        {
            List<OutLine> all;
            if (!outputs.TryGetValue(name, out all)) { all = new List<OutLine>(); outputs[name] = all; }
            OutLine l = new OutLine();
            l.Text = text ?? "";
            l.Problem = problem || IsProblemLine(l.Text);
            l.Note = note;
            all.Add(l);
            bool shown = current != null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            if (all.Count > MAX_OUTPUT)
            {
                // The oldest go, a hundred at a time, so a script printing in a
                // loop doesn't rebuild the box for every line.
                all.RemoveRange(0, all.Count - (MAX_OUTPUT - 100));
                if (shown) ShowOutput();
                return;
            }
            if (shown) AppendLine(l);
        }

        void AppendLine(OutLine l)
        {
            output.Select(output.TextLength, 0);
            output.SelectionColor = l.Problem ? ProblemColour : l.Note ? Theme.Muted : Theme.Text;
            output.SelectedText = l.Text + "\n";
            output.Select(output.TextLength, 0);
            output.ScrollToCaret();
        }

        void ShowOutput()
        {
            output.Clear();
            List<OutLine> all;
            if (current == null || !outputs.TryGetValue(current, out all)) return;
            foreach (OutLine l in all) AppendLine(l);
        }

        public void ClearOutput()
        {
            if (current != null) outputs.Remove(current);
            output.Clear();
        }

        void Say(string text, bool warn)
        {
            statusText = text ?? "";
            statusWarn = warn;
            lblStatus.Text = statusText;
            lblStatus.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        // ---------- pictures ----------

        void ChoosePicture(Control under)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            WatchedClient[] clients = kit.RunningClients();
            foreach (WatchedClient c in clients)
            {
                WatchedClient client = c;
                menu.Items.Add("Take a new picture from " + WatchUi.ClientName(c), null, delegate { Cut(client); });
            }
            if (clients.Length == 0)
                menu.Items.Add("Open a Roblox window to take a picture from").Enabled = false;

            List<string> have = PicturesKnown();
            if (have.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem sub = new ToolStripMenuItem("Use a picture you already have");
                sub.DropDown = DarkMenu.Make();
                for (int i = 0; i < have.Count && i < 15; i++)
                {
                    string n = have[i];
                    sub.DropDownItems.Add(n, null, delegate { InsertPicture(n); });
                }
                menu.Items.Add(sub);
            }
            menu.Show(under, new Point(0, under.Height));
        }

        List<string> PicturesKnown()
        {
            try { return PictureShelf.For(kit.Store, null, WatchStore.TemplatesDir).Names(); }
            catch { return new List<string>(); }
        }

        // The picture's name, in quotes, where the cursor is: that is what
        // find() and clickpicture() are given.
        public void InsertPicture(string name)
        {
            editor.InsertAtCursor("\"" + name + "\"");
        }

        void Cut(WatchedClient c)
        {
            Pixels still = WindowCapture.Grab(WindowCapture.GameWindow(c.Pid));
            if (still == null)
            {
                Say("Couldn't see that Roblox window. Is it minimized?", true);
                return;
            }
            using (RegionPickerForm f = new RegionPickerForm(still, WatchUi.ClientName(c),
                       RegionPickMode.Picture, c.PlaceId, RegionScaling.Uniform, null))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.PickedPicture == null) return;
                string called;
                using (PictureNameDialog n = new PictureNameDialog())
                {
                    if (n.ShowDialog(this) != DialogResult.OK) return;
                    called = n.Chosen;
                }
                string file = WatcherForm.TemplateName(called, DateTime.Now);
                try { WatcherForm.SavePicture(f.PickedPicture, file); }
                catch (Exception ex) { Say("Couldn't save the picture: " + ex.Message, true); return; }
                // The name scripts find it by: what it is called, unless another
                // picture is called that too.
                string name;
                try { name = PictureShelf.For(kit.Store, null, WatchStore.TemplatesDir).NameOf(file); }
                catch { name = MacroStep.PictureLabel(file); }
                InsertPicture(name);
                Say("Picture saved as \"" + name + "\".", false);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
