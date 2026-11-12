using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A script running now, as the Scripts window shows it.
    class ScriptRunInfo
    {
        public string Name;
        public string Where;        // "Client 2 - VladDerKing", "the screen"
        public int Pid;             // 0 on the screen
        public string Tag;          // who started it: null from the Scripts window, "rule:..." from a rule, "key:..." from its key
        public int Line;            // the line it is on; 0 before its first function
        public string Status;       // what status() last said; null for nothing
        public DateTime Started;
    }

    // What the Scripts window, and rules, ask of the main window about Lua
    // scripts. The main window is the only one; reached as IShell.Scripts and
    // WatchKit.Scripts (null where there is no main window, as in tests).
    //
    // Every event is raised on the main window's thread. Output and the line
    // are gathered and handed over a batch at a time, so a script printing
    // in a loop can't flood the window.
    interface IScriptHost
    {
        // The scripts on disk: list, create, load, save, settings. Rename and
        // remove through RenameScript and RemoveScript below, which refuse
        // while it runs.
        ScriptStore Store { get; }

        // Plays on its own setting (Plays on). Null when it started;
        // otherwise why not, in a line for the window.
        string StartScript(string name);

        // From a rule: on the Roblox window with this process id, or - pid 0 -
        // where its own setting says. tag: who started it ("rule:" and the
        // rule's name), so IsPlaying and StopTag find it. Null when it
        // started; otherwise why not. The same script already running on the
        // same window is not started twice.
        string StartScript(string name, int pid, string tag);

        bool StopScript(string name);               // every run of it; true when one was running
        bool IsScriptRunning(string name);
        List<ScriptRunInfo> ScriptsRunning();       // a copy, as things are now

        // The one key check, for a script's own key: null when vk is free for
        // the script `name` (its present key is no clash).
        string ScriptKeyProblem(byte vk, string name);

        // After its settings were saved - a key may have come or gone.
        void ScriptsSaved();

        // A script imported from someone else's file is not trusted until
        // you have read it and said so: it doesn't start, and can never play
        // on the whole screen. False only for such a script; true for one
        // made here, and for a name that isn't a script.
        bool IsScriptTrusted(string name);
        void TrustScript(string name);

        // Null when done; otherwise why not. Refused while it runs.
        string RenameScript(string from, string to);
        string RemoveScript(string name);

        // A macro as a script, one line a step; see ScriptConvert.
        string MacroAsLua(Macro m);

        // A .rkscript file: this script and every picture it names. Null
        // when written; otherwise why not.
        string ExportScript(string name, string zipPath);
        ScriptImport ImportScript(string zipPath);

        // MoonSharp's copyright notice and disclaimer, to show in the app.
        string Notices { get; }

        event Action<string, string> ScriptOutput;          // name, one printed line
        event Action<string, int> ScriptLine;               // name, the line it is on now
        event Action<string, string> ScriptStatus;          // name, what status() said (null: hidden)
        event Action<string, ScriptResult> ScriptEnded;     // name, how the run went (r.Summary is the line)
    }

    // The third-party notices packed into the exe.
    static class Notices
    {
        public static string Text()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RobloxKeeper.THIRD-PARTY-NOTICES.md"))
                {
                    if (s == null) return "";
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return ""; }
        }
    }

    // The small dark label status() shows over the game - the extras' pill,
    // on a see-through window of its own that clicks fall through. Only over
    // its window while that window is in front (always, for the screen), at
    // the top in the middle.
    //
    // Its windows live on a thread of their own (a GlassHost) that measures
    // in real pixels, as the extras' do; everything here is posted to it and
    // nothing waits on it.
    class ScriptPill
    {
        GlassHost host;
        readonly object gate = new object();

        class Pill
        {
            public GlassWindow Glass;
            public string Text;
            public IntPtr Window;       // the game window, or zero for the screen
        }

        // On the overlay thread only.
        readonly Dictionary<object, Pill> pills = new Dictionary<object, Pill>();

        GlassHost Host()
        {
            lock (gate)
            {
                if (host == null)
                {
                    host = new GlassHost();
                    host.Start();
                }
                return host;
            }
        }

        // text null hides it. window: the game window it is over, or zero for
        // the screen.
        public void Set(object key, string text, IntPtr window)
        {
            if (text == null && host == null) return;
            Host().Post(delegate
            {
                Pill p;
                pills.TryGetValue(key, out p);
                if (text == null)
                {
                    if (p != null) { p.Glass.Dispose(); pills.Remove(key); }
                    return;
                }
                if (p == null)
                {
                    p = new Pill();
                    p.Glass = new GlassWindow();
                    pills[key] = p;
                }
                p.Window = window;
                if (p.Text != text)
                {
                    p.Text = text;
                    using (Bitmap b = GlassDraw.Pill(text.Length > 80 ? text.Substring(0, 77) + "..." : text))
                        p.Glass.SetPicture(b, 100);
                }
                Place(p);
            });
        }

        // Once a second: over its window if that is in front and where it is now.
        public void Follow()
        {
            GlassHost h = host;
            if (h == null) return;
            h.Post(delegate { foreach (Pill p in pills.Values) Place(p); });
        }

        static void Place(Pill p)
        {
            Rectangle area;
            if (p.Window == IntPtr.Zero) area = Screen.PrimaryScreen.Bounds;
            else
            {
                Xn.RECT r;
                if (Xn.GetForegroundWindow() != p.Window || !Xn.GetClientRect(p.Window, out r)) { p.Glass.Hide(); return; }
                Xn.POINT o = new Xn.POINT();
                Xn.ClientToScreen(p.Window, ref o);
                area = new Rectangle(o.X, o.Y, r.R - r.L, r.B - r.T);
            }
            Size s = p.Glass.PictureSize;
            p.Glass.ShowAt(new Point(area.Left + (area.Width - s.Width) / 2, area.Top + GlassMath.MARGIN));
            p.Glass.Raise();
        }

        public void Stop()
        {
            GlassHost h;
            lock (gate) { h = host; host = null; }
            if (h == null) return;
            try
            {
                h.Ask<bool>(delegate
                {
                    foreach (Pill p in pills.Values) p.Glass.Dispose();
                    pills.Clear();
                    return true;
                }, false);
                h.Stop();
            }
            catch { }
        }
    }
}
