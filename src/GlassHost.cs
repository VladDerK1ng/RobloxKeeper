using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A see-through window that shows one picture on top of the game.
    //
    // It is a layered window: Windows blends its picture over whatever is
    // underneath. It is marked so that clicks fall through it to the game,
    // it can never be made the active window, and it is in no taskbar or
    // Alt+Tab list. Nothing is drawn until the picture changes - Windows
    // keeps the picture, so a window that is only sitting there costs
    // nothing.
    class GlassWindow : NativeWindow, IDisposable
    {
        bool shown;
        Point at = new Point(int.MinValue, int.MinValue);

        public GlassWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "RobloxKeeper overlay";
            cp.Style = Xn.WS_POPUP;
            cp.ExStyle = Xn.WS_EX_LAYERED | Xn.WS_EX_TRANSPARENT | Xn.WS_EX_NOACTIVATE | Xn.WS_EX_TOOLWINDOW | Xn.WS_EX_TOPMOST;
            cp.X = cp.Y = 0;
            cp.Width = cp.Height = 1;
            CreateHandle(cp);
        }

        public bool Shown { get { return shown; } }
        public Size PictureSize { get; private set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Xn.WM_NCHITTEST) { m.Result = new IntPtr(Xn.HTTRANSPARENT); return; }
            if (m.Msg == Xn.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Xn.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        // The picture this window shows, see-through wherever the picture is.
        public bool SetPicture(Bitmap picture, int opacityPercent)
        {
            if (Handle == IntPtr.Zero || picture == null) return false;
            IntPtr screen = Xn.GetDC(IntPtr.Zero), mem = Xn.CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = picture.GetHbitmap(Color.FromArgb(0));
                old = Xn.SelectObject(mem, hbmp);
                Xn.SIZE size; size.W = picture.Width; size.H = picture.Height;
                Xn.POINT src; src.X = src.Y = 0;
                Xn.POINT dst; dst.X = at.X == int.MinValue ? 0 : at.X; dst.Y = at.Y == int.MinValue ? 0 : at.Y;
                Xn.BLENDFUNCTION blend;
                blend.Op = Xn.AC_SRC_OVER; blend.Flags = 0; blend.Format = Xn.AC_SRC_ALPHA;
                blend.Alpha = (byte)Math.Max(0, Math.Min(255, opacityPercent * 255 / 100));
                bool ok = Xn.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Xn.ULW_ALPHA);
                if (ok) PictureSize = picture.Size;
                return ok;
            }
            catch { return false; }
            finally
            {
                if (old != IntPtr.Zero) Xn.SelectObject(mem, old);
                if (hbmp != IntPtr.Zero) Xn.DeleteObject(hbmp);
                Xn.DeleteDC(mem);
                Xn.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // Shown with its top-left corner here, on top, without being made
        // the active window. Asked again for the same place, it does nothing.
        public void ShowAt(Point p)
        {
            if (Handle == IntPtr.Zero) return;
            if (shown && p == at) return;
            at = p;
            Xn.SetWindowPos(Handle, Xn.HWND_TOPMOST, p.X, p.Y, 0, 0, Xn.SWP_NOSIZE | Xn.SWP_NOACTIVATE | Xn.SWP_SHOWWINDOW);
            shown = true;
        }

        // Back above everything else that is always-on-top - the shader
        // overlay is, and shows a copy of the game that does not have this in it.
        public void Raise()
        {
            if (shown) Xn.SetWindowPos(Handle, Xn.HWND_TOPMOST, 0, 0, 0, 0, Xn.SWP_NOSIZE | Xn.SWP_NOMOVE | Xn.SWP_NOACTIVATE);
        }

        public void Hide()
        {
            if (!shown || Handle == IntPtr.Zero) return;
            Xn.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Xn.SWP_NOSIZE | Xn.SWP_NOMOVE | Xn.SWP_NOACTIVATE | Xn.SWP_HIDEWINDOW | 0x4 /* no z-order change */);
            shown = false;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    // Something that draws itself over the game: the crosshair, the keys
    // display. Told where the game is, or that there is none to draw over.
    // Always called on the overlay thread.
    interface IGlassPiece
    {
        bool Wanted { get; }                            // false: switched off, nothing to show anywhere
        void Place(IntPtr game, Rectangle onScreen);    // the game window in front and where its picture is, in real pixels
        void Away();                                    // no game in front: hide
        void Again();                                   // the same game is still in front, where it was: stay on top of it
    }

    // The thread the on-screen extras live on, and what decides when they
    // show: only over a Roblox window, only while it is the window in front.
    //
    // It is a thread of its own for two reasons. Its windows are measured in
    // real pixels, which the rest of the app is not. And a key or the move
    // of a window is answered here at once, whatever the main window is busy
    // with.
    //
    // Idle cost is the point of the design. With nothing switched on there
    // is no hook and no timer. With something on and no game in front, it
    // waits for Windows to say the window in front changed. Only while a
    // game is in front does a timer look, five times a second, at where that
    // window is - two questions to Windows, no drawing unless it moved.
    class GlassHost
    {
        const string GAME_CLASS = "WINDOWSCLIENT";
        const string GAME_PROCESS = "RobloxPlayerBeta";
        const int FOLLOW_MS = 200;      // while a game is in front: is it still there, has it moved
        const int WATCH_MS = 1000;      // while none is: has one come to the front that no event told us of

        readonly List<IGlassPiece> pieces = new List<IGlassPiece>();
        readonly Dictionary<int, Action> keys = new Dictionary<int, Action>();
        readonly Dictionary<uint, bool> isGame = new Dictionary<uint, bool>();
        Thread thread;
        SynchronizationContext ctx;
        KeyWindow keyWindow;
        System.Windows.Forms.Timer follow;
        Xn.WinEventProc onEvent;        // kept, so it is not collected while Windows still calls it
        IntPtr hookFront, hookMin;
        IntPtr game;
        Rectangle gameAt;
        volatile bool running;

        // For a test window standing in for Roblox.
        public Func<IntPtr, bool> IsGame { get; set; }
        // Whether the crosshair and the rest are wanted on this client.
        public Func<uint, bool> Chosen { get; set; }
        // What happened, for the spike's log. Null in the app.
        public Action<string> Trace { get; set; }

        public bool Running { get { return running; } }
        public bool RealPixels { get; private set; }
        public IntPtr Game { get { return game; } }

        class KeyWindow : NativeWindow
        {
            readonly GlassHost host;
            public KeyWindow(GlassHost host)
            {
                this.host = host;
                CreateParams cp = new CreateParams();
                cp.Parent = new IntPtr(-3);     // message-only: never on screen
                CreateHandle(cp);
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Xn.WM_HOTKEY)
                {
                    Action a;
                    if (host.keys.TryGetValue(m.WParam.ToInt32(), out a) && a != null)
                    {
                        try { a(); } catch { }
                    }
                    return;
                }
                base.WndProc(ref m);
            }
        }

        public void Start()
        {
            if (thread != null) return;
            ManualResetEvent ready = new ManualResetEvent(false);
            thread = new Thread(delegate()
            {
                RealPixels = Xn.RealPixelsOnThisThread();
                keyWindow = new KeyWindow(this);
                ctx = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(ctx);
                follow = new System.Windows.Forms.Timer();
                follow.Interval = FOLLOW_MS;
                follow.Tick += delegate { Look(); };
                onEvent = delegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint th, uint time)
                {
                    if (Trace != null) Trace("event " + ev.ToString("X") + " for " + hwnd.ToInt64().ToString("X") + " (" + Xn.ClassOf(hwnd) + "); front=" + Xn.GetForegroundWindow().ToInt64().ToString("X"));
                    Look();
                };
                running = true;
                ready.Set();
                try { Application.Run(); }
                finally { running = false; }
            });
            thread.IsBackground = true;
            thread.Name = "RobloxKeeper overlay";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(5000);
        }

        // On the overlay thread, without waiting for it.
        public void Post(Action work)
        {
            SynchronizationContext c = ctx;
            if (c == null || !running) return;
            try { c.Post(delegate { try { work(); } catch { } }, null); } catch { }
        }

        // On the overlay thread, and wait for the answer.
        public T Ask<T>(Func<T> work, T otherwise)
        {
            SynchronizationContext c = ctx;
            if (c == null || !running) return otherwise;
            T answer = otherwise;
            try { c.Send(delegate { try { answer = work(); } catch { } }, null); } catch { }
            return answer;
        }

        public void Stop()
        {
            if (thread == null) return;
            Ask<bool>(delegate
            {
                Unhook();
                foreach (IGlassPiece p in pieces) p.Away();
                foreach (int id in new List<int>(keys.Keys)) Xn.UnregisterHotKey(keyWindow.Handle, id);
                keys.Clear();
                follow.Dispose();
                Application.ExitThread();
                return true;
            }, false);
            thread = null;
        }

        // Overlay thread only.
        public void Add(IGlassPiece piece)
        {
            pieces.Add(piece);
            Changed();
        }

        // A key that works while any window is in front. The game does not
        // see it. True when Windows gave it to us; false when something else
        // already has it. Asking for key 0 lets go of it.
        public bool SetKey(int id, byte vk, Action pressed)
        {
            return Ask<bool>(delegate
            {
                if (keys.ContainsKey(id)) { Xn.UnregisterHotKey(keyWindow.Handle, id); keys.Remove(id); }
                if (vk == 0) return true;
                if (!Xn.RegisterHotKey(keyWindow.Handle, id, Xn.MOD_NOREPEAT, vk)) return false;
                keys[id] = pressed;
                return true;
            }, false);
        }

        // Something was switched on, off or redrawn: look again. Overlay
        // thread only - from elsewhere, Post(host.Changed).
        public void Changed()
        {
            bool any = false;
            foreach (IGlassPiece p in pieces) if (p.Wanted) any = true;
            if (any) Hook(); else Unhook();
            game = IntPtr.Zero;         // so every piece is placed afresh
            gameAt = Rectangle.Empty;
            if (any) Look();
            else foreach (IGlassPiece p in pieces) p.Away();
        }

        void Hook()
        {
            if (hookFront != IntPtr.Zero) return;
            hookFront = Xn.SetWinEventHook(Xn.EVENT_SYSTEM_FOREGROUND, Xn.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, onEvent, 0, 0, Xn.WINEVENT_OUTOFCONTEXT);
            hookMin = Xn.SetWinEventHook(Xn.EVENT_SYSTEM_MINIMIZESTART, Xn.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, onEvent, 0, 0, Xn.WINEVENT_OUTOFCONTEXT);
        }

        void Unhook()
        {
            if (hookFront != IntPtr.Zero) Xn.UnhookWinEvent(hookFront);
            if (hookMin != IntPtr.Zero) Xn.UnhookWinEvent(hookMin);
            hookFront = hookMin = IntPtr.Zero;
            if (follow != null) follow.Stop();
        }

        // Windows says when the window in front changes, and that is what
        // makes the extras appear at once. It is not relied on alone: the
        // shell passes the foreground through a window of its own on the way
        // (seen on this PC as "ForegroundStaging"), and a look made at that
        // instant sees no game. So a slow timer looks again.
        void Pace(int ms)
        {
            if (follow.Interval != ms) follow.Interval = ms;
            if (!follow.Enabled) follow.Start();
        }

        bool GameWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (IsGame != null) return IsGame(hwnd);
            if (Xn.ClassOf(hwnd) != GAME_CLASS) return false;
            uint pid;
            Xn.GetWindowThreadProcessId(hwnd, out pid);
            bool yes;
            if (isGame.TryGetValue(pid, out yes)) return yes;
            try { using (Process p = Process.GetProcessById((int)pid)) yes = string.Equals(p.ProcessName, GAME_PROCESS, StringComparison.OrdinalIgnoreCase); }
            catch { yes = false; }
            if (isGame.Count > 64) isGame.Clear();
            isGame[pid] = yes;
            return yes;
        }

        // Which window is in front, whether it is a game the extras are
        // wanted on, and where its picture is. Pieces hear only of a change.
        void Look()
        {
            IntPtr front = Xn.GetForegroundWindow();
            IntPtr now = IntPtr.Zero;
            Rectangle where = Rectangle.Empty;
            if (GameWindow(front) && !Xn.IsIconic(front))
            {
                uint pid;
                Xn.GetWindowThreadProcessId(front, out pid);
                Xn.RECT r;
                if ((Chosen == null || Chosen(pid)) && Xn.GetClientRect(front, out r))
                {
                    Xn.POINT p; p.X = p.Y = 0;
                    Xn.ClientToScreen(front, ref p);
                    where = new Rectangle(p.X, p.Y, r.R - r.L, r.B - r.T);
                    if (where.Width >= 200 && where.Height >= 200) now = front;
                }
            }

            if (now == IntPtr.Zero)
            {
                Pace(WATCH_MS);
                if (game != IntPtr.Zero)
                {
                    game = IntPtr.Zero;
                    gameAt = Rectangle.Empty;
                    foreach (IGlassPiece p in pieces) p.Away();
                    if (Trace != null) Trace("away; front=" + front.ToInt64().ToString("X"));
                }
                return;
            }

            Pace(FOLLOW_MS);
            if (now == game && where == gameAt)
            {
                // Measured on a live Roblox client with a shader look on: the
                // shader overlay is always-on-top too, shows a copy of the
                // game that has none of this in it, and puts itself on top
                // when its game comes to the front - so a piece raised at that
                // same moment ended up underneath, shown and invisible. Raised
                // again a fifth of a second later it was on top and stayed
                // (60ms later was too soon). So each look raises them again,
                // in the order they were added, which is the order they stack in.
                foreach (IGlassPiece p in pieces) if (p.Wanted) p.Again();
                return;
            }
            game = now;
            gameAt = where;
            foreach (IGlassPiece p in pieces)
            {
                if (p.Wanted) p.Place(now, where); else p.Away();
            }
            if (Trace != null) Trace("placed over " + now.ToInt64().ToString("X") + " at " + where + "; front after=" + Xn.GetForegroundWindow().ToInt64().ToString("X"));
        }
    }

    // The crosshair on the glass.
    class CrossPiece : IGlassPiece, IDisposable
    {
        GlassWindow glass;
        CrossStyle style = new CrossStyle();
        bool on, drawn, tried;
        // What the picture on the glass was drawn for: a game this even or
        // odd across and down, at this zoom; and where its shape's middle is.
        bool drawnEvenWide, drawnEvenHigh;
        Point middle2;
        CrossStyle drawnAs = new CrossStyle();
        // The game it was last placed over, for Again(), which is not told.
        Rectangle where;
        bool placed;

        public string Problem { get; private set; }     // why the user's own picture can't be shown
        public bool Wanted { get { return on; } }
        public GlassWindow Glass { get { return glass; } }

        // While the screen is magnified this many times (1 when it is not).
        // Overlay thread only; the caller then tells the host it Changed().
        public void SetZoom(int level)
        {
            zoom = level < 1 ? 1 : level;
            drawn = tried = false;
        }

        int zoom = 1;

        // Overlay thread only. The caller then tells the host it Changed().
        public void Set(bool switchedOn, CrossStyle to)
        {
            on = switchedOn;
            style = to.Clone();
            drawn = tried = false;
            if (!on && glass != null) glass.Hide();
        }

        // Drawn smaller while the screen is magnified, so that magnified it
        // is the size chosen; and drawn for the game's even or odd size, so
        // its middle is exactly on the game's (CrossArt.Middle2).
        void Draw(bool evenWide, bool evenHigh)
        {
            if (glass == null) glass = new GlassWindow();
            tried = true;
            drawnAs = style.ScaledFor(zoom);
            drawnEvenWide = evenWide;
            drawnEvenHigh = evenHigh;
            middle2 = CrossArt.Middle2(drawnAs, evenWide, evenHigh);
            string why;
            using (Bitmap b = CrossArt.Draw(drawnAs, evenWide, evenHigh, out why))
            {
                Problem = why;
                drawn = b != null && glass.SetPicture(b, style.Opacity);
            }
        }

        public void Place(IntPtr game, Rectangle onScreen)
        {
            where = onScreen;
            placed = true;
            if (!drawn) tried = false;      // a new game, or the same moved: one more try
            Show();
        }

        // Drawn again only when something it was drawn for has changed; then
        // shown where it goes, unless it is for aiming only and the game is
        // not holding the mouse.
        void Show()
        {
            bool evenWide = where.Width % 2 == 0, evenHigh = where.Height % 2 == 0;
            // A picture that could not be read is not read again five times a
            // second; only once something about it is changed (Set, SetZoom).
            if (!tried || evenWide != drawnEvenWide || evenHigh != drawnEvenHigh) Draw(evenWide, evenHigh);
            if (!drawn) { if (glass != null) glass.Hide(); return; }
            if (style.AimOnly)
            {
                Xn.POINT m;
                bool aiming = Xn.GetCursorPos(out m) && CrossPlace.AimShows(true, new Point(m.X, m.Y), where);
                if (!aiming) { glass.Hide(); return; }
            }
            glass.ShowAt(CrossPlace.TopLeft(where, middle2, drawnAs.NudgeX, drawnAs.NudgeY));
            glass.Raise();
        }

        public void Away()
        {
            placed = false;
            if (glass != null) glass.Hide();
        }

        // Five times a second while the same game stays in front. A
        // crosshair for aiming decides again whether the game still holds
        // the mouse; one marked stale (SetZoom, Set) is drawn again.
        public void Again()
        {
            if (placed && (style.AimOnly || !tried)) { Show(); return; }
            if (glass != null) glass.Raise();
        }

        public void Dispose()
        {
            if (glass != null) glass.Dispose();
            glass = null;
        }
    }
}
