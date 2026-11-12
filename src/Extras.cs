using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The on-screen extras as the rest of the app sees them: what is
    // chosen, and one call to change it.
    //
    // Nothing runs until something is switched on. A PC where nobody ever
    // opened these windows has no extra thread, no hook and no timer; the
    // only cost is reading a small file once when the app starts.
    static class Extras
    {
        static readonly object gate = new object();
        static GlassHost host;
        static ExtrasSettings now = new ExtrasSettings();
        static string path;
        static IShell shell;
        static bool booted;

        static DimPiece dim;
        static KeysPiece keys;
        static StatsPiece stats;
        static CrossPiece cross;
        static KeysListener listener;

        // The crosshair's key switched it. Raised on the overlay thread.
        public static event Action Switched;

        // The program itself, and not the test runner or anything else
        // built from the same sources. The extras put windows on the screen
        // and read a file in the user's profile; a test must do neither.
        static bool IsTheApp()
        {
            try
            {
                Assembly entry = Assembly.GetEntryAssembly();
                return entry != null && entry.EntryPoint != null && entry.EntryPoint.DeclaringType == typeof(Program);
            }
            catch { return false; }
        }

        // Once, when the main window is built. `anchor` is any control that
        // ends up on that window: it is how the clients' names are found,
        // for "only on these clients".
        public static void Boot(Control anchor)
        {
            if (booted || !IsTheApp()) return;
            booted = true;
            if (anchor != null)
            {
                EventHandler find = delegate { IShell s = anchor.FindForm() as IShell; if (s != null) shell = s; };
                anchor.ParentChanged += find;
                find(anchor, EventArgs.Empty);
            }
            Load(ExtrasSettings.DefaultPath);
            Application.ApplicationExit += delegate { Stop(); };
        }

        // Reads what was chosen and starts whatever is on. Tests call this
        // with a path of their own; the app calls it through Boot.
        public static void Load(string settingsPath)
        {
            path = settingsPath;
            ExtrasSettings s = ExtrasSettings.Load(path);
            lock (gate) now = s;
            if (Needs(s)) Push(s);
        }

        public static void UseShell(IShell s) { if (s != null) shell = s; }

        public static bool Running { get { return host != null && host.Running; } }
        public static bool Loaded { get { return path != null; } }

        public static ExtrasSettings Current { get { lock (gate) return now.Clone(); } }

        static bool Needs(ExtrasSettings s)
        {
            return s.AnyOn || s.CrossKey != 0;
        }

        // What was chosen in a window: kept, saved, and shown at once.
        public static void Apply(ExtrasSettings s)
        {
            s = s.Clone();
            s.Tidy();
            lock (gate) now = s;
            if (path != null) s.Save(path);
            Push(s);
        }

        // Shown without being kept - the editor trying a crosshair on.
        public static void Try(ExtrasSettings s)
        {
            s = s.Clone();
            s.Tidy();
            Push(s);
        }

        static void Push(ExtrasSettings s)
        {
            if (host == null)
            {
                if (!Needs(s)) return;
                host = new GlassHost();
                host.Chosen = Chosen;
                host.Start();
                host.Post(delegate
                {
                    // In the order they stack: the dim underneath, the crosshair on top.
                    dim = new DimPiece();
                    keys = new KeysPiece();
                    stats = new StatsPiece();
                    cross = new CrossPiece();
                    listener = new KeysListener();
                    listener.Rules.ToggleCross = ToggleCross;
                    listener.Rules.Spared = AppsOwnKey;
                    // The magnifier makes everything bigger, the crosshair's glass
                    // window included; it is told so and draws itself smaller.
                    listener.ZoomChanged = delegate(int level)
                    {
                        CrossPiece piece = cross;
                        GlassHost h = host;
                        if (piece == null) return;
                        piece.SetZoom(level);
                        if (h != null) h.Changed();
                    };
                    host.Add(dim); host.Add(keys); host.Add(stats); host.Add(cross); host.Add(listener);
                });
            }
            ExtrasSettings c = s.Clone();
            host.Post(delegate
            {
                dim.Set(c.DimOn, ExtrasSettings.DarkPercent[c.DimDark], ExtrasSettings.HolePercent[c.DimHole]);
                keys.Set(c.KeysOn, c.KeysCorner, c.KeysSize);
                stats.Set(c.PingOn, c.MemoryOn, c.StatsCorner);
                cross.Set(c.CrossOn, c.Cross);
                listener.Set(c.CrossKey, c.ZoomOn, c.ZoomKey, ExtrasSettings.ZoomLevels[c.ZoomLevel], c.HoldOn, c.HoldKey, c.HoldWith);
                host.Changed();
            });
        }

        // The crosshair's key: on becomes off and off becomes on, and it is
        // remembered. Overlay thread.
        static void ToggleCross()
        {
            ExtrasSettings s;
            lock (gate)
            {
                now.CrossOn = !now.CrossOn;
                s = now.Clone();
            }
            cross.Set(s.CrossOn, s.Cross);
            host.Changed();
            string to = path;
            if (to != null) ThreadPool.QueueUserWorkItem(delegate { s.Save(to); });
            Action heard = Switched;
            if (heard != null) { try { heard(); } catch { } }
        }

        // Why the crosshair's own picture can't be shown, if it can't.
        public static string CrossProblem
        {
            get { return host == null || cross == null ? null : host.Ask<string>(delegate { return cross.Problem; }, null); }
        }

        // Whether the extras are wanted on this client. With nobody named,
        // on every one.
        static bool Chosen(uint pid)
        {
            List<string> only;
            lock (gate) only = now.Only;
            if (only.Count == 0) return true;
            IShell s = shell;
            if (s == null) return false;
            try
            {
                foreach (WatchedClient c in s.Clients())
                    if (c.Pid == (int)pid) return CrossPlace.Chosen(only, c.AccountName, c.Label);
            }
            catch { }
            return false;
        }

        // The clients open now, by the name the user knows each by.
        public static List<string> ClientNames()
        {
            List<string> names = new List<string>();
            IShell s = shell;
            if (s == null) return names;
            try
            {
                foreach (WatchedClient c in s.Clients())
                {
                    string name = !string.IsNullOrEmpty(c.AccountName) ? c.AccountName : c.Label;
                    if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
                }
            }
            catch { }
            return names;
        }

        // The middle of a client's picture as it is right now, for the
        // editor to show the crosshair on. Null when no client is open.
        public static Bitmap GameMiddle(Size size)
        {
            IShell s = shell;
            if (s == null) return null;
            try
            {
                foreach (WatchedClient c in s.Clients())
                {
                    IntPtr hwnd = WindowCapture.GameWindow(c.Pid);
                    if (hwnd == IntPtr.Zero || Xn.IsIconic(hwnd)) continue;
                    Pixels p = WindowCapture.Grab(hwnd);
                    if (p == null || p.IsEmpty || p.Width < size.Width || p.Height < size.Height) continue;
                    Pixels mid = p.Crop(new Rectangle((p.Width - size.Width) / 2, (p.Height - size.Height) / 2, size.Width, size.Height));
                    Bitmap b = new Bitmap(mid.Width, mid.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    System.Drawing.Imaging.BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try { System.Runtime.InteropServices.Marshal.Copy(mid.Argb, 0, d.Scan0, mid.Argb.Length); }
                    finally { b.UnlockBits(d); }
                    return b;
                }
            }
            catch { }
            return null;
        }

        // ---------- keys ----------

        // Null when this key may be used for `what`; otherwise why not, in a
        // line someone can act on. `s` is what is chosen so far in the window
        // asking; `mine` is the key being replaced, which is not a clash.
        public static string KeyProblem(byte vk, ExtrasSettings s, string what, IMacroHost macros, byte shotKey)
        {
            if (vk == 0) return "No key was pressed.";
            string name = MacroKeys.Name(vk);
            if (vk == 0x1B || vk == 0x5B || vk == 0x5C) return name + " can't be used. Choose another.";
            if (vk == MacroRecording.STOP_KEY) return name + " ends a macro recording. Choose another.";
            if (macros != null)
            {
                if (vk == macros.StopKey) return name + " is the key that stops macros. Choose another.";
                if (macros.Clicker != null && macros.Clicker.Enabled && vk == macros.Clicker.ToggleVk)
                    return name + " switches the autoclicker. Choose another.";
            }
            if (shotKey != 0 && vk == shotKey) return name + " takes screenshots. Choose another.";
            // The game sees the crosshair's key and the zoom key too, so a key it
            // already uses would do two things at once. The key that switches the
            // held key is kept from the game, which would then never see it.
            if (what == "cross" || what == "zoom")
            {
                if (IsMoveKey(vk)) return name + " moves you in most games, so it would do both things. Choose another.";
            }
            else if (what != "hold" && IsMoveKey(vk)) return name + " moves you in most games, and the game would stop seeing it. Choose another.";

            byte trigger = s.HoldOn ? (s.HoldWith != 0 ? s.HoldWith : s.HoldKey) : (byte)0;
            if (what != "cross" && s.CrossKey == vk) return name + " already switches the crosshair. Choose another.";
            if (what != "zoom" && s.ZoomOn && s.ZoomKey == vk) return name + " is already the zoom key. Choose another.";
            if (what != "hold" && what != "holdwith" && trigger == vk) return name + " already holds a key for you. Choose another.";
            return null;
        }

        // A key the app answers to right now: stopping macros, switching the
        // autoclicker, taking a screenshot, ending a recording. Asked at
        // every press, on the overlay thread - these are single values read
        // as they stand.
        static bool AppsOwnKey(byte vk)
        {
            if (vk == MacroRecording.STOP_KEY) return true;
            IShell s = shell;
            if (s == null) return false;
            try
            {
                if (s.ShotKey != 0 && vk == s.ShotKey) return true;
                IMacroHost m = s.Macros;
                if (m == null) return false;
                if (vk == m.StopKey) return true;
                return m.Clicker != null && m.Clicker.Enabled && vk == m.Clicker.ToggleVk;
            }
            catch { return false; }
        }

        // Whether some program - this one included - already has this key as
        // a shortcut that works everywhere. Windows gives such a key to one
        // owner only, so asking for it and being refused is the answer; it is
        // given straight back when it was free. Shift, Ctrl and Alt can't be
        // asked about this way and are taken to be free. Of this app's own
        // keys only the stop key is still asked of Windows, and only while
        // something plays; the rest are heard by GlobalKeys and are looked up
        // in KeyProblemLive instead.
        public static bool KeyTaken(byte vk)
        {
            if (vk == 0 || (vk >= 0x10 && vk <= 0x12)) return false;
            const int PROBE = 0x5EF7;
            try
            {
                if (!Xn.RegisterHotKey(IntPtr.Zero, PROBE, 0, vk)) return true;
                Xn.UnregisterHotKey(IntPtr.Zero, PROBE);
            }
            catch { }
            return false;
        }

        // KeyProblem, and then whether the key is somebody's shortcut already.
        public static string KeyProblemLive(byte vk, ExtrasSettings s, string what, IMacroHost macros, byte shotKey)
        {
            string why = KeyProblem(vk, s, what, macros, shotKey);
            if (why != null) return why;
            // The main window's one check: the rules' keys and every macro's
            // own key, as well as its own keys KeyProblem has looked at.
            if (macros != null)
            {
                why = macros.KeyUseProblem(vk, KeyUse.EXTRAS, null);
                if (why != null) return why;
            }
            // The rules' keys are heard without being taken from anyone, so
            // asking Windows below no longer finds them - they are looked up.
            // The autoclicker's key and the screenshot key KeyProblem has
            // already said; any key left that the app listens for is a rule's.
            if (vk != 0 && GlobalKeys.ListensFor(RuleHotkeys.Key(vk, false, false, false)))
                return MacroKeys.Name(vk) + " already plays one of your rules. Choose another.";
            // Only the key that switches the held key is kept from the game, so
            // only it matters if another program already has it as a shortcut.
            // The crosshair's and the zoom key are let through, and the held key
            // itself is only sent, unless it is its own switch.
            bool swallowed = what == "holdwith" || (what == "hold" && s.HoldWith == 0);
            if (swallowed && KeyTaken(vk))
                return MacroKeys.Name(vk) + " is already a shortcut - in RobloxKeeper's rules or in another program. Choose another.";
            return null;
        }

        public static bool IsMoveKey(byte vk)
        {
            return vk == 0x57 || vk == 0x41 || vk == 0x53 || vk == 0x44 || vk == 0x20;     // W A S D Space
        }

        public static void Stop()
        {
            GlassHost h = host;
            if (h == null) return;
            host = null;
            try
            {
                h.Ask<bool>(delegate
                {
                    if (listener != null) listener.Dispose();
                    if (cross != null) cross.Dispose();
                    if (keys != null) keys.Dispose();
                    if (dim != null) dim.Dispose();
                    if (stats != null) stats.Dispose();
                    return true;
                }, false);
                h.Stop();
            }
            catch { }
        }
    }
}
