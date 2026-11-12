using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace RobloxKeeper
{
    // The sums behind the extras, kept apart from their windows so each can
    // be checked on its own.
    static class GlassMath
    {
        public const int MARGIN = 16, TOP_CLEAR = 64;      // top corners sit under Roblox's own buttons

        // Where a panel this size goes in a corner of the game's picture, or
        // at the middle of its top or bottom edge. Places: 0 top-left, 1
        // top-right, 2 bottom-left, 3 bottom-right, 4 top-middle, 5
        // bottom-middle. The first four are what older settings files hold
        // and must keep their meaning, which is why the middle ones come last.
        public static Point Corner(Rectangle game, Size panel, int corner)
        {
            bool middle = corner == 4 || corner == 5;
            bool right = corner == 1 || corner == 3, bottom = corner == 2 || corner == 3 || corner == 5;
            int x = middle ? game.Left + (game.Width - panel.Width) / 2
                  : right ? game.Right - MARGIN - panel.Width : game.Left + MARGIN;
            int y = bottom ? game.Bottom - MARGIN - panel.Height : game.Top + TOP_CLEAR;
            return new Point(x, y);
        }

        // How far above the bottom edge the "holding a key" note floats: the
        // game's own hotbar and chat sit down there.
        public const int HOLD_NOTE_LIFT = 110;

        // Where the note about a held key goes: the middle of the game's
        // width, a little above the bottom, and never outside the game.
        public static Point HoldNoteAt(Rectangle game, Size note)
        {
            int x = game.Left + (game.Width - note.Width) / 2;
            int y = game.Bottom - HOLD_NOTE_LIFT - note.Height;
            if (y < game.Top) y = game.Top;
            return new Point(x, y);
        }

        // What the note says while a key is held for the user: which key is
        // down, and which one lets go of it. `holdWith` is 0 when the held
        // key itself lets go.
        public static string HoldNoteText(byte holdKey, byte holdWith)
        {
            byte letGo = holdWith != 0 ? holdWith : holdKey;
            return "Holding " + MacroKeys.Name(holdKey) + " - tap " + MacroKeys.Name(letGo) + " to let go";
        }

        // The magnifier is told where the top-left of what it shows is. For
        // the game's middle to stay where it is while everything grows around
        // it, that corner is this far in.
        public static Point ZoomOffset(Point middle, int level)
        {
            if (level <= 1) return Point.Empty;
            return new Point(middle.X - middle.X / level, middle.Y - middle.Y / level);
        }

        // How many of these presses, given as milliseconds on one clock, fell
        // in the second before now. Older ones are dropped from the list.
        public static int PerSecond(List<long> presses, long now)
        {
            while (presses.Count > 0 && now - presses[0] >= 1000) presses.RemoveAt(0);
            return presses.Count;
        }

        // Left and right Shift, Ctrl and Alt are one key each as far as
        // choosing a key goes.
        public static byte Plain(uint vk)
        {
            if (vk == 0xA0 || vk == 0xA1) return 0x10;
            if (vk == 0xA2 || vk == 0xA3) return 0x11;
            if (vk == 0xA4 || vk == 0xA5) return 0x12;
            return vk > 254 ? (byte)0 : (byte)vk;
        }

        public static string Memory(long bytes)
        {
            if (bytes <= 0) return "?";
            double mb = bytes / (1024.0 * 1024.0);
            return mb >= 1000 ? (mb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
                              : mb.ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        // Processor use between two readings, as a share of the whole PC -
        // what Task Manager's column shows. Times are in 100ns ticks.
        public static int CpuPercent(long busyBefore, long busyNow, long clockBefore, long clockNow, int processors)
        {
            long clock = clockNow - clockBefore, busy = busyNow - busyBefore;
            if (clock <= 0 || busy < 0 || processors <= 0) return 0;
            double share = 100.0 * busy / clock / processors;
            return share < 0 ? 0 : share > 100 ? 100 : (int)Math.Round(share, MidpointRounding.AwayFromZero);
        }

        static readonly Regex JoinAt = new Regex(@"! Joining game '[^']*' place \d+ at (\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})", RegexOptions.IgnoreCase);
        static readonly Regex Udmux = new Regex(@"UDMUX Address = (\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})", RegexOptions.IgnoreCase);

        // An address only reachable inside somebody's own network.
        public static bool IsPrivate(string address)
        {
            System.Net.IPAddress ip;
            if (!System.Net.IPAddress.TryParse(address, out ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return true;
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        // The address of the server a client last joined, from the lines of
        // its own log. Null when it has not joined one, or the log names no
        // address that can be reached from here.
        //
        // Read from a live log: for a server in Roblox's own data centres
        // the "Joining game ... at" line holds an address inside Roblox's
        // network (10.x), and the one a PC actually talks to is on the
        // "UDMUX Address" line that follows it. A server elsewhere has only
        // the first line, with a real address in it.
        public static string ServerIn(IEnumerable<string> logLines)
        {
            string joined = null, front = null;
            foreach (string line in logLines)
            {
                if (line == null) continue;
                Match m = JoinAt.Match(line);
                if (m.Success)
                {
                    System.Net.IPAddress ip;
                    if (System.Net.IPAddress.TryParse(m.Groups[1].Value, out ip)) { joined = m.Groups[1].Value; front = null; }
                    continue;
                }
                m = Udmux.Match(line);
                if (m.Success && joined != null && !IsPrivate(m.Groups[1].Value)) front = m.Groups[1].Value;
            }
            if (front != null) return front;
            return joined != null && !IsPrivate(joined) ? joined : null;
        }

        public static string StatsLine(bool wantPing, long pingMs, bool pingKnown, bool wantMemory, long memoryBytes, int cpuPercent, bool cpuKnown)
        {
            return StatsLine(wantPing, pingMs, pingKnown, false, wantMemory, memoryBytes, cpuPercent, cpuKnown);
        }

        // The read-out, as one line. Only what was measured is in it.
        // `pingGaveUp`: the server has been asked and has not answered, so it
        // is no longer being asked.
        public static string StatsLine(bool wantPing, long pingMs, bool pingKnown, bool pingGaveUp, bool wantMemory, long memoryBytes, int cpuPercent, bool cpuKnown)
        {
            List<string> parts = new List<string>();
            if (wantPing)
                parts.Add(!pingKnown ? "Ping ..."
                        : pingMs >= 0 ? "Ping " + pingMs + " ms"
                        : pingGaveUp ? "Ping: this server doesn't answer"
                        : "Ping: no answer");
            if (wantMemory)
            {
                parts.Add("Memory " + Memory(memoryBytes));
                if (cpuKnown) parts.Add("CPU " + cpuPercent + "%");
            }
            return string.Join("    ", parts.ToArray());
        }
    }

    // Counts the pings a server has left unanswered, one server at a time.
    //
    // Roblox's own servers never answer an ordinary ping, so asking again
    // every few seconds gets nothing and costs a wait each time. After a few
    // misses in a row the server is left alone; a different server starts
    // the count again. Asked from two threads (the one that pings, and the
    // one that shows the answer), so each call takes the lock.
    class PingTally
    {
        public const int GIVE_UP_AFTER = 3;

        readonly object gate = new object();
        string server;
        int misses;

        // One ping's outcome. A new address is a new server: the count of the
        // old one is dropped. Returns whether the server is now given up on.
        public bool Record(string address, bool answered)
        {
            lock (gate)
            {
                if (address != server) { server = address; misses = 0; }
                if (answered) misses = 0; else misses++;
                return address != null && misses >= GIVE_UP_AFTER;
            }
        }

        // Whether this server is one that has stopped being asked.
        public bool GaveUp(string address)
        {
            lock (gate) return address != null && address == server && misses >= GIVE_UP_AFTER;
        }

        public void Reset()
        {
            lock (gate) { server = null; misses = 0; }
        }
    }

    // Shared drawing for the panels that show text.
    static class GlassDraw
    {
        // The rounded dark label the read-out and the held-key note are
        // drawn on: this text, white, with room round it. The picture is the
        // caller's to dispose.
        public static Bitmap Pill(string text)
        {
            using (Font f = Px(15f, true))
            {
                Size s;
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(probe))
                    s = Size.Ceiling(g.MeasureString(text, f));
                s = new Size(s.Width + 22, s.Height + 10);
                Bitmap b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Start(b))
                {
                    using (GraphicsPath p = Round(new RectangleF(0.5f, 0.5f, s.Width - 1, s.Height - 1), 7))
                    using (SolidBrush back = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                        g.FillPath(back, p);
                    Centre(g, text, f, Color.White, new RectangleF(0, 0, s.Width, s.Height));
                }
                return b;
            }
        }

        public static Font Px(float pixels, bool bold)
        {
            return new Font("Segoe UI", pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Graphics Start(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Not ClearType: it colours the edges for one background, and
            // this is drawn over whatever the game shows.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return g;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Centre(Graphics g, string text, Font f, Color colour, RectangleF r)
        {
            using (SolidBrush ink = new SolidBrush(colour))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, f, ink, r, sf);
            }
        }
    }

    // W A S D, Space, Shift and the mouse buttons, lit while they are down,
    // with clicks a second under each button.
    //
    // No hook: while it is showing, Windows is asked about sixty times a
    // second whether each of those eight keys is down. Nothing else is
    // looked at, nothing is kept, and it is redrawn only when one changes.
    class KeysPiece : IGlassPiece, IDisposable
    {
        static readonly int[] Vks = { 0x57, 0x41, 0x53, 0x44, 0x20, 0x10, 0x01, 0x02 };   // W A S D Space Shift LMB RMB
        static readonly string[] Names = { "W", "A", "S", "D", "Space", "Shift", "LMB", "RMB" };
        static readonly int[] UnitPx = { 30, 40, 52 };

        GlassWindow glass;
        System.Windows.Forms.Timer poll;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly bool[] down = new bool[8];
        readonly List<long> left = new List<long>(), right = new List<long>();
        int cpsL, cpsR, corner = 2, size = 1;
        bool on, drawn;
        Rectangle gameAt;

        // Made once for a size and kept: the panel is redrawn every time a
        // key or a click changes, and three fonts a redraw was most of what
        // this piece cost while a game was in front.
        Font fontBig, fontSmall, fontTiny;
        int fontsFor = -1;

        public bool Wanted { get { return on; } }

        public void Set(bool switchedOn, int corner, int size)
        {
            on = switchedOn;
            this.corner = corner;
            this.size = Math.Max(0, Math.Min(UnitPx.Length - 1, size));
            drawn = false;
            if (!on) Away();
        }

        void DropFonts()
        {
            if (fontBig != null) fontBig.Dispose();
            if (fontSmall != null) fontSmall.Dispose();
            if (fontTiny != null) fontTiny.Dispose();
            fontBig = fontSmall = fontTiny = null;
            fontsFor = -1;
        }

        void MakeFonts()
        {
            if (fontsFor == size && fontBig != null) return;
            DropFonts();
            float u = UnitPx[size];
            fontBig = GlassDraw.Px(u * 0.42f, true);
            fontSmall = GlassDraw.Px(u * 0.30f, true);
            fontTiny = GlassDraw.Px(u * 0.30f * 0.78f, false);
            fontsFor = size;
        }

        public static Size PanelSize(int size)
        {
            int u = UnitPx[Math.Max(0, Math.Min(UnitPx.Length - 1, size))], gap = Math.Max(3, u / 10);
            return new Size(u * 3 + gap * 2, u * 4 + gap * 3);
        }

        public void Place(IntPtr game, Rectangle onScreen)
        {
            gameAt = onScreen;
            if (glass == null) glass = new GlassWindow();
            if (poll == null)
            {
                poll = new System.Windows.Forms.Timer();
                poll.Interval = 16;
                poll.Tick += delegate { Read(); };
            }
            if (!drawn) Draw();
            glass.ShowAt(GlassMath.Corner(onScreen, PanelSize(size), corner));
            glass.Raise();
            poll.Start();
        }

        public void Away()
        {
            if (poll != null) poll.Stop();
            if (glass != null) glass.Hide();
            left.Clear(); right.Clear();
            cpsL = cpsR = 0;
        }

        public void Again() { if (glass != null) glass.Raise(); }

        void Read()
        {
            bool changed = false;
            long now = clock.ElapsedMilliseconds;
            for (int i = 0; i < Vks.Length; i++)
            {
                bool d = (Xn.GetAsyncKeyState(Vks[i]) & 0x8000) != 0;
                if (d == down[i]) continue;
                down[i] = d;
                changed = true;
                if (d && i == 6) left.Add(now);
                if (d && i == 7) right.Add(now);
            }
            int l = GlassMath.PerSecond(left, now), r = GlassMath.PerSecond(right, now);
            if (l != cpsL || r != cpsR) { cpsL = l; cpsR = r; changed = true; }
            if (changed) Draw();
        }

        void Draw()
        {
            Size s = PanelSize(size);
            int u = UnitPx[size], gap = Math.Max(3, u / 10);
            MakeFonts();
            Font big = fontBig, small = fontSmall;
            using (Bitmap b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = GlassDraw.Start(b))
                {
                    int row2 = u + gap, row3 = row2 * 2, row4 = row2 * 3, half = (s.Width - gap) / 2;
                    Key(g, big, 0, new RectangleF(u + gap, 0, u, u), null);
                    Key(g, big, 1, new RectangleF(0, row2, u, u), null);
                    Key(g, big, 2, new RectangleF(u + gap, row2, u, u), null);
                    Key(g, big, 3, new RectangleF((u + gap) * 2, row2, u, u), null);
                    Key(g, small, 5, new RectangleF(0, row3, half, u), null);
                    Key(g, small, 4, new RectangleF(half + gap, row3, s.Width - half - gap, u), null);
                    Key(g, small, 6, new RectangleF(0, row4, half, u), cpsL > 0 ? cpsL + " a sec" : null);
                    Key(g, small, 7, new RectangleF(half + gap, row4, s.Width - half - gap, u), cpsR > 0 ? cpsR + " a sec" : null);
                }
                drawn = glass.SetPicture(b, 100);
            }
        }

        void Key(Graphics g, Font f, int i, RectangleF r, string under)
        {
            bool lit = down[i];
            using (GraphicsPath p = GlassDraw.Round(r, Math.Max(3f, r.Height * 0.16f)))
            using (SolidBrush fill = new SolidBrush(lit ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0)))
            using (Pen edge = new Pen(Color.FromArgb(lit ? 255 : 90, 255, 255, 255), 1f))
            {
                g.FillPath(fill, p);
                g.DrawPath(edge, p);
            }
            Color ink = lit ? Color.Black : Color.White;
            if (under == null) GlassDraw.Centre(g, Names[i], f, ink, r);
            else
            {
                GlassDraw.Centre(g, Names[i], f, ink, new RectangleF(r.X, r.Y + 1, r.Width, r.Height * 0.55f));
                GlassDraw.Centre(g, under, fontTiny, ink, new RectangleF(r.X, r.Y + r.Height * 0.48f, r.Width, r.Height * 0.5f));
            }
        }

        public void Dispose()
        {
            if (poll != null) poll.Dispose();
            if (glass != null) glass.Dispose();
            DropFonts();
            poll = null; glass = null;
        }
    }

    // Everything but a circle round the middle, darkened.
    //
    // One picture the size of the game, drawn when the game's size changes
    // and not otherwise.
    class DimPiece : IGlassPiece, IDisposable
    {
        GlassWindow glass;
        bool on;
        int dark = 50, hole = 30;
        Size drawnFor;

        public bool Wanted { get { return on; } }

        public void Set(bool switchedOn, int darkPercent, int holePercent)
        {
            on = switchedOn;
            dark = darkPercent;
            hole = holePercent;
            drawnFor = Size.Empty;
            if (!on) Away();
        }

        // The picture: black at this strength, clear in a circle whose radius
        // is this share of the shorter side, fading in over its outer part.
        public static Bitmap Draw(Size game, int darkPercent, int holePercent)
        {
            Bitmap b = new Bitmap(Math.Max(1, game.Width), Math.Max(1, game.Height), PixelFormat.Format32bppArgb);
            Color shade = Color.FromArgb(Math.Max(0, Math.Min(255, darkPercent * 255 / 100)), 0, 0, 0);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(shade);
                float r = Math.Min(game.Width, game.Height) * holePercent / 100f;
                if (r >= 2)
                {
                    float cx = game.Width / 2f, cy = game.Height / 2f;
                    g.CompositingMode = CompositingMode.SourceCopy;     // put see-through pixels in, rather than paint over
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        using (PathGradientBrush fade = new PathGradientBrush(p))
                        {
                            fade.CenterPoint = new PointF(cx, cy);
                            fade.CenterColor = Color.FromArgb(0, 0, 0, 0);
                            fade.SurroundColors = new Color[] { shade };
                            fade.FocusScales = new PointF(0.72f, 0.72f);    // clear this far out, then fading
                            g.FillPath(fade, p);
                        }
                    }
                }
            }
            return b;
        }

        public void Place(IntPtr game, Rectangle onScreen)
        {
            if (glass == null) glass = new GlassWindow();
            if (drawnFor != onScreen.Size)
            {
                using (Bitmap b = Draw(onScreen.Size, dark, hole))
                    if (glass.SetPicture(b, 100)) drawnFor = onScreen.Size;
            }
            glass.ShowAt(onScreen.Location);
        }

        public void Away()
        {
            if (glass != null) glass.Hide();
        }

        public void Again() { if (glass != null) glass.Raise(); }

        public void Dispose()
        {
            if (glass != null) glass.Dispose();
            glass = null;
        }
    }

    // Ping to the game's server, and the client's memory and processor use.
    //
    // Ping is this PC's own: the server's address is read from the client's
    // log file, and an ordinary ping is sent to it. It is not the number
    // Roblox shows, and a server that does not answer pings shows as such.
    // Memory and processor are what Windows says of the process, as Task
    // Manager shows them. Frames per second are not here: they can only be
    // counted inside the game, and Roblox shows its own with Shift+F5.
    class StatsPiece : IGlassPiece, IDisposable
    {
        const int EVERY_MS = 2000, PING_EVERY = 3;      // a ping every third look

        GlassWindow glass;
        System.Windows.Forms.Timer tick;
        SynchronizationContext home;
        bool on, wantPing, wantMemory;
        int corner = 1, looks;
        Rectangle gameAt;
        int pid;
        string shownText;
        Size shownSize;

        // readings
        long pingMs = -1, memory;
        bool pingKnown, cpuKnown, pinging, pingGaveUp;
        readonly PingTally tally = new PingTally();
        int cpu;
        long busyBefore, clockBefore;
        string logPath, server;
        int logFor;

        // For tests and for a PC where the logs are elsewhere.
        public string LogsDir = RobloxLog.LogsDir;

        public bool Wanted { get { return on; } }

        public void Set(bool ping, bool memoryAndCpu, int corner)
        {
            wantPing = ping;
            wantMemory = memoryAndCpu;
            on = ping || memoryAndCpu;
            this.corner = corner;
            shownText = null;
            if (!on) Away();
        }

        public void Place(IntPtr game, Rectangle onScreen)
        {
            gameAt = onScreen;
            uint p;
            Xn.GetWindowThreadProcessId(game, out p);
            if ((int)p != pid)
            {
                pid = (int)p;
                pingKnown = cpuKnown = pingGaveUp = false;
                busyBefore = clockBefore = 0;
                server = null;
                logPath = null;
                tally.Reset();
            }
            if (glass == null) glass = new GlassWindow();
            if (home == null) home = SynchronizationContext.Current;
            if (tick == null)
            {
                tick = new System.Windows.Forms.Timer();
                tick.Interval = EVERY_MS;
                tick.Tick += delegate { Measure(); };
            }
            if (!tick.Enabled) { looks = 0; tick.Start(); Measure(); }
            else Show();
        }

        public void Away()
        {
            if (tick != null) tick.Stop();
            if (glass != null) glass.Hide();
        }

        public void Again() { if (glass != null) glass.Raise(); }

        void Measure()
        {
            if (wantMemory) ReadProcess();
            if (wantPing && !pinging && looks++ % PING_EVERY == 0) StartPing();
            Show();
        }

        void ReadProcess()
        {
            IntPtr h = Xn.OpenProcess(Xn.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) { memory = 0; cpuKnown = false; return; }
            try
            {
                Xn.PROCESS_MEMORY_COUNTERS c;
                c.cb = (uint)Marshal.SizeOf(typeof(Xn.PROCESS_MEMORY_COUNTERS));
                if (Xn.K32GetProcessMemoryInfo(h, out c, c.cb)) memory = (long)c.WorkingSetSize.ToUInt64();
                long made, gone, kernel, user;
                if (Xn.GetProcessTimes(h, out made, out gone, out kernel, out user))
                {
                    long busy = kernel + user, clock = DateTime.UtcNow.Ticks;
                    if (clockBefore != 0)
                    {
                        cpu = GlassMath.CpuPercent(busyBefore, busy, clockBefore, clock, Environment.ProcessorCount);
                        cpuKnown = true;
                    }
                    busyBefore = busy;
                    clockBefore = clock;
                }
            }
            catch { }
            finally { Xn.CloseHandle(h); }
        }

        // Off this thread: finding the client's log asks Windows about open
        // files, and a ping waits on the network.
        void StartPing()
        {
            pinging = true;
            int forPid = pid;
            string knownLog = logFor == forPid ? logPath : null;
            SynchronizationContext back = home;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string log = knownLog, address = null;
                long ms = -1;
                bool asked = false, answered = false;
                try
                {
                    if (log == null) log = LogOf(forPid, LogsDir);
                    if (log != null) address = GlassMath.ServerIn(ReadShared(log));
                    // A server that has not answered three times is not asked
                    // again - Roblox's own never answer - until it is a different one.
                    if (address != null && !tally.GaveUp(address))
                        using (Ping ping = new Ping())
                        {
                            PingReply reply = ping.Send(address, 1500);
                            // A reply that says "timed out" is a miss; a ping that could
                            // not be sent at all (no network) is neither a miss nor an answer.
                            asked = reply != null;
                            if (reply != null && reply.Status == IPStatus.Success) { ms = reply.RoundtripTime; answered = true; }
                        }
                }
                catch { }
                Action done = delegate
                {
                    pinging = false;
                    if (forPid != pid) return;
                    logPath = log; logFor = forPid;
                    server = address;
                    pingMs = ms;
                    pingKnown = true;
                    if (asked) tally.Record(address, answered);
                    pingGaveUp = tally.GaveUp(address);
                    Show();
                };
                if (back != null) back.Post(delegate { done(); }, null); else done();
            });
        }

        // The log this client is writing: the newest few are asked who has
        // them open. With one client running, the newest is taken when
        // Windows can't say.
        static string LogOf(int pid, string logsDir)
        {
            if (!Directory.Exists(logsDir)) return null;
            FileInfo[] logs = new DirectoryInfo(logsDir).GetFiles("*_Player_*_last.log");
            Array.Sort(logs, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            int looked = 0;
            string newest = null;
            foreach (FileInfo f in logs)
            {
                if (f.Name.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (newest == null) newest = f.FullName;
                if (++looked > 8) break;
                IList<int> writers = null;
                try { writers = RobloxLog.WritersOf(f.Name); } catch { }
                if (writers == null) continue;
                if (writers.Contains(pid)) return f.FullName;
            }
            Process[] clients = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (Process p in clients) p.Dispose();
            return clients.Length == 1 ? newest : null;
        }

        static IEnumerable<string> ReadShared(string path)
        {
            List<string> lines = new List<string>();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader r = new StreamReader(fs))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                    if (line.IndexOf("Joining game", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("UDMUX Address", StringComparison.OrdinalIgnoreCase) >= 0) lines.Add(line);
            }
            return lines;
        }

        void Show()
        {
            if (glass == null || !on) return;
            bool known = pingKnown && server != null;
            string text = GlassMath.StatsLine(wantPing, pingMs, known, pingGaveUp, wantMemory, memory, cpu, cpuKnown);
            if (wantPing && pingKnown && server == null) text = text.Replace("Ping ...", "Ping: no server to ask yet");
            if (text != shownText)
            {
                using (Bitmap b = GlassDraw.Pill(text))
                    if (glass.SetPicture(b, 100)) { shownText = text; shownSize = b.Size; }
            }
            if (shownText != null)
            {
                glass.ShowAt(GlassMath.Corner(gameAt, shownSize, corner));
                glass.Raise();
            }
        }

        public void Dispose()
        {
            if (tick != null) tick.Dispose();
            if (glass != null) glass.Dispose();
            tick = null; glass = null;
        }
    }

    // What the three keys do: the one that switches the crosshair, the one
    // that zooms while held, and the one that holds another key down.
    //
    // Decided here with no hook and no window, so it can be checked: given a
    // key going down or up, what should happen, and should the game be kept
    // from seeing that key.
    //
    // The game sees the crosshair's key and the zoom key as well: keeping
    // them from it took the letter away from anyone who chose one for zoom,
    // and they only need to be noticed, not held back. The one key that is
    // kept is the one that switches "hold a key for me" - the game must not
    // see that tap, or it would see the held key let go the moment it came up.
    class KeyRules
    {
        public byte CrossKey, ZoomKey, HoldKey, HoldWith;
        public bool ZoomOn, HoldOn;

        public bool Zoomed { get; private set; }
        public bool Holding { get; private set; }
        bool crossDown, holdDown;

        public Action ToggleCross = delegate { };
        public Action<bool> Zoom = delegate { };            // true: in; false: back out
        public Action<byte, bool> Send = delegate { };      // press or let go of a key, as if typed

        // Keys the app itself answers to - the key that stops macros above
        // all. A key is checked against them when it is chosen, but they can
        // be changed afterwards in another window; asked again at every
        // press, one of them is never taken for holding a key, whatever was
        // chosen here. (The other two keys are never kept from anything, so
        // for them it makes no difference.)
        public Func<byte, bool> Spared = delegate { return false; };

        public bool Any { get { return CrossKey != 0 || (ZoomOn && ZoomKey != 0) || (HoldOn && HoldKey != 0); } }

        byte Trigger { get { return HoldWith != 0 ? HoldWith : HoldKey; } }

        // True when the game should not see this key: only for the key that
        // switches the held key.
        public bool Key(byte vk, bool down)
        {
            if (vk == 0) return false;
            if (CrossKey != 0 && vk == CrossKey)
            {
                // Held down, a key repeats; it switches once.
                if (down && !crossDown) ToggleCross();
                crossDown = down;
                return false;
            }
            if (ZoomOn && ZoomKey != 0 && vk == ZoomKey)
            {
                if (down != Zoomed) { Zoomed = down; Zoom(down); }
                return false;
            }
            if (HoldOn && HoldKey != 0 && vk == Trigger)
            {
                if (Spared(vk)) return false;
                if (down && !holdDown)
                {
                    Holding = !Holding;
                    Send(HoldKey, Holding);
                }
                holdDown = down;
                return true;
            }
            return false;
        }

        // The game is no longer in front, or the extras are being switched
        // off: nothing stays zoomed and no key stays held.
        public void LetGo()
        {
            if (Zoomed) { Zoomed = false; Zoom(false); }
            if (Holding) { Holding = false; Send(HoldKey, false); }
            crossDown = holdDown = false;
        }
    }

    // The keys, listened for only while a Roblox window is in front.
    //
    // A keyboard hook is the one way to see a key go down *and* come back up
    // and to keep the game from acting on it. It is put in when a game comes
    // to the front and taken out when it leaves, so no other program's keys
    // ever pass through here; it looks only for the keys chosen, and keeps
    // nothing. Keys this app itself sends are let through untouched.
    class KeysListener : IGlassPiece, IDisposable
    {
        public readonly KeyRules Rules = new KeyRules();
        Xn.KeyHookProc proc;        // kept, so it is not collected while Windows still calls it
        IntPtr hook;
        Point middle;
        int zoomLevel = 3;
        bool magReady;

        // The screen is now magnified this many times, 1 for not at all.
        // Raised on the overlay thread; the crosshair uses it to draw itself
        // smaller, since the magnifier would make it as big as the rest.
        public Action<int> ZoomChanged;

        // The note that says a key is being held, and where the game is.
        GlassWindow note;
        string noteText;
        Rectangle gameAt;

        public KeysListener()
        {
            Rules.Zoom = DoZoom;
            Magnify = MagnifyScreen;
            Rules.Send = delegate(byte vk, bool down)
            {
                InputSender.SendScan(vk, down);
                if (down) ShowNote(); else HideNote();
            };
        }

        public bool Wanted { get { return Rules.Any; } }

        public void Set(byte crossKey, bool zoomOn, byte zoomKey, int zoomLevel, bool holdOn, byte holdKey, byte holdWith)
        {
            Rules.LetGo();
            Rules.CrossKey = crossKey;
            Rules.ZoomOn = zoomOn; Rules.ZoomKey = zoomKey;
            Rules.HoldOn = holdOn; Rules.HoldKey = holdKey; Rules.HoldWith = holdWith;
            this.zoomLevel = Math.Max(2, zoomLevel);
            if (!Rules.Any) Away();
        }

        public void Place(IntPtr game, Rectangle onScreen)
        {
            middle = new Point(onScreen.Left + onScreen.Width / 2, onScreen.Top + onScreen.Height / 2);
            gameAt = onScreen;
            if (Rules.Holding) ShowNote();      // the game moved or was resized: the note follows
            if (hook != IntPtr.Zero) return;
            if (proc == null) proc = Heard;
            hook = Xn.SetWindowsHookExW(Xn.WH_KEYBOARD_LL, proc, Xn.GetModuleHandleW(IntPtr.Zero), 0);
        }

        public void Away()
        {
            Rules.LetGo();
            HideNote();
            if (hook != IntPtr.Zero) Xn.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        public void Again() { if (note != null) note.Raise(); }

        // Drawn like the read-out: a dark rounded label with white text.
        void ShowNote()
        {
            if (gameAt.IsEmpty) return;
            if (note == null) note = new GlassWindow();
            string text = GlassMath.HoldNoteText(Rules.HoldKey, Rules.HoldWith);
            if (text != noteText)
            {
                using (Bitmap b = GlassDraw.Pill(text))
                    if (note.SetPicture(b, 100)) noteText = text;
            }
            if (noteText == null) return;
            note.ShowAt(GlassMath.HoldNoteAt(gameAt, note.PictureSize));
            note.Raise();
        }

        void HideNote()
        {
            if (note != null) note.Hide();
        }

        IntPtr Heard(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    Xn.KBDLLHOOKSTRUCT k = (Xn.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Xn.KBDLLHOOKSTRUCT));
                    if ((k.flags & Xn.LLKHF_INJECTED) == 0 && Rules.Key(GlassMath.Plain(k.vkCode), (k.flags & Xn.LLKHF_UP) == 0))
                        return new IntPtr(1);
                }
            }
            catch { }
            return Xn.CallNextHookEx(hook, code, wParam, lParam);
        }

        // What magnifies the screen: this many times, with the top-left of what
        // is shown here. True when it was done. Tests put their own here, so
        // that none of them ever magnifies the screen of whoever runs them.
        public Func<int, Point, bool> Magnify;

        bool MagnifyScreen(int level, Point from)
        {
            try
            {
                if (!magReady) magReady = Xn.MagInitialize();
                if (!magReady) return false;
                Xn.MagSetFullscreenTransform(level, from.X, from.Y);
                return true;
            }
            catch { return false; }
        }

        void DoZoom(bool closer)
        {
            int level = closer ? zoomLevel : 1;
            if (!Magnify(level, closer ? GlassMath.ZoomOffset(middle, zoomLevel) : Point.Empty)) return;
            Action<int> told = ZoomChanged;
            if (told != null) { try { told(level); } catch { } }
        }

        public void Dispose()
        {
            Away();
            if (magReady) { try { Xn.MagUninitialize(); } catch { } magReady = false; }
            if (note != null) note.Dispose();
            note = null;
        }
    }
}
