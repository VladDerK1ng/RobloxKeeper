using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // What a page may ask of the main window. Pages know nothing else about
    // it, so each can be built and tested on its own.
    interface IShell
    {
        IList<RobloxAccount> Accounts();        // decrypted the first time anything asks
        WatchedClient[] Clients();
        long MemoryOf(int pid);
        void ShowClient(int pid);
        void Go(string page);                   // by a rail entry's key
        void Say(string line);                  // into the activity list
        void Play(GameCard game, Control under);
        void Join(FriendCard friend, Control under);
        void OpenAccounts();
        void OpenWatchers();
        void OpenMacros();
        void OpenClicker();
        void OpenScripts();
        void Search(string words);              // on the Games page
        IList<string> RecentPlaces();           // newest first
        void TakeScreenshots();
        byte ShotKey { get; }
        string SetShotKey(byte vk);             // null when it was taken; otherwise why not
        IMacroHost Macros { get; }
        IScriptHost Scripts { get; }            // Lua scripts: start, stop, their keys and files
        WatchKit Kit { get; }                   // null until watching has started
        string PlayAs { get; set; }             // the account the launcher pages act as
        bool TipsHidden { get; set; }           // Home's "Start here" has been put away
        LooksChoices LooksChosen { get; set; }  // the graphics settings asked for on the Looks page
        string Shader { get; }                  // the shader look that is on, or ShaderLooks.OFF
        int ShaderStrength { get; }
        void SetShader(string look, int strength);
        string ShaderState { get; }             // what the overlays are doing, in a line
    }

    // One page of the window. A page does nothing until it is first shown -
    // no sign-in is read, nothing is asked of Roblox, no timer runs - so a
    // window that starts in the tray, or is never looked at, pays nothing for
    // pages nobody opened.
    class ShellPage : Panel
    {
        public const int HEAD_H = 64;
        public const int FOOT_H = 26;

        protected readonly IShell shell;
        readonly Label said;
        bool entered;

        public ShellPage(IShell shell, Size size)
        {
            this.shell = shell;
            Size = size;
            BackColor = Theme.Bg;

            // The last thing that happened, at the foot of the page. The
            // activity list is on another page now, and "started", "couldn't
            // join" or "saved a picture" has to be said where it was asked for.
            said = Ui.RowLabel("", CardLayout.PAD, size.Height - FOOT_H, FOOT_H - 4, size.Width - CardLayout.PAD * 2, 8.25f, Theme.Muted);
            said.BackColor = Theme.Bg;
            said.AutoEllipsis = true;
            Controls.Add(said);
        }

        Label headTitle, headSays;

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (headTitle != null && !ReferenceEquals(e.Control, headTitle)) headTitle.SendToBack();
            if (headSays != null && !ReferenceEquals(e.Control, headSays)) headSays.SendToBack();
        }

        // The room between the page's head and its foot.
        protected Rectangle Body { get { return new Rectangle(0, HEAD_H, Width, Height - HEAD_H - FOOT_H); } }

        public void Heard(string line)
        {
            said.Text = DateTime.Now.ToString("HH:mm") + "  " + line;
        }

        public string LastHeard { get { return said.Text; } }

        // Where the line at the foot of the page is, below everything else.
        public int FootTop { get { return said.Top - AutoScrollPosition.Y; } }

        // On a screen too short for the whole window - a 768-high laptop - a
        // page is shorter than it was laid out for, and its last card ran
        // into the line at its foot or off the bottom. Then the page scrolls,
        // and the line goes below its last card. Called once it is built.
        public void FitContent()
        {
            int bottom = 0;
            foreach (Control c in Controls)
                if (!ReferenceEquals(c, said)) bottom = Math.Max(bottom, c.Bottom);
            if (bottom <= Height - FOOT_H) return;
            said.Top = bottom + 4;
            AutoScroll = true;
            AutoScrollMinSize = new Size(0, said.Bottom + 4);
        }

        public bool IsShowing { get { return entered; } }

        // Coming into view, and leaving it. Timers start and stop here.
        public void ComeIn() { entered = true; OnEnterPage(); }
        public void GoOut() { if (!entered) return; entered = false; OnLeavePage(); }

        protected virtual void OnEnterPage() { }
        protected virtual void OnLeavePage() { }

        // The page's name, and a line under it saying what it is for.
        protected Label Head(string title, string says)
        {
            Label t = Ui.RowLabel(title, CardLayout.PAD, 10, 30, 400, 15f, Theme.Text);
            t.Font = Ui.F("Segoe UI Semibold", 15f);
            t.BackColor = Theme.Bg;
            Controls.Add(t);
            Label s = Ui.RowLabel(says, CardLayout.PAD, 38, 20, Width - CardLayout.PAD * 2, 8.25f, Theme.Muted);
            s.BackColor = Theme.Bg;
            Controls.Add(s);
            // Both run the width of the page, under the buttons and boxes a page
            // puts at its top right. Drawn on top, they hid the bottom of those
            // buttons, which then looked short with their text sitting low.
            // The first control added is drawn on top, and those buttons are
            // added after this - so these are sent back again as each one comes.
            headTitle = t;
            headSays = s;
            t.SendToBack();
            s.SendToBack();
            return s;
        }

        // Work that waits on Roblox, off the window's thread, with the
        // answer handed back on it - and dropped if the page has gone.
        protected void InBackground<T>(Func<T> work, Action<T> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                T result;
                try { result = work(); }
                catch { result = default(T); }
                try
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) done(result); });
                }
                catch { }       // closing
            });
        }
    }

    // One entry in the rail.
    class RailItem
    {
        public string Key, Name;
        public char Glyph;              // from the icon font that ships with Windows
        public bool AtBottom;

        public RailItem(string key, string name, char glyph, bool atBottom)
        {
            Key = key; Name = name; Glyph = glyph; AtBottom = atBottom;
        }
    }

    // The strip of icons down the window's left edge: one for each page.
    class Rail : Control
    {
        public const int WIDTH = 60;
        const int ITEM = 44, STEP = 50, TOP = 54;

        readonly List<RailItem> items = new List<RailItem>();
        readonly ToolTip tips = new ToolTip();
        readonly Font icons;
        int hot = -1;
        string selected;
        string tipFor;

        public event Action<string> Chosen;

        public Rail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Inset;
            // Windows 11 has the newer icon font; Windows 10 only the older.
            // Both draw the same glyphs at the same codes.
            icons = Ui.F(HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 14f);
            tips.InitialDelay = 300;
            Extras.Boot(this);      // the crosshair and other on-screen extras come back with the app; does nothing in tests
        }

        static bool HasFont(string name)
        {
            try { using (Font f = Ui.F(name, 10f)) return f.Name == name; }
            catch { return false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { icons.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }

        public void Add(RailItem item) { items.Add(item); Invalidate(); }
        public IList<RailItem> Items { get { return items; } }

        public string Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        // Where an entry sits: from the top down, or for the ones kept at
        // the bottom, from the bottom up.
        public Rectangle BoundsOf(int index)
        {
            int above = 0, below = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].AtBottom) continue;
                if (i == index) return new Rectangle((WIDTH - ITEM) / 2, TOP + above * STEP, ITEM, ITEM);
                above++;
            }
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!items[i].AtBottom) continue;
                if (i == index) return new Rectangle((WIDTH - ITEM) / 2, Height - 12 - ITEM - below * STEP, ITEM, ITEM);
                below++;
            }
            return Rectangle.Empty;
        }

        int At(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (BoundsOf(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = At(e.Location);
            if (now == hot) return;
            hot = now;
            Cursor = now < 0 ? Cursors.Default : Cursors.Hand;
            string tip = now < 0 ? null : items[now].Name;
            if (tip != tipFor) { tipFor = tip; tips.SetToolTip(this, tip ?? ""); }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hot = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int at = At(e.Location);
            if (at >= 0 && e.Button == MouseButtons.Left) Choose(items[at].Key);
        }

        public void Choose(string key)
        {
            if (Chosen != null) Chosen(key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Inset);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = BoundsOf(i);
                bool on = items[i].Key == selected;
                if (on || i == hot)
                    using (SolidBrush b = new SolidBrush(on ? Theme.Button : Theme.Card))
                    using (GraphicsPath p = RoundRect(r, 10))
                        g.FillPath(b, p);
                if (on)
                    using (SolidBrush mark = new SolidBrush(Theme.Accent))
                    using (GraphicsPath p = RoundRect(new Rectangle(r.X - 6, r.Y + 12, 4, r.Height - 24), 2))
                        g.FillPath(mark, p);
                using (SolidBrush ink = new SolidBrush(on ? Theme.Text : i == hot ? Theme.Text : Theme.Muted))
                using (StringFormat centre = new StringFormat())
                {
                    centre.Alignment = StringAlignment.Center;
                    centre.LineAlignment = StringAlignment.Center;
                    g.DrawString(items[i].Glyph.ToString(), icons, ink, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), centre);
                }
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // The games played lately, newest first, by the place that starts them.
    //
    // Kept by this app from what Roblox's own log says each client joined -
    // nothing is asked of Roblox, and it works for a client signed in to an
    // account this app has never been told about.
    class Recents
    {
        public const int KEEP = 24;

        readonly string path;
        readonly List<string> places = new List<string>();

        public Recents(string path) { this.path = path; }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "RobloxKeeper", "recent.txt");
            }
        }

        public IList<string> Places { get { return places; } }

        // True when it changed what is first.
        public bool Add(string placeId)
        {
            long n;
            if (string.IsNullOrEmpty(placeId) || !long.TryParse(placeId, out n) || n <= 0) return false;
            if (places.Count > 0 && places[0] == placeId) return false;
            places.Remove(placeId);
            places.Insert(0, placeId);
            while (places.Count > KEEP) places.RemoveAt(places.Count - 1);
            return true;
        }

        public void Load()
        {
            places.Clear();
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                // Written newest first; added back oldest first so the order holds.
                for (int i = lines.Length - 1; i >= 0; i--) Add(lines[i].Trim());
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, places.ToArray());
            }
            catch { }
        }

        // The first time, before this app has seen anything join: what the
        // newest of Roblox's own logs say was played. Oldest first, so the
        // newest ends up on top.
        public static List<string> FromLogs(string logsDir, int newestFiles)
        {
            List<string> found = new List<string>();
            try
            {
                if (!Directory.Exists(logsDir)) return found;
                FileInfo[] logs = new DirectoryInfo(logsDir).GetFiles("*_Player_*_last.log");
                Array.Sort(logs, delegate(FileInfo a, FileInfo b) { return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc); });
                for (int i = Math.Max(0, logs.Length - newestFiles); i < logs.Length; i++)
                {
                    try
                    {
                        // Roblox may still have it open; that is no reason not to read it.
                        using (FileStream fs = new FileStream(logs[i].FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader r = new StreamReader(fs))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                RobloxLogEvent e = RobloxLog.Parse(line);
                                if (e.Type == RobloxLogEvent.Kind.Joined && !string.IsNullOrEmpty(e.PlaceId)) found.Add(e.PlaceId);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return found;
        }
    }
}
