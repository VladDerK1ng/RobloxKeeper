using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A script ended, on purpose or because something outside it changed:
    // its Stop, the stop key, stop() in the script, or another window coming
    // to the front. Thrown through the script so it stops at once.
    //
    // Deliberately not one of MoonSharp's own exceptions: a script's pcall()
    // catches those, and must never be able to catch this.
    class ScriptStopped : Exception
    {
        public ScriptStopped(string why) : base(why) { }
    }

    // Something the script asked for can't be done - a picture that isn't
    // there, a place off the window - said in plain words. The run ends and
    // says "Line N: " and this.
    class ScriptFault : Exception
    {
        public ScriptFault(string why) : base(why) { }
    }

    // What a script plays on: one Roblox window, or the whole screen.
    class ScriptTarget
    {
        public bool Screen;
        public int Pid;
        public string Label;        // "Client 2"
        public string Account;      // null for a window this app did not launch

        public static ScriptTarget OnScreen()
        {
            ScriptTarget t = new ScriptTarget();
            t.Screen = true;
            t.Label = "the screen";
            return t;
        }

        public static ScriptTarget Of(WatchedClient c)
        {
            ScriptTarget t = new ScriptTarget();
            t.Pid = c.Pid;
            t.Label = c.Label;
            t.Account = c.AccountName;
            return t;
        }

        // "Client 2 - VladDerKing", for the activity list.
        public string Name
        {
            get
            {
                if (Screen) return "the screen";
                return string.IsNullOrEmpty(Account) ? Label : Label + " - " + Account;
            }
        }

        public bool Same(ScriptTarget other)
        {
            if (other == null) return false;
            return Screen ? other.Screen : !other.Screen && other.Pid == Pid;
        }
    }

    // Everything a script touches on the PC: the Roblox windows, the keyboard
    // and mouse (through the macros' own world), the pictures, the clock. The
    // real one is LiveDesk; tests hand in their own, so every decision a
    // script's functions make is checked without a game, a keyboard or a
    // screen.
    interface IScriptDesk
    {
        // On the script's own thread, before its first function and after its
        // last: the real one puts the thread in real pixels.
        void Begin();
        void End();

        WatchedClient[] Clients();                      // the Roblox windows running now, in list order

        // Where its keys and clicks go, as a macro's world: the window's area,
        // sending, sleeping, looking for a picture. Null when that window has
        // gone. stop: the run's own stop, which the world's StopAsked says too.
        IMacroWorld World(ScriptTarget t, Func<bool> stop);

        // Sends straight to the PC, to whatever window is in front - for
        // letting go of keys held on a window that has since closed, which
        // has no world to send through. Without it, a Shift held down when
        // Roblox closed would stay down for everything on the PC.
        void SendPlain(InputAction a);

        // Waits its turn with the keyboard, as a macro does. False when kept
        // out (problem says why) or stopped while waiting (problem null).
        bool TakeKeyboard(Func<bool> stop, out string problem);
        void GiveKeyboard();
        bool BringToFront(ScriptTarget t);              // false: Windows wouldn't

        bool GateWanted { get; }                        // the nudge, or a macro about to give up waiting
        bool AnyoneWaiting { get; }                     // anything at all waiting for the keyboard

        long NowMs { get; }
        void Nap(int ms);                               // a short real wait; tests move their clock

        // A picture by the name a script gives it. False when there is none.
        bool FindPicture(string name, out string file, out string mask);

        string Read(ScriptTarget t);                    // the words on it; "" for none
        bool ColorAt(ScriptTarget t, double fx, double fy, out Color c);
    }

    // Where a running script's output goes. Called on the script's thread.
    interface IScriptOutlet
    {
        void Print(string text);        // the output pane
        void Line(int line);            // the line it is on, each time a function is called
        void Status(string text);       // the label over the game; null hides it
        void Say(string text);          // the activity list
        void Notify(string text);       // a pop-up by the clock
        void Discord(string text);      // the watchers' webhook
    }

    // Plays a script's keys and clicks, and shares the keyboard while it
    // does. Everything sent is a macro step played by MacroRunner against the
    // desk's world, so key names, the window's place, DPI, held keys let go
    // and the app's own mark on its input all work as they do for macros.
    //
    // The keyboard (FocusGate) is taken only to press or click, and kept
    // across presses that come one after another. It is given up:
    //  - whenever the anti-AFK nudge wants it, between any two things - a
    //    script never keeps it out, because the nudge gives up after a
    //    minute and the account is kicked;
    //  - at a wait, or a look, of LONG_WAIT_MS or more, when no key is held;
    //  - when nothing has been pressed for LONG_WAIT_MS (see Between).
    // It is taken back, and the window brought to the front again, by the
    // next press. Keys held down (keydown) are let go when it is given up
    // and pressed again when it is taken back - which is why a long wait
    // with a key held keeps the keyboard, unless the nudge wants it: walking
    // forward through a wait is what holding a key is for.
    //
    // While it has the keyboard on a Roblox window, another window coming to
    // the front stops it, as it stops a macro: you clicked something.
    //
    // All on the script's own thread - FocusGate is a lock, which is its
    // thread's.
    class ScriptPlayer
    {
        public const int LONG_WAIT_MS = 300;
        const int SLICE_MS = 25;

        readonly IScriptDesk desk;
        readonly Func<bool> stopAsked;
        ScriptTarget target;
        IMacroWorld world;
        bool keyboard;
        readonly List<byte> held = new List<byte>();        // keydown'd and not let go
        readonly List<byte> letGo = new List<byte>();       // let go to give the keyboard up; pressed again on taking it back
        long lastSentMs;

        public ScriptPlayer(IScriptDesk desk, ScriptTarget target, Func<bool> stop)
        {
            this.desk = desk;
            this.target = target ?? ScriptTarget.OnScreen();
            stopAsked = stop ?? delegate { return false; };
        }

        public ScriptTarget Target { get { return target; } }
        public bool HasKeyboard { get { return keyboard; } }
        public IList<byte> Held { get { return held; } }

        public void CheckStop()
        {
            if (stopAsked()) throw new ScriptStopped(MacroPlayer.CANCELLED);
        }

        // Its area, for turning places in % into points.
        public Rectangle Area { get { return World().Client; } }

        IMacroWorld World()
        {
            IMacroWorld w = desk.World(target, stopAsked);
            if (w == null) throw new ScriptStopped("the Roblox window it was playing on closed");
            world = w;
            return w;
        }

        // ---------- the keyboard ----------

        void Take()
        {
            CheckStop();
            string problem;
            if (!desk.TakeKeyboard(stopAsked, out problem))
            {
                CheckStop();
                throw new ScriptFault(problem ?? "something else was using the keyboard for too long");
            }
            keyboard = true;
            CheckStop();
            if (!target.Screen && !desk.BringToFront(target))
                throw new ScriptFault("Windows wouldn't bring the Roblox window to the front");
            IMacroWorld w = World();
            foreach (byte vk in letGo)
            {
                InputAction down = InputAction.Of(InputActionKind.KeyDown);
                down.Vk = vk;
                w.Send(down);
                if (!held.Contains(vk)) held.Add(vk);
            }
            letGo.Clear();
            lastSentMs = desk.NowMs;
        }

        // Whatever is held is let go, and remembered to press again.
        void GiveUp()
        {
            if (!keyboard) return;
            try
            {
                if (held.Count > 0)
                {
                    IMacroWorld w = desk.World(target, stopAsked);
                    foreach (byte vk in held)
                    {
                        InputAction up = InputAction.Of(InputActionKind.KeyUp);
                        up.Vk = vk;
                        // Its window gone: let go of anyway, so the key is
                        // not left down for the whole PC.
                        if (w != null) w.Send(up);
                        else desk.SendPlain(up);
                    }
                    foreach (byte vk in held) if (!letGo.Contains(vk)) letGo.Add(vk);
                    held.Clear();
                }
            }
            finally
            {
                keyboard = false;
                desk.GiveKeyboard();
            }
        }

        // Gives the keyboard to whoever is waiting, lets them in, and takes
        // it back. A lock makes no promise about whose turn is next, so the
        // waiting one is given a moment to get in first, as MacroPlayer does.
        void HandOver()
        {
            GiveUp();
            for (int i = 0; i < 150 && desk.AnyoneWaiting; i++)
            {
                CheckStop();
                desk.Nap(20);
            }
            Take();
        }

        // Has the keyboard, on its window in front, and nobody it must make
        // way for is waiting.
        void Ensure()
        {
            if (keyboard)
            {
                if (desk.GateWanted) { HandOver(); return; }
                if (!target.Screen && !World().InFront()) throw new ScriptStopped(MacroPlayer.STOPPED);
                return;
            }
            Take();
        }

        // ---------- what a script does ----------

        // Presses, clicks, typing: steps played as a macro. A long press or
        // drag steps aside part-way for the nudge, as a long macro does.
        public void Act(IList<MacroStep> steps)
        {
            CheckStop();
            foreach (MacroStep s in steps)
                if (s != null && (s.Kind == MacroStepKind.Key || s.Kind == MacroStepKind.KeyDown || s.Kind == MacroStepKind.KeyUp))
                    NotSystem(s.Vk);
            NoCtrlEsc(steps);
            Ensure();
            IMacroWorld w = World();
            Macro m = new Macro();
            m.Name = "script";
            m.Steps.AddRange(steps);
            MacroRunner run = new MacroRunner(m);
            run.StepsAside = true;
            while (true)
            {
                RunEnd end = run.Run(w);
                lastSentMs = desk.NowMs;
                switch (end)
                {
                    case RunEnd.Finished: return;
                    case RunEnd.Yielded: HandOver(); w = World(); continue;
                    case RunEnd.TakenOver: throw new ScriptStopped(MacroPlayer.STOPPED);
                    case RunEnd.Cancelled: throw new ScriptStopped(MacroPlayer.CANCELLED);
                    default: throw new ScriptFault(run.Problem ?? "it couldn't go on");
                }
            }
        }

        // A key held down across what follows, until KeyUp.
        public void KeyDown(byte vk)
        {
            CheckStop();
            NotSystem(vk);
            NoCtrlEsc(new MacroStep[] { MacroStep.KeyDown(vk) });
            Ensure();
            Send(MacroStep.KeyDown(vk));
            if (!held.Contains(vk)) held.Add(vk);
        }

        public void KeyUp(byte vk)
        {
            CheckStop();
            NotSystem(vk);
            // Let go of already, to give the keyboard up: it stays let go.
            if (letGo.Remove(vk) && !held.Contains(vk)) return;
            Ensure();
            Send(MacroStep.KeyUp(vk));
            held.Remove(vk);
        }

        // The Windows keys and the menu key, whatever asked for them: with
        // them a script could open Run and type a command (see ScriptKeys).
        static void NotSystem(byte vk)
        {
            if (ScriptKeys.IsSystem(vk)) throw new ScriptFault(ScriptKeys.SYSTEM_REFUSED);
        }

        const byte VK_ESC = 0x1B;

        static bool IsCtrl(byte vk) { return vk == 0x11 || vk == 0xA2 || vk == 0xA3; }

        // Ctrl+Esc opens Start just as the Windows key does, and from there
        // a script could type "cmd" and press Enter; Ctrl+Shift+Esc opens
        // Task Manager. On a Roblox window the check that it is still in
        // front stops what would come after, but on the screen nothing does,
        // so there Esc is refused while Ctrl is down. Ctrl counts as down
        // when the script holds it (keydown), when it was let go of only to
        // give the keyboard up (Take presses it again before these steps),
        // and when an earlier step of these same steps pressed it. Typing
        // the Esc character counts too: VkKeyScan gives the Esc key for it,
        // so typing sends that key.
        void NoCtrlEsc(IList<MacroStep> steps)
        {
            if (!target.Screen) return;
            bool ctrl = false;
            foreach (byte vk in held) if (IsCtrl(vk)) ctrl = true;
            foreach (byte vk in letGo) if (IsCtrl(vk)) ctrl = true;
            foreach (MacroStep s in steps)
            {
                if (s == null) continue;
                if (s.Kind == MacroStepKind.KeyDown && IsCtrl(s.Vk)) ctrl = true;
                bool esc = (s.Kind == MacroStepKind.Key || s.Kind == MacroStepKind.KeyDown) && s.Vk == VK_ESC;
                if (s.Kind == MacroStepKind.Type && s.Text != null && s.Text.IndexOf('\u001b') >= 0) esc = true;
                if (esc && ctrl) throw new ScriptFault(ScriptKeys.CTRL_ESC_REFUSED);
            }
        }

        void Send(MacroStep s)
        {
            IMacroWorld w = World();
            List<InputAction> plan = new List<InputAction>();
            MacroPlan.Add(plan, new List<byte>(), s, w.Client);
            foreach (InputAction a in plan)
            {
                if (a.Kind == InputActionKind.Sleep) { if (a.Ms > 0) w.Sleep(a.Ms); continue; }
                if (!w.InFront()) throw new ScriptStopped(MacroPlayer.STOPPED);
                w.Send(a);
            }
            lastSentMs = desk.NowMs;
        }

        public void Wait(int ms)
        {
            CheckStop();
            if (ms <= 0) return;
            if (keyboard && ms >= LONG_WAIT_MS && held.Count == 0) GiveUp();
            Pause(ms);
        }

        // A wait, a slice at a time, so a stop is felt at once. While it has
        // the keyboard it makes way for the nudge and stops if another window
        // comes to the front. Keys let go of to make way are pressed again
        // as soon as the one let in is done - before the wait is up, so a
        // walk carries on.
        void Pause(int ms)
        {
            long until = desk.NowMs + ms;
            while (true)
            {
                CheckStop();
                if (keyboard)
                {
                    if (desk.GateWanted) GiveUp();
                    else if (!target.Screen && !World().InFront()) throw new ScriptStopped(MacroPlayer.STOPPED);
                }
                else if (letGo.Count > 0 && !desk.AnyoneWaiting) Take();
                long left = until - desk.NowMs;
                if (left <= 0) break;
                desk.Nap((int)Math.Min(left, SLICE_MS));
            }
            if (!keyboard && letGo.Count > 0) Take();
        }

        // Looks for a picture until it is there (or gone), or the time is up.
        // Needs no keyboard: a picture of the window is taken without it in
        // front. Problem pictures are a fault.
        public PictureLook WaitFor(string file, string mask, int timeoutMs, bool wantSeen)
        {
            CheckStop();
            MacroStep s = MacroStep.WaitPicture(file, mask, timeoutMs, !wantSeen);
            if (keyboard && timeoutMs >= LONG_WAIT_MS && held.Count == 0) GiveUp();
            long began = desk.NowMs;
            while (true)
            {
                CheckStop();
                PictureLook look = World().Look(s);
                if (look.Problem != null) throw new ScriptFault(look.Problem);
                if (look.Found == wantSeen || desk.NowMs - began >= timeoutMs) return look;
                Pause(MacroStep.LOOK_EVERY_MS);
            }
        }

        // The words on it until they say `words`, or the time is up. Reading
        // is slow, so it is done twice a second at most.
        public const int READ_EVERY_MS = 500;

        public bool See(string words, int timeoutMs)
        {
            CheckStop();
            if (keyboard && timeoutMs >= LONG_WAIT_MS && held.Count == 0) GiveUp();
            long began = desk.NowMs;
            while (true)
            {
                CheckStop();
                string text = desk.Read(target) ?? "";
                if (Contains(text, words)) return true;
                if (desk.NowMs - began >= timeoutMs) return false;
                Pause(READ_EVERY_MS);
            }
        }

        // Words are matched without caring about case or how lines and
        // spaces fall: the recogniser breaks lines where the game does.
        public static bool Contains(string text, string words)
        {
            if (string.IsNullOrEmpty(words)) return true;
            return Squash(text).IndexOf(Squash(words), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string Squash(string s)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char c in s ?? "")
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public string Read()
        {
            CheckStop();
            return desk.Read(target) ?? "";
        }

        public Color ColorAt(double fx, double fy)
        {
            CheckStop();
            World();        // a window that has gone says so
            Color c;
            if (!desk.ColorAt(target, fx, fy, out c)) throw new ScriptFault("couldn't see " + (target.Screen ? "the screen" : "the Roblox window") + " just now");
            return c;
        }

        // Plays on another window, or the screen, from now. The keys held on
        // the one before are let go there.
        public void Use(ScriptTarget t)
        {
            CheckStop();
            if (t == null || t.Same(target)) { if (t != null) target = t; return; }
            GiveUp();
            letGo.Clear();
            target = t;
            world = null;
            World();
        }

        // Between Lua's own work, while it runs code that calls nothing: the
        // nudge is let in, and a keyboard nothing is being pressed on is given
        // up. Cheap: it is asked every thousand Lua instructions.
        public void Between()
        {
            if (!keyboard) return;
            if (desk.GateWanted)
            {
                if (held.Count > 0) HandOver();
                else GiveUp();
                return;
            }
            if (held.Count == 0 && desk.NowMs - lastSentMs >= LONG_WAIT_MS) GiveUp();
        }

        // However the run ends: nothing left held down, the keyboard given up.
        public void Finish()
        {
            try { if (keyboard) GiveUp(); }
            catch { }
            letGo.Clear();
            held.Clear();
            keyboard = false;
        }

        // ---------- places ----------

        // A point on the screen as % across and down of what it plays on.
        public static double Across(Rectangle area, int x)
        {
            double f = (x - area.Left) * 100.0 / Math.Max(1, area.Width - 1);
            return f < 0 ? 0 : f > 100 ? 100 : f;
        }

        public static double Down(Rectangle area, int y)
        {
            double f = (y - area.Top) * 100.0 / Math.Max(1, area.Height - 1);
            return f < 0 ? 0 : f > 100 ? 100 : f;
        }
    }

    // A script's run, in one line for the activity list.
    static class ScriptSummary
    {
        // "12s", "4m 5s", "1h 12m".
        public static string Duration(long ms)
        {
            long s = Math.Max(0, ms) / 1000;
            if (s < 60) return s + "s";
            long m = s / 60;
            if (m < 60) return m + "m" + (s % 60 > 0 ? " " + (s % 60) + "s" : "");
            return (m / 60) + "h" + (m % 60 > 0 ? " " + (m % 60) + "m" : "");
        }

        public static string Number(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // "Farm loop ran 1h 12m: sold 34, collected 210." - the counters in
        // the order they were first counted. problem: why it stopped, when it
        // did not end by itself or by being stopped on purpose.
        public static string Line(string name, long ms, IList<KeyValuePair<string, double>> counters, string problem)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(name).Append(" ran ").Append(Duration(ms));
            if (counters != null && counters.Count > 0)
            {
                sb.Append(": ");
                for (int i = 0; i < counters.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(counters[i].Key).Append(' ').Append(Number(counters[i].Value));
                }
            }
            sb.Append('.');
            if (!string.IsNullOrEmpty(problem))
            {
                string p = problem.TrimEnd('.');
                // "stopped - another window came to the front" says it already.
                if (p.StartsWith("stopped", StringComparison.Ordinal)) sb.Append(" S").Append(p.Substring(1)).Append('.');
                else sb.Append(" It stopped: ").Append(p).Append('.');
            }
            return sb.ToString();
        }
    }

    // A script's key names, as printed: "e", "space", "shift", "f1", "left".
    // Any case. A key with no name of its own is "key 160".
    static class ScriptKeys
    {
        static Dictionary<string, byte> byName;
        static Dictionary<byte, string> names;

        static void Build()
        {
            if (byName != null) return;
            Dictionary<string, byte> k = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            Dictionary<byte, string> n = new Dictionary<byte, string>();
            // The name each key is written with, first; the others after only
            // read.
            string[] main =
            {
                "space", "32", "enter", "13", "tab", "9", "backspace", "8", "esc", "27",
                "shift", "16", "ctrl", "17", "alt", "18",
                "left", "37", "up", "38", "right", "39", "down", "40",
                "delete", "46", "insert", "45", "home", "36", "end", "35", "pageup", "33", "pagedown", "34",
                "capslock", "20",
                "lshift", "160", "rshift", "161", "lctrl", "162", "rctrl", "163", "lalt", "164", "ralt", "165",
                ";", "186", "=", "187", ",", "188", "-", "189", ".", "190", "/", "191", "`", "192",
                "[", "219", "\\", "220", "]", "221", "'", "222"
            };
            for (int i = 0; i < main.Length; i += 2)
            {
                byte vk = byte.Parse(main[i + 1], CultureInfo.InvariantCulture);
                k[main[i]] = vk;
                if (!n.ContainsKey(vk)) n[vk] = main[i];
            }
            for (int vk = 0x41; vk <= 0x5A; vk++) { string s = ((char)(vk + 32)).ToString(); k[s] = (byte)vk; n[(byte)vk] = s; }
            for (int vk = 0x30; vk <= 0x39; vk++) { string s = ((char)vk).ToString(); k[s] = (byte)vk; n[(byte)vk] = s; }
            for (int f = 1; f <= 24; f++) { string s = "f" + f; k[s] = (byte)(0x6F + f); n[(byte)(0x6F + f)] = s; }
            for (int d = 0; d <= 9; d++) { string s = "numpad" + d; k[s] = (byte)(0x60 + d); n[(byte)(0x60 + d)] = s; }
            string[] also =
            {
                "spacebar", "32", "return", "13", "escape", "27", "control", "17", "del", "46", "ins", "45",
                "left arrow", "37", "up arrow", "38", "right arrow", "39", "down arrow", "40",
                "page up", "33", "page down", "34", "caps lock", "20",
                // Read only so that a script naming them is told it can't
                // press them, rather than that there is no such key.
                "win", "91", "lwin", "91", "rwin", "92", "windows", "91", "menu", "93", "apps", "93"
            };
            for (int i = 0; i < also.Length; i += 2)
                if (!k.ContainsKey(also[i])) k[also[i]] = byte.Parse(also[i + 1], CultureInfo.InvariantCulture);
            names = n;
            byName = k;
        }

        // The key a name means, or 0 for none.
        public static byte Parse(string name)
        {
            Build();
            if (string.IsNullOrEmpty(name)) return 0;
            string t = name.Trim();
            byte vk;
            if (byName.TryGetValue(t, out vk)) return vk;
            if (t.StartsWith("key ", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(t.Substring(4).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 254)
                    return (byte)n;
            }
            return 0;
        }

        // What a script writes for a key, so that Parse gives it back.
        public static string Name(byte vk)
        {
            Build();
            string s;
            if (names.TryGetValue(vk, out s)) return s;
            return "key " + vk.ToString(CultureInfo.InvariantCulture);
        }

        // Windows numbers the mouse's buttons among the keys; sent as a key
        // they click nothing.
        public static bool IsMouse(byte vk)
        {
            return vk == 1 || vk == 2 || vk == 4 || vk == 5 || vk == 6;
        }

        // The left and right Windows keys and the menu key. Win+R opens Run
        // and Win+X a menu with a command prompt in it - a script shared by
        // someone else could type a command there and press Enter. No game
        // needs them, so no script may press them, by name or as "key 91".
        //
        // The same goes for Sleep (0x5F), which puts the PC to sleep, and the
        // keyboard's browser, media and launch keys (0xA6 to 0xB7): the
        // launch ones open the mail app, Calculator or File Explorer.
        public static bool IsSystem(byte vk)
        {
            return vk == 0x5B || vk == 0x5C || vk == 0x5D || vk == 0x5F || (vk >= 0xA6 && vk <= 0xB7);
        }

        public const string SYSTEM_REFUSED = "scripts can't press the Windows keys, the menu key, Sleep, or the browser, media and launch keys";
        public const string CTRL_ESC_REFUSED = "scripts playing on the screen can't press Esc while Ctrl is held - Ctrl+Esc opens the Start menu";
    }

    // Which picture file a script means by a name, and its painted-out copy.
    static class ScriptPictures
    {
        // By what it is called or its file's name, ".png" or not. The mask is
        // the one a watcher or macro uses with it, or else the newest
        // "<picture>-mask-....png" beside it - the name the picture tool
        // gives one.
        public static bool Resolve(PictureShelf shelf, string folder, string name, out string file, out string mask)
        {
            file = mask = null;
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return false;
            string n = name.Trim();
            if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (shelf == null || !shelf.Find(n, out file, out mask)) { file = mask = null; return false; }
            if (mask == null) mask = MaskBeside(folder, file);
            return true;
        }

        public static string MaskBeside(string folder, string file)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file) || !Directory.Exists(folder)) return null;
                string[] masks = Directory.GetFiles(folder, Path.GetFileNameWithoutExtension(file) + "-mask-*.png");
                if (masks.Length == 0) return null;
                Array.Sort(masks, StringComparer.OrdinalIgnoreCase);
                return Path.GetFileName(masks[masks.Length - 1]);
            }
            catch { return null; }
        }
    }

    // The real PC: the macros' own worlds on a Roblox window or the screen,
    // the keyboard through FocusGate, pictures from the templates folder.
    // Begin and End and everything else on the script's own thread.
    class LiveDesk : IScriptDesk
    {
        readonly Func<WatchedClient[]> clients;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        IntPtr dpiWas;
        bool fine;
        PictureShelf shelf;

        // The world last asked for, kept with the pictures it has loaded.
        ScriptTarget worldFor;
        IntPtr worldHwnd;
        IMacroWorld worldNow;
        Func<bool> worldStop;

        // shelf: every picture the app knows, made on the window's thread as
        // the script starts - the macros and watchers it reads are the
        // window's to change, and are not walked from the script's thread.
        public LiveDesk(Func<WatchedClient[]> clients, PictureShelf shelf)
        {
            this.clients = clients;
            this.shelf = shelf;
        }

        public void Begin()
        {
            // The whole run in real pixels, so the window's place, the clicks
            // and the pointer all agree; a look at a picture steps out of
            // them and back, as a macro's does.
            dpiWas = MacroPlayer.RealPixels();
        }

        public void End()
        {
            MacroPlayer.Restore(dpiWas);
            dpiWas = IntPtr.Zero;
        }

        public WatchedClient[] Clients()
        {
            WatchedClient[] c = clients == null ? null : clients();
            return c ?? new WatchedClient[0];
        }

        public IMacroWorld World(ScriptTarget t, Func<bool> stop)
        {
            if (t == null) return null;
            if (t.Screen)
            {
                if (worldNow == null || worldFor == null || !worldFor.Screen || worldStop != stop)
                {
                    PlayOptions o = new PlayOptions();
                    o.Stop = stop;
                    ScreenWorld s = new ScreenWorld(o, MacroStop.Epoch, Rectangle.Empty);
                    worldNow = s;
                    worldFor = t;
                    worldStop = stop;
                    worldHwnd = IntPtr.Zero;
                }
                // Read each time: a monitor plugged in moves where the % land.
                ((ScreenWorld)worldNow).Begin(dpiWas, SystemInformation.VirtualScreen);
                return worldNow;
            }

            IntPtr hwnd = worldFor != null && worldFor.Same(t) && StillTheWindow(worldHwnd, t.Pid) ? worldHwnd : WindowCapture.GameWindow(t.Pid);
            if (hwnd == IntPtr.Zero) return null;
            if (worldNow == null || hwnd != worldHwnd || worldStop != stop)
            {
                PlayOptions o = new PlayOptions();
                o.Stop = stop;
                worldNow = MacroPlayer.WorldOn(hwnd, o, dpiWas, MacroStop.Epoch);
                worldHwnd = hwnd;
                worldFor = t;
                worldStop = stop;
            }
            return worldNow;
        }

        public void SendPlain(InputAction a) { MacroPlayer.Send(a); }

        static bool StillTheWindow(IntPtr hwnd, int pid)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint owner;
            Native.GetWindowThreadProcessId(hwnd, out owner);
            Native.RECT r;
            return owner == (uint)pid && Native.GetWindowRect(hwnd, out r);
        }

        public bool TakeKeyboard(Func<bool> stop, out string problem)
        {
            problem = null;
            if (!FocusGate.TryEnter(MacroPlayer.GATE_WAIT_MS, false, stop))
            {
                if (stop == null || !stop()) problem = "something else was using the keyboard for too long";
                return false;
            }
            if (stop != null && stop()) { FocusGate.Exit(); return false; }
            // A key you are holding - Ctrl from the key that started it - would
            // turn every key it sends into a shortcut.
            if (!MacroPlayer.WaitForKeysUp(delegate(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; },
                                           delegate(int ms) { Thread.Sleep(ms); }, 2000))
            {
                FocusGate.Exit();
                problem = "Ctrl, Alt or Shift was held down - let go of it and try again";
                return false;
            }
            // Windows naps in steps of about 15ms unless asked for finer;
            // asked only while it holds the keyboard - it costs a little power.
            try { fine = Native.timeBeginPeriod(1) == 0; } catch { fine = false; }
            return true;
        }

        public void GiveKeyboard()
        {
            if (fine) try { Native.timeEndPeriod(1); } catch { }
            fine = false;
            FocusGate.Exit();
        }

        public bool BringToFront(ScriptTarget t)
        {
            if (t == null || t.Screen) return true;
            IntPtr hwnd = WindowCapture.GameWindow(t.Pid);
            if (hwnd == IntPtr.Zero) return false;
            if (Native.IsIconic(hwnd)) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(180); }
            if (Native.GetForegroundWindow() != hwnd)
            {
                InputSender.FocusWindow(hwnd);
                Thread.Sleep(100);
            }
            return Native.GetForegroundWindow() == hwnd;
        }

        public bool GateWanted { get { return FocusGate.NudgeWanted || FocusGate.MacroGivingUp; } }
        public bool AnyoneWaiting { get { return FocusGate.Wanted; } }
        public long NowMs { get { return clock.ElapsedMilliseconds; } }
        public void Nap(int ms) { if (ms > 0) Thread.Sleep(ms); }

        public bool FindPicture(string name, out string file, out string mask)
        {
            if (ScriptPictures.Resolve(shelf, WatchStore.TemplatesDir, name, out file, out mask)) return true;
            // Cut while it runs: looked for again in the folder itself.
            return ScriptPictures.Resolve(PictureShelf.For(null, null, WatchStore.TemplatesDir), WatchStore.TemplatesDir,
                                          name, out file, out mask);
        }

        public string Read(ScriptTarget t)
        {
            // Said, rather than see() quietly never seeing anything.
            if (!ScreenText.Available)
                throw new ScriptFault("Windows' text reader isn't installed, so read() and see() can't work - Watchers offers to install it");
            Pixels shot = Shot(t);
            return shot == null ? "" : ScreenText.Read(shot);
        }

        // A picture taken the way watchers take them - outside real pixels.
        Pixels Shot(ScriptTarget t)
        {
            MacroPlayer.Restore(dpiWas);
            try
            {
                if (t.Screen) return ScreenWorld.GrabScreen();
                IntPtr hwnd = WindowCapture.GameWindow(t.Pid);
                return hwnd == IntPtr.Zero ? null : WindowCapture.Grab(hwnd);
            }
            finally { MacroPlayer.RealPixels(); }
        }

        public bool ColorAt(ScriptTarget t, double fx, double fy, out Color c)
        {
            c = Color.Empty;
            try
            {
                if (t.Screen)
                {
                    Point p = MacroPlan.PointIn(SystemInformation.VirtualScreen, fx, fy);
                    using (Bitmap b = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                            g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
                        c = b.GetPixel(0, 0);
                    }
                    return true;
                }

                IntPtr hwnd = WindowCapture.GameWindow(t.Pid);
                if (hwnd == IntPtr.Zero) return false;
                // A picture of the whole window - frame too - taken the way
                // watchers take them, so the client area is found inside it
                // in the same units, and scaled if the picture is not the
                // window's size.
                MacroPlayer.Restore(dpiWas);
                try
                {
                    Native.RECT win, client;
                    if (!Native.GetWindowRect(hwnd, out win) || !Native.GetClientRect(hwnd, out client)) return false;
                    Native.POINT origin = new Native.POINT();
                    Native.ClientToScreen(hwnd, ref origin);
                    Pixels shot = WindowCapture.Grab(hwnd);
                    if (shot == null || shot.IsEmpty) return false;
                    int ww = Math.Max(1, win.Right - win.Left), wh = Math.Max(1, win.Bottom - win.Top);
                    Rectangle area = new Rectangle(origin.X - win.Left, origin.Y - win.Top,
                                                   client.Right - client.Left, client.Bottom - client.Top);
                    Point inside = MacroPlan.PointIn(area, fx, fy);
                    int x = (int)Math.Round(inside.X * (double)shot.Width / ww);
                    int y = (int)Math.Round(inside.Y * (double)shot.Height / wh);
                    x = Math.Max(0, Math.Min(shot.Width - 1, x));
                    y = Math.Max(0, Math.Min(shot.Height - 1, y));
                    c = Color.FromArgb(shot.At(x, y));
                    return true;
                }
                finally { MacroPlayer.RealPixels(); }
            }
            catch { return false; }
        }
    }
}
