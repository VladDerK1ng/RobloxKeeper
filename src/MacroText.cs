using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // The pictures a macro written as text can name.
    //
    // A macro's text names a picture by what it is called - "Buy button" -
    // and the file is found here. The picture itself is not in the text: a
    // macro sent to someone else names pictures they have to cut themselves.
    class PictureShelf
    {
        class Entry
        {
            public string File, Mask;
        }

        readonly List<Entry> entries = new List<Entry>();

        // The boxes drawn for games to look in, by name; null when nobody
        // knows, and then any name is taken. Like pictures they are this
        // PC's: a macro from someone else names boxes that have to be drawn.
        public List<string> Boxes;

        public bool HasBox(string name)
        {
            if (Boxes == null) return true;
            foreach (string b in Boxes)
                if (string.Equals(b, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Earlier ones win where two are called the same, so what the macro
        // being edited already uses is added first.
        public void Add(string file, string mask)
        {
            if (string.IsNullOrEmpty(file)) return;
            foreach (Entry e in entries)
            {
                if (!string.Equals(e.File, file, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Mask == null) e.Mask = mask;
                return;
            }
            Entry n = new Entry();
            n.File = file; n.Mask = mask;
            entries.Add(n);
        }

        public void AddSteps(IEnumerable<MacroStep> steps)
        {
            if (steps == null) return;
            foreach (MacroStep s in steps)
                if (s != null && s.UsesPicture) Add(s.Picture, s.Mask);
        }

        static string Stem(string file)
        {
            return Path.GetFileNameWithoutExtension(file);
        }

        // By the whole file name first - that is never two pictures - and
        // then by what it is called.
        public bool Find(string name, out string file, out string mask)
        {
            file = null; mask = null;
            if (string.IsNullOrEmpty(name)) return false;
            Entry hit = null;
            foreach (Entry e in entries)
                if (string.Equals(Stem(e.File), name, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
            if (hit == null)
                foreach (Entry e in entries)
                    if (string.Equals(MacroStep.PictureLabel(e.File), name, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
            if (hit == null) return false;
            file = hit.File; mask = hit.Mask;
            return true;
        }

        // What to write for a file so that reading it back finds the same
        // one: what it is called, unless another picture is called that too.
        public string NameOf(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            string label = MacroStep.PictureLabel(file), found, mask;
            if (Find(label, out found, out mask) && string.Equals(found, file, StringComparison.OrdinalIgnoreCase)) return label;
            return Stem(file);
        }

        public List<string> Names()
        {
            List<string> names = new List<string>();
            foreach (Entry e in entries)
            {
                string n = NameOf(e.File);
                if (!names.Contains(n)) names.Add(n);
            }
            return names;
        }

        // Everything the app knows of: the macro being edited, the other
        // macros, the picture watchers, and whatever else has been cut.
        // folder is the templates folder, or null to leave the disk alone.
        public static PictureShelf For(WatchStore store, IEnumerable<MacroStep> editing, string folder)
        {
            PictureShelf shelf = new PictureShelf();
            shelf.AddSteps(editing);
            if (store != null)
            {
                shelf.Boxes = new List<string>();
                foreach (WatchRegion r in store.Regions)
                    if (r != null && !string.IsNullOrEmpty(r.Name) && !shelf.HasBox(r.Name)) shelf.Boxes.Add(r.Name);
                // A box the macro already looks in is kept, drawn or not.
                if (editing != null)
                    foreach (MacroStep s in editing)
                        if (s != null && !string.IsNullOrEmpty(s.Box) && !shelf.HasBox(s.Box)) shelf.Boxes.Add(s.Box);
                foreach (Macro m in store.Macros) if (m != null) shelf.AddSteps(m.Steps);
                foreach (Watcher w in store.Watchers)
                    if (w != null && w.Kind == WatchKind.ImageFound) shelf.Add(w.TemplateFile, w.MaskFile);
            }
            try
            {
                if (folder != null && Directory.Exists(folder))
                {
                    // A file's name ends in when it was cut, so backwards is
                    // newest first: of two called the same, the one cut again
                    // is the one meant.
                    string[] paths = Directory.GetFiles(folder, "*.png");
                    Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                    Array.Reverse(paths);
                    foreach (string path in paths)
                    {
                        string name = Path.GetFileName(path);
                        // A painted-out copy belongs to its picture; it is not one to click.
                        if (name.IndexOf("-mask-", StringComparison.OrdinalIgnoreCase) < 0) shelf.Add(name, null);
                    }
                }
            }
            catch { }
            return shelf;
        }
    }

    // A macro as lines of text, and back.
    //
    //     repeat forever
    //         click picture "Buy button" wait 3
    //         press E
    //         wait 0.5
    //     end
    //
    // The text is another way to write the steps the editor's buttons and
    // its recording make: every line is one step of a kind they make, held
    // to the limits the editor's windows hold a step to, in the same words.
    // What the editor checks about a macro as a whole - how long it is, a
    // repeat with nothing in it - it still checks when the macro is saved.
    // It is not a programming language and it does not run inside Roblox -
    // it is played from outside, as keys and clicks, like every other macro.
    static class MacroText
    {
        public const string STARTS = "press, hold, release, wait, click, right click, move, drag, scroll, type, say, repeat or end";

        // ---------- writing ----------

        public static string Write(IList<MacroStep> steps, PictureShelf shelf)
        {
            if (shelf == null) shelf = new PictureShelf();
            StringBuilder sb = new StringBuilder();
            int depth = 0;
            foreach (MacroStep s in steps)
            {
                if (s.Kind == MacroStepKind.EndRepeat && depth > 0) depth--;
                sb.Append(' ', depth * 4).Append(Line(s, shelf)).Append("\r\n");
                if (s.Kind == MacroStepKind.Repeat) depth++;
            }
            return sb.ToString();
        }

        static string Line(MacroStep s, PictureShelf shelf)
        {
            string right = s.RightButton ? "right " : "";
            switch (s.Kind)
            {
                case MacroStepKind.Key:
                    return "press " + KeyName(s.Vk) + (s.HoldMs != 50 ? " for " + Sec(s.HoldMs) : "");
                case MacroStepKind.KeyDown: return "hold " + KeyName(s.Vk);
                case MacroStepKind.KeyUp: return "release " + KeyName(s.Vk);
                case MacroStepKind.Wait: return "wait " + Sec(s.Ms);
                case MacroStepKind.Click:
                    return right + "click " + (s.AtPointer ? "here" : Pct(s.X) + " " + Pct(s.Y));
                case MacroStepKind.Type:
                    return (s.InChat ? "say " : "type ") + Quote(s.Text ?? "");
                case MacroStepKind.ClickPicture:
                    return right + "click picture " + Quote(shelf.NameOf(s.Picture))
                         + (s.TimeoutMs > 0 ? " wait " + Sec(s.TimeoutMs) : "") + Looking(s)
                         + (s.OffX != 0 || s.OffY != 0 ? " offset " + Int(s.OffX) + " " + Int(s.OffY) : "")
                         + (s.StopIfMissing ? " or stop" : "");
                case MacroStepKind.WaitPicture:
                    return "wait for " + Quote(shelf.NameOf(s.Picture)) + (s.UntilGone ? " gone" : "")
                         + " up to " + Sec(s.TimeoutMs) + Looking(s) + (s.StopIfMissing ? " or stop" : "");
                case MacroStepKind.Move: return "move " + Pct(s.X) + " " + Pct(s.Y);
                case MacroStepKind.Drag:
                    return right + "drag " + Pct(s.X) + " " + Pct(s.Y) + " to " + Pct(s.X2) + " " + Pct(s.Y2) + " over " + Sec(s.Ms);
                case MacroStepKind.Scroll:
                    return "scroll " + (s.Notches >= 0 ? "up " : "down ") + Int(Math.Abs(s.Notches))
                         + (s.AtPointer ? " here" : " at " + Pct(s.X) + " " + Pct(s.Y));
                case MacroStepKind.Repeat:
                    switch (s.Mode)
                    {
                        case RepeatMode.Forever: return "repeat forever";
                        case RepeatMode.UntilSeen: return "repeat until " + Quote(shelf.NameOf(s.Picture)) + " shows" + Looking(s);
                        case RepeatMode.UntilGone: return "repeat until " + Quote(shelf.NameOf(s.Picture)) + " gone" + Looking(s);
                        default: return "repeat " + Int(s.Count);
                    }
                case MacroStepKind.EndRepeat: return "end";
                default: return "# a step from a newer RobloxKeeper";
            }
        }

        // How a picture is looked for, where it is not the usual way.
        static string Looking(MacroStep s)
        {
            return (Math.Abs(s.Tolerance - MacroStep.DEFAULT_TOLERANCE) >= 0.005 ? " alike " + Int((int)Math.Round(s.Tolerance * 100)) : "")
                 + (string.IsNullOrEmpty(s.Box) ? "" : " in " + Quote(s.Box));
        }

        static string Int(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string Sec(int ms) { return (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture); }
        static string Pct(double fraction) { return (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture); }

        // Only \" and \\ mean something else inside quotes. What is typed is
        // one line, as it is in the editor's box: a line break or a tab in it
        // would be sent to the game as Enter or Tab.
        static string Quote(string text)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (char.IsControl(c)) continue;
                if (c == '\\' || c == '"') sb.Append('\\');
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // ---------- keys ----------

        static Dictionary<string, byte> keys;

        static Dictionary<string, byte> Keys()
        {
            if (keys != null) return keys;
            Dictionary<string, byte> k = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Letters and digits first: a key Windows has no name for is
            // called by its number, and "7" has to mean the 7 key.
            for (int vk = 0x30; vk <= 0x5A; vk++)
                if (vk <= 0x39 || vk >= 0x41) k[MacroKeys.Name((byte)vk)] = (byte)vk;
            for (int vk = 1; vk < 255; vk++)
            {
                string name = MacroKeys.Name((byte)vk);
                int number;
                if (int.TryParse(name, out number) || k.ContainsKey(name)) continue;
                k[name] = (byte)vk;
            }
            string[] also =
            {
                "LShift", "160", "RShift", "161", "LCtrl", "162", "RCtrl", "163", "LAlt", "164", "RAlt", "165",
                "Spacebar", "32", "Escape", "27", "Return", "13", "Control", "17", "Del", "46",
                "Left", "37", "Up", "38", "Right", "39", "Down", "40",
                "[", "219", "\\", "220", "]", "221", "'", "222"
            };
            for (int i = 0; i < also.Length; i += 2)
                if (!k.ContainsKey(also[i])) k[also[i]] = byte.Parse(also[i + 1], CultureInfo.InvariantCulture);
            keys = k;
            return k;
        }

        // What reads back as the same key. A key with no name of its own is
        // "key 160".
        public static string KeyName(byte vk)
        {
            switch (vk)
            {
                // As they are printed, not as Windows calls them.
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            string name = MacroKeys.Name(vk);
            byte back;
            if (Keys().TryGetValue(name, out back) && back == vk) return name;
            switch (vk)
            {
                case 0xA0: return "LShift";
                case 0xA1: return "RShift";
                case 0xA2: return "LCtrl";
                case 0xA3: return "RCtrl";
                case 0xA4: return "LAlt";
                case 0xA5: return "RAlt";
                default: return "key " + vk.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ---------- reading ----------

        class Token
        {
            public string Text;
            public bool Quoted;
        }

        // The words of one line, a "quoted piece" as one word. Null and why
        // when a quote is never closed.
        static List<Token> Words(string line, out string problem)
        {
            problem = null;
            List<Token> words = new List<Token>();
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                Token t = new Token();
                StringBuilder sb = new StringBuilder();
                if (c == '"')
                {
                    t.Quoted = true;
                    i++;
                    bool closed = false;
                    while (i < line.Length)
                    {
                        c = line[i++];
                        if (c == '\\' && i < line.Length && (line[i] == '"' || line[i] == '\\')) sb.Append(line[i++]);
                        else if (c == '"') { closed = true; break; }
                        else if (char.IsControl(c)) { problem = "there is a tab inside the quotes. Use letters, numbers and spaces only"; return null; }
                        else sb.Append(c);
                    }
                    if (!closed) { problem = "a quote mark is opened but never closed"; return null; }
                }
                else
                {
                    while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '"') sb.Append(line[i++]);
                }
                t.Text = sb.ToString();
                words.Add(t);
            }
            return words;
        }

        // One line being read, a word at a time.
        class Reader
        {
            public readonly List<Token> Words;
            public int At;
            public string Problem;

            public Reader(List<Token> words) { Words = words; }

            public bool Done { get { return At >= Words.Count; } }

            // The next word is this one, not in quotes: step over it.
            public bool Take(string word)
            {
                if (Done || Words[At].Quoted || !string.Equals(Words[At].Text, word, StringComparison.OrdinalIgnoreCase)) return false;
                At++;
                return true;
            }

            public bool Fail(string why)
            {
                if (Problem == null) Problem = why;
                return false;
            }

            public bool Quoted(string what, out string text)
            {
                text = null;
                if (Done || !Words[At].Quoted) return Fail("put " + what + " in quotes, \"like this\"");
                text = Words[At++].Text;
                return true;
            }

            public bool Seconds(string what, bool nothingIsFine, out int ms)
            {
                ms = 0;
                if (Done || Words[At].Quoted) return Fail("write " + what + ", in seconds");
                string t = Words[At++].Text;
                double v;
                if (nothingIsFine && double.TryParse(t.TrimEnd('s', 'S').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v == 0)
                    return true;
                string why = MacroForm.ParseSeconds(t, out ms);
                return why == null || Fail("\"" + t + "\": " + why.TrimEnd('.'));
            }

            public bool Whole(string what, int low, int high, out int n)
            {
                n = 0;
                if (Done || Words[At].Quoted) return Fail("write " + what);
                string t = Words[At++].Text;
                if (!int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n))
                    return Fail("\"" + t + "\" isn't a whole number");
                if (n < low || n > high) return Fail(what + " goes from " + low + " to " + high);
                return true;
            }

            // A place on the window: so far across, so far down, each 0 to 100.
            public bool Place(out double x, out double y)
            {
                x = 0; y = 0;
                return Percent("how far across", out x) && Percent("how far down", out y);
            }

            bool Percent(string what, out double fraction)
            {
                fraction = 0;
                if (Done || Words[At].Quoted) return Fail("write " + what + " the window, from 0 to 100");
                string t = Words[At++].Text;
                double v;
                if (!double.TryParse(t.TrimEnd('%').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    return Fail("\"" + t + "\" isn't a number. A place is how far across and down the window, from 0 to 100");
                // "NaN" is a number to the parser and is neither under 0 nor over 100.
                if (double.IsNaN(v) || v < 0 || v > 100) return Fail("a place goes from 0 to 100, and \"" + t + "\" is outside that");
                fraction = v / 100;
                return true;
            }

            public bool Key(out byte vk)
            {
                vk = 0;
                if (Take("key"))
                {
                    int n;
                    if (!Whole("the key's number", 1, 254, out n)) return false;
                    vk = (byte)n;
                    return NotTheMouse(vk);
                }
                // A key's name may be two words - "Left arrow" - so it is
                // everything up to "for" or the end of the line.
                List<string> parts = new List<string>();
                while (!Done && !Words[At].Quoted && !string.Equals(Words[At].Text, "for", StringComparison.OrdinalIgnoreCase))
                    parts.Add(Words[At++].Text);
                if (parts.Count == 0) return Fail("write which key, like press E or press Space");
                string name = string.Join(" ", parts.ToArray());
                if (!Keys().TryGetValue(name, out vk))
                    return Fail("there is no key called \"" + name + "\". Write it the way it is printed on the keyboard: E, 5, Space, Enter, Shift, F5, Left arrow");
                return NotTheMouse(vk);
            }

            // Windows numbers the mouse's buttons among the keys. Sent as a
            // key press they click nothing, so they are not keys here.
            bool NotTheMouse(byte vk)
            {
                if (vk == 1 || vk == 2 || vk == 4 || vk == 5 || vk == 6)
                    return Fail("that is a button on the mouse, not a key. Write click or right click");
                return true;
            }
        }

        // The steps a text stands for, added to `into`. Null when every line
        // is understood; otherwise what is wrong and on which line, and
        // `into` is not to be used.
        public static string Read(string text, PictureShelf shelf, List<MacroStep> into)
        {
            if (shelf == null) shelf = new PictureShelf();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                // "repeat forever:" reads as well with its colon as without.
                if (line.EndsWith(":")) line = line.Substring(0, line.Length - 1);

                string problem;
                List<Token> words = Words(line, out problem);
                MacroStep step = null;
                if (words != null && words.Count > 0)
                {
                    Reader r = new Reader(words);
                    step = Step(r, shelf);
                    if (step != null && !r.Done && r.Problem == null)
                        r.Fail("didn't expect \"" + words[r.At].Text + "\" there");
                    problem = r.Problem;
                }
                if (problem != null) return "Line " + (i + 1) + ": " + problem + ".";
                if (step != null) into.Add(step);
            }

            Macro whole = new Macro();
            whole.Steps.AddRange(into);
            return whole.BlockProblem();
        }

        // The line a problem is about, counted from 1, or 0 when it is
        // about the macro as a whole.
        public static int LineOf(string problem)
        {
            Match m = Regex.Match(problem ?? "", "^Line (\\d+):");
            int line;
            return m.Success && int.TryParse(m.Groups[1].Value, out line) ? line : 0;
        }

        static MacroStep Step(Reader r, PictureShelf shelf)
        {
            bool right = r.Take("right");
            if (right && !Is(r, "click") && !Is(r, "drag"))
            {
                r.Fail("\"right\" goes before click or drag, like right click 50 50");
                return null;
            }
            int ms, n;
            double x, y, x2, y2;
            byte vk;
            string text;
            MacroStep s;

            if (r.Take("press") || r.Take("tap"))
            {
                if (!r.Key(out vk)) return null;
                s = MacroStep.Key(vk, 50);
                if (r.Take("for")) { if (!r.Seconds("how long to hold it", false, out ms)) return null; s.HoldMs = ms; }
                return s;
            }
            if (r.Take("hold"))
            {
                if (!r.Key(out vk)) return null;
                // "hold W" keeps it down until "release W"; "hold W for 2" lets go by itself.
                if (!r.Take("for")) return MacroStep.KeyDown(vk);
                return r.Seconds("how long to hold it", false, out ms) ? MacroStep.Key(vk, ms) : null;
            }
            if (r.Take("release"))
                return r.Key(out vk) ? MacroStep.KeyUp(vk) : null;

            if (r.Take("wait"))
            {
                if (!r.Take("for") && !r.Take("until"))
                    return r.Seconds("how long to wait", false, out ms) ? MacroStep.Wait(ms) : null;
                s = MacroStep.WaitPicture(null, null, 10000, false);
                if (!Picture(r, shelf, s)) return null;
                while (!r.Done)
                {
                    if (r.Take("gone")) s.UntilGone = true;
                    else if (r.Take("shows")) s.UntilGone = false;
                    else if (r.Take("up"))
                    {
                        r.Take("to");
                        if (!r.Seconds("how long to wait for it", false, out ms)) return null;
                        s.TimeoutMs = ms;
                    }
                    else if (!Look(r, shelf, s)) return null;
                }
                return s;
            }

            if (r.Take("click"))
            {
                if (r.Take("here")) return MacroStep.ClickHere(right);
                if (!r.Take("picture"))
                    return r.Place(out x, out y) ? MacroStep.Click(x, y, right) : null;
                s = MacroStep.ClickPicture(null, null, 0);
                s.RightButton = right;
                if (!Picture(r, shelf, s)) return null;
                while (!r.Done)
                {
                    if (r.Take("wait"))
                    {
                        if (!r.Seconds("how long to wait for it", true, out ms)) return null;
                        s.TimeoutMs = ms;
                    }
                    else if (r.Take("offset"))
                    {
                        if (!r.Whole("how many pixels right of its middle", -5000, 5000, out n)) return null;
                        s.OffX = n;
                        if (!r.Whole("how many pixels below its middle", -5000, 5000, out n)) return null;
                        s.OffY = n;
                    }
                    else if (!Look(r, shelf, s)) return null;
                }
                return s;
            }

            if (r.Take("move"))
            {
                r.Take("to");
                return r.Place(out x, out y) ? MacroStep.Move(x, y) : null;
            }

            if (r.Take("drag"))
            {
                if (!r.Place(out x, out y)) return null;
                if (!r.Take("to")) { r.Fail("a drag needs an end spot, like drag 40 50 to 60 50"); return null; }
                if (!r.Place(out x2, out y2)) return null;
                ms = 500;
                if (r.Take("over") && !r.Seconds("how long the drag takes", false, out ms)) return null;
                if (x == x2 && y == y2) { r.Fail("the drag starts and ends in the same spot"); return null; }
                return MacroStep.Drag(x, y, x2, y2, ms, right);
            }

            if (r.Take("scroll"))
            {
                bool down = r.Take("down");
                if (!down && !r.Take("up")) { r.Fail("write which way, like scroll up 3 or scroll down 3"); return null; }
                if (!r.Whole("how many clicks of the wheel", 1, 50, out n)) return null;
                if (r.Take("at"))
                    return r.Place(out x, out y) ? MacroStep.Scroll(x, y, down ? -n : n) : null;
                r.Take("here");
                s = MacroStep.Scroll(0.5, 0.5, down ? -n : n);
                s.AtPointer = true;
                return s;
            }

            // One word or the other: "type say" is not a way to say it.
            bool typing = r.Take("type"), chat = !typing && r.Take("say");
            if (typing || chat)
            {
                if (!r.Quoted("what to type", out text)) return null;
                if (text.Length == 0) { r.Fail("there is nothing between the quotes to type"); return null; }
                s = MacroStep.Type(text);
                s.InChat = chat;
                return s;
            }

            if (r.Take("repeat"))
            {
                if (r.Take("forever")) return MacroStep.Repeat(RepeatMode.Forever, 2);
                if (r.Take("until"))
                {
                    s = MacroStep.Repeat(RepeatMode.UntilSeen, 2);
                    if (!Picture(r, shelf, s)) return null;
                    bool said = false;
                    while (!r.Done)
                    {
                        if (r.Take("shows")) { s.Mode = RepeatMode.UntilSeen; said = true; }
                        else if (r.Take("gone")) { s.Mode = RepeatMode.UntilGone; said = true; }
                        else if (r.Take("or")) { r.Fail("a repeat can't stop the macro, it can only end"); return null; }
                        else if (!Look(r, shelf, s)) return null;
                    }
                    if (!said) { r.Fail("write what ends it, like repeat until \"Shop\" shows or repeat until \"Shop\" gone"); return null; }
                    return s;
                }
                if (!r.Whole("how many times", 2, 9999, out n)) return null;
                r.Take("times");
                return MacroStep.Repeat(RepeatMode.Times, n);
            }

            if (r.Take("end"))
            {
                r.Take("repeat");
                return MacroStep.EndRepeat();
            }

            r.Fail("don't know \"" + r.Words[r.At].Text + "\". A line starts with " + STARTS);
            return null;
        }

        static bool Is(Reader r, string word)
        {
            return !r.Done && !r.Words[r.At].Quoted && string.Equals(r.Words[r.At].Text, word, StringComparison.OrdinalIgnoreCase);
        }

        // The picture a step goes by. "" is no picture yet: the step is
        // kept, and the editor asks for one before it will save.
        static bool Picture(Reader r, PictureShelf shelf, MacroStep s)
        {
            string name, file, mask;
            if (!r.Quoted("the picture's name", out name)) return false;
            if (name.Length == 0) return true;
            if (!shelf.Find(name, out file, out mask))
            {
                List<string> have = shelf.Names();
                return r.Fail("there is no picture called \"" + name + "\" on this PC"
                    + (have.Count == 0 ? ". Make one first: add a \"Click a picture\" step and choose it there"
                                       : ". The ones you have: " + string.Join(", ", have.GetRange(0, Math.Min(8, have.Count)).ToArray())
                                         + (have.Count > 8 ? " and " + (have.Count - 8) + " more" : "")));
            }
            s.Picture = file;
            s.Mask = mask;
            return true;
        }

        // The words that say how a picture is looked for, on any line that
        // has one.
        static bool Look(Reader r, PictureShelf shelf, MacroStep s)
        {
            int n;
            string box;
            if (r.Take("alike"))
            {
                if (!r.Whole("how close the match must be", 50, 100, out n)) return false;
                s.Tolerance = n / 100.0;
                return true;
            }
            if (r.Take("in"))
            {
                if (!r.Quoted("the box's name", out box)) return false;
                // A box that isn't there is not looked in at all - the whole
                // window is - so a name spelt wrong would pass for working.
                if (box.Length > 0 && !shelf.HasBox(box))
                    return r.Fail("there is no box called \"" + box + "\" on this PC"
                        + (shelf.Boxes.Count == 0 ? ". Draw one first: edit a picture step and choose \"Look in\""
                                                  : ". The ones you have: " + string.Join(", ", shelf.Boxes.GetRange(0, Math.Min(8, shelf.Boxes.Count)).ToArray())));
                s.Box = box.Length == 0 ? null : box;
                return true;
            }
            if (r.Take("or"))
            {
                if (!r.Take("stop")) return r.Fail("after \"or\" comes \"stop\", which stops the macro if the picture isn't there");
                s.StopIfMissing = true;
                return true;
            }
            return r.Fail("didn't expect \"" + r.Words[r.At].Text + "\" there");
        }
    }

    // The guide to making mods - packs, shaders and macros as text - which
    // is in the exe and is written out to be read.
    static class Guide
    {
        public const string RESOURCE = "RobloxKeeper.MODDING.md";

        public static string Text()
        {
            try
            {
                using (Stream s = typeof(Guide).Assembly.GetManifestResourceStream(RESOURCE))
                {
                    if (s == null) return null;
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        public static string File
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RobloxKeeper", "How to make mods.html");
            }
        }

        // Written fresh each time, so it is this version's, as a page for
        // the browser: the guide is written with tables and examples, which
        // read as a mess of bars and marks in Notepad.
        public static void Open(Action<string> say)
        {
            try
            {
                string text = Text();
                if (text == null)
                {
                    if (say != null) say("The guide isn't included in this version. Look for MODDING.md next to the README.");
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                System.IO.File.WriteAllText(File, Html(text), new UTF8Encoding(false));
                System.Diagnostics.Process.Start(File);
            }
            catch (Exception ex)
            {
                if (say != null) say("Couldn't open the guide: " + ex.Message);
            }
        }

        // ---------- as a page ----------

        const string STYLE =
            "body{background:#14161c;color:#d8dbe4;font:15px/1.6 'Segoe UI',sans-serif;margin:0}" +
            "main{max-width:820px;margin:0 auto;padding:32px 28px 80px}" +
            "h1{font-size:30px;margin:0 0 8px}h2{font-size:23px;margin:44px 0 8px;padding-top:20px;border-top:1px solid #2b2f3d}" +
            "h3{font-size:17px;margin:28px 0 6px;color:#fff}b{color:#fff}a{color:#9a90ff}" +
            "code{font:13.5px Consolas,monospace;background:#1f2230;padding:1px 5px;border-radius:4px;color:#c9c4ff}" +
            "pre{font:13.5px/1.5 Consolas,monospace;background:#1f2230;padding:14px 16px;border-radius:8px;overflow-x:auto;color:#e6e8ef}" +
            "table{border-collapse:collapse;margin:12px 0;width:100%}th,td{text-align:left;padding:7px 12px;border-bottom:1px solid #2b2f3d;vertical-align:top}" +
            "th{color:#9aa0b4;font-weight:600;font-size:13px}li{margin:4px 0}";

        // The few kinds of line the guide is written in - headings,
        // paragraphs, lists, tables, examples - and nothing else. It makes a
        // page with no script in it and nothing fetched from anywhere.
        public static string Html(string markdown)
        {
            StringBuilder h = new StringBuilder();
            h.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>Making mods for RobloxKeeper</title><style>")
             .Append(STYLE).Append("</style></head><body><main>\n");

            StringBuilder para = new StringBuilder(), item = new StringBuilder();
            string list = null;             // "ul" or "ol" while in one
            bool code = false, table = false;

            foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("```"))
                {
                    Close(h, para, item, ref list, ref table);
                    h.Append(code ? "</pre>\n" : "<pre>");
                    code = !code;
                    continue;
                }
                if (code) { h.Append(Escape(raw)).Append('\n'); continue; }

                string line = raw.TrimEnd();
                if (line.Length == 0) { Close(h, para, item, ref list, ref table); continue; }

                if (line[0] == '#')
                {
                    Close(h, para, item, ref list, ref table);
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    h.Append("<h").Append(level).Append('>').Append(Inline(line.Substring(level).Trim())).Append("</h").Append(level).Append(">\n");
                    continue;
                }

                if (line[0] == '|')
                {
                    string[] cells = line.Trim('|').Split('|');
                    if (line.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0) continue;     // the rule under the heading row
                    string cell = table ? "td" : "th";
                    if (!table) { Close(h, para, item, ref list, ref table); h.Append("<table>\n"); table = true; }
                    h.Append("<tr>");
                    foreach (string c in cells) h.Append('<').Append(cell).Append('>').Append(Inline(c.Trim())).Append("</").Append(cell).Append('>');
                    h.Append("</tr>\n");
                    continue;
                }

                Match numbered = Regex.Match(line, "^\\d+\\. ");
                if (line.StartsWith("- ") || numbered.Success)
                {
                    string kind = numbered.Success ? "ol" : "ul";
                    if (list != kind) { Close(h, para, item, ref list, ref table); h.Append('<').Append(kind).Append(">\n"); list = kind; }
                    else EndItem(h, item);
                    item.Append(line.Substring(numbered.Success ? numbered.Length : 2));
                    continue;
                }
                // A list item that runs on to the next line.
                if (list != null && raw.StartsWith("  ")) { item.Append(' ').Append(line.Trim()); continue; }

                if (list != null || table) Close(h, para, item, ref list, ref table);
                if (para.Length > 0) para.Append(' ');
                para.Append(line.Trim());
            }
            Close(h, para, item, ref list, ref table);
            if (code) h.Append("</pre>\n");
            return h.Append("</main></body></html>\n").ToString();
        }

        static void EndItem(StringBuilder h, StringBuilder item)
        {
            if (item.Length == 0) return;
            h.Append("<li>").Append(Inline(item.ToString())).Append("</li>\n");
            item.Length = 0;
        }

        static void Close(StringBuilder h, StringBuilder para, StringBuilder item, ref string list, ref bool table)
        {
            if (para.Length > 0) { h.Append("<p>").Append(Inline(para.ToString())).Append("</p>\n"); para.Length = 0; }
            if (list != null) { EndItem(h, item); h.Append("</").Append(list).Append(">\n"); list = null; }
            if (table) { h.Append("</table>\n"); table = false; }
        }

        static string Escape(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // `code`, **bold** and *leaning* - and nothing inside `code` is
        // taken for either of the others.
        static string Inline(string text)
        {
            string[] parts = Escape(text).Split('`');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1 && i < parts.Length - 1) { sb.Append("<code>").Append(parts[i]).Append("</code>"); continue; }
                string t = i % 2 == 1 ? "`" + parts[i] : parts[i];       // a ` with no partner is just a `
                t = Regex.Replace(t, "\\*\\*(.+?)\\*\\*", "<b>$1</b>");
                t = Regex.Replace(t, "\\*(.+?)\\*", "<i>$1</i>");
                sb.Append(t);
            }
            return sb.ToString();
        }
    }
}
