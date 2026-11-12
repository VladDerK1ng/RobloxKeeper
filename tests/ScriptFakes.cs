using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // A pretend PC for scripts: two Roblox windows and the screen, a
    // keyboard that is only counted, pictures by name, and a clock that only
    // moves when something waits. Nothing real is pressed, clicked or looked
    // at. Every Send is written down with which window it went to.
    class FakeDesk : IScriptDesk
    {
        public long Now;
        public bool RealTime;                       // naps really sleep, and the clock is real
        readonly System.Diagnostics.Stopwatch real = System.Diagnostics.Stopwatch.StartNew();

        public readonly List<string> Log = new List<string>();        // take, give, front <pid>
        public readonly List<KeyValuePair<string, InputAction>> Sent = new List<KeyValuePair<string, InputAction>>();
        public WatchedClient[] Windows;
        public Rectangle WindowArea = new Rectangle(100, 50, 801, 601);
        public Rectangle ScreenArea = new Rectangle(-1920, 0, 3840, 1080);
        public Func<int, bool> Front = delegate { return true; };          // by pid
        public Func<bool> Nudge = delegate { return false; };
        public Func<bool> Waiting = null;                                  // null: whenever Nudge says
        public bool Refuse;                                                // the keyboard is kept from it
        public bool WindowGone;
        public bool Keyboard;
        public int Takes, Gives;

        // Pictures: a name scripts use -> its file; seen by how many looks.
        public readonly Dictionary<string, string> Pictures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Func<string, int, bool> Seen = delegate { return false; };
        public Point SeenAt = new Point(500, 350);
        public int Looks;
        public string Text = "";
        public Color Colour = Color.FromArgb(10, 20, 30);
        public Action Napped;                       // after every nap: a test stops a run here, at a known moment

        public FakeDesk()
        {
            WatchedClient a = new WatchedClient();
            a.Pid = 11; a.Label = "Client 1"; a.AccountName = "Vlad";
            WatchedClient b = new WatchedClient();
            b.Pid = 22; b.Label = "Client 2";
            Windows = new WatchedClient[] { a, b };
        }

        public void Begin() { Log.Add("begin"); }
        public void End() { Log.Add("end"); }
        public WatchedClient[] Clients() { return Windows; }

        public IMacroWorld World(ScriptTarget t, Func<bool> stop)
        {
            if (!t.Screen && WindowGone) return null;
            return new FakeWorld(this, t, stop);
        }

        // Sent with no window to send it to, as "plain".
        public void SendPlain(InputAction a)
        {
            if (!Keyboard) throw new Exception("sent " + Describe(a) + " without the keyboard");
            Sent.Add(new KeyValuePair<string, InputAction>("plain", a));
        }

        public bool TakeKeyboard(Func<bool> stop, out string problem)
        {
            problem = null;
            if (Refuse) { problem = "something else was using the keyboard for too long"; return false; }
            if (Keyboard) throw new Exception("took the keyboard twice");
            Keyboard = true;
            Takes++;
            Log.Add("take");
            return true;
        }

        public void GiveKeyboard()
        {
            if (!Keyboard) throw new Exception("gave up a keyboard it didn't have");
            Keyboard = false;
            Gives++;
            Log.Add("give");
        }

        public bool BringToFront(ScriptTarget t) { Log.Add("front " + t.Pid); return true; }
        public bool GateWanted { get { return Nudge(); } }
        public bool AnyoneWaiting { get { return Waiting != null ? Waiting() : Nudge(); } }
        public long NowMs { get { return RealTime ? real.ElapsedMilliseconds : Now; } }

        public void Nap(int ms)
        {
            if (RealTime) Thread.Sleep(ms);
            else Now += ms;
            if (Napped != null) Napped();
        }

        public bool FindPicture(string name, out string file, out string mask)
        {
            mask = null;
            return Pictures.TryGetValue(name, out file);
        }

        public string Read(ScriptTarget t) { Log.Add("read"); return Text; }

        public bool ColorAt(ScriptTarget t, double fx, double fy, out Color c)
        {
            c = Colour;
            return true;
        }

        // What was sent, leaving out the pauses, as "down E", "up E",
        // "move 500,350", "press left", "release left", "wheel 120",
        // "char h", "drag 10,10".
        public List<string> Said()
        {
            List<string> s = new List<string>();
            foreach (KeyValuePair<string, InputAction> p in Sent) s.Add(Describe(p.Value));
            return s;
        }

        public static string Describe(InputAction a)
        {
            switch (a.Kind)
            {
                case InputActionKind.KeyDown: return "down " + MacroKeys.Name(a.Vk);
                case InputActionKind.KeyUp: return "up " + MacroKeys.Name(a.Vk);
                case InputActionKind.MoveTo: return "move " + a.X + "," + a.Y;
                case InputActionKind.ButtonDown: return "press " + (a.Right ? "right" : "left");
                case InputActionKind.ButtonUp: return "release " + (a.Right ? "right" : "left");
                case InputActionKind.Wheel: return "wheel " + a.Delta;
                case InputActionKind.Char: return "char " + a.Char;
                case InputActionKind.DragTo: return "drag " + a.X + "," + a.Y;
                default: return a.Kind.ToString();
            }
        }

        public string SaidText() { return string.Join(" | ", Said().ToArray()); }

        class FakeWorld : IMacroWorld
        {
            readonly FakeDesk d;
            readonly ScriptTarget t;
            readonly Func<bool> stop;

            public FakeWorld(FakeDesk d, ScriptTarget t, Func<bool> stop)
            {
                this.d = d; this.t = t; this.stop = stop;
            }

            public Rectangle Client { get { return t.Screen ? d.ScreenArea : d.WindowArea; } }
            public bool InFront() { return t.Screen || d.Front(t.Pid); }
            public void Send(InputAction a)
            {
                if (!d.Keyboard) throw new Exception("sent " + Describe(a) + " without the keyboard");
                d.Sent.Add(new KeyValuePair<string, InputAction>(t.Screen ? "screen" : t.Pid.ToString(), a));
            }
            public void Sleep(int ms) { d.Nap(ms); }
            public long NowMs { get { return d.NowMs; } }
            public bool StopAsked { get { return stop != null && stop(); } }
            public bool GateWanted { get { return d.Nudge(); } }
            public bool OthersWaiting { get { return false; } }

            public PictureLook Look(MacroStep s)
            {
                PictureLook l = new PictureLook();
                l.Found = d.Seen(s.Picture, d.Looks);
                l.At = d.SeenAt;
                d.Looks++;
                return l;
            }
        }
    }

    // Writes down what a script said.
    class FakeOutlet : IScriptOutlet
    {
        public readonly List<string> Printed = new List<string>();
        public readonly List<int> Lines = new List<int>();
        public readonly List<string> Statuses = new List<string>();
        public readonly List<string> Said = new List<string>();
        public readonly List<string> Notified = new List<string>();
        public readonly List<string> Discorded = new List<string>();

        public void Print(string text) { lock (Printed) Printed.Add(text); }
        public void Line(int line) { Lines.Add(line); }
        public void Status(string text) { Statuses.Add(text); }
        public void Say(string text) { Said.Add(text); }
        public void Notify(string text) { Notified.Add(text); }
        public void Discord(string text) { Discorded.Add(text); }

        public string All { get { lock (Printed) return string.Join("\n", Printed.ToArray()); } }
    }

    static class ScriptRig
    {
        public static ScriptTarget Window(FakeDesk d) { return ScriptTarget.Of(d.Windows[0]); }

        public static ScriptResult Run(string code, FakeDesk d, FakeOutlet o)
        {
            return Run(code, d, o, Window(d), null);
        }

        public static ScriptResult Run(string code, FakeDesk d, FakeOutlet o, ScriptTarget t, ScriptStore store)
        {
            return new LuaRun("Test", code, d, t, o, store).Run();
        }

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-scripts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
