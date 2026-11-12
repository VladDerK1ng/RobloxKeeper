using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    // One thing a script can do, as the Functions list shows it.
    class ScriptFunction
    {
        public string Group;        // the heading it is listed under
        public string Name;         // as it is written in a script
        public string Insert;       // what a click puts in, ready to change
        public string Says;         // one plain line on what it does

        public ScriptFunction(string group, string name, string insert, string says)
        {
            Group = group; Name = name; Insert = insert; Says = says;
        }
    }

    // Every function the app gives a script, in the order the Functions list
    // shows them. Places are % across and % down the window: 50, 50 is the
    // middle. A test runs each Insert against the real engine, so a name or
    // an argument that drifts from LuaScripts shows up there.
    static class ScriptFunctions
    {
        public static readonly ScriptFunction[] All =
        {
            new ScriptFunction("Keys", "press", "press(\"e\")", "Press a key. Add how long to hold it, in seconds."),
            new ScriptFunction("Keys", "keydown", "keydown(\"w\")", "Hold a key down until you let go of it."),
            new ScriptFunction("Keys", "keyup", "keyup(\"w\")", "Let go of a key you are holding."),
            new ScriptFunction("Keys", "type", "type(\"hello\")", "Type some text."),
            new ScriptFunction("Keys", "chat", "chat(\"hello\")", "Say something in the Roblox chat."),

            new ScriptFunction("Mouse", "click", "click(50, 50)", "Click a spot. 50, 50 is the middle of the window."),
            new ScriptFunction("Mouse", "doubleclick", "doubleclick(50, 50)", "Click twice, fast."),
            new ScriptFunction("Mouse", "move", "move(50, 50)", "Move the pointer without clicking."),
            new ScriptFunction("Mouse", "drag", "drag(20, 50, 80, 50)", "Press at one spot, drag to another and let go."),
            new ScriptFunction("Mouse", "scroll", "scroll(3)", "Turn the wheel. A number above 0 goes up, below 0 goes down."),

            new ScriptFunction("Time", "wait", "wait(1)", "Pause for that many seconds."),
            new ScriptFunction("Time", "elapsed", "elapsed()", "How many seconds the script has been running."),

            new ScriptFunction("The screen", "find", "find(\"coin\")", "Look for a picture. You get its spot, or nothing if it isn't there."),
            new ScriptFunction("The screen", "waitfor", "waitfor(\"coin\", 10)", "Wait up to that many seconds for a picture to show up."),
            new ScriptFunction("The screen", "waitgone", "waitgone(\"coin\", 10)", "Wait up to that many seconds for a picture to go away."),
            new ScriptFunction("The screen", "clickpicture", "clickpicture(\"coin\")", "Click a picture if it's showing. You get true if it did."),
            new ScriptFunction("The screen", "color", "color(50, 50)", "The colour at a spot, as red, green and blue."),
            new ScriptFunction("The screen", "read", "read()", "The words on the window, as text."),
            new ScriptFunction("The screen", "see", "see(\"Sell\")", "True if those words are on the window."),

            new ScriptFunction("Windows", "windows", "windows()", "A list of your Roblox windows."),
            new ScriptFunction("Windows", "use", "use(1)", "Pick the Roblox window to play on. use(\"screen\") plays on the whole screen."),

            new ScriptFunction("Telling you", "print", "print(\"hello\")", "Write a line in the output box."),
            new ScriptFunction("Telling you", "say", "say(\"hello\")", "Write a line in the activity list."),
            new ScriptFunction("Telling you", "notify", "notify(\"hello\")", "Show a pop-up by the clock."),
            new ScriptFunction("Telling you", "status", "status(\"Farming\")", "Show a small label over the game. status() hides it."),
            new ScriptFunction("Telling you", "discord", "discord(\"hello\")", "Send a message to your Discord webhook."),

            new ScriptFunction("Counting", "count", "count(\"sold\")", "Add 1 to a counter. The counters are in the summary when the script ends."),
            new ScriptFunction("Counting", "remember", "remember(\"best\", 5)", "Keep a number, text or true/false for the next time."),
            new ScriptFunction("Counting", "recall", "recall(\"best\")", "Get what you kept, or nothing if there isn't anything."),

            new ScriptFunction("Ending", "stop", "stop()", "End the script.")
        };

        static HashSet<string> names;

        public static ICollection<string> Names
        {
            get
            {
                if (names == null)
                {
                    HashSet<string> n = new HashSet<string>();
                    foreach (ScriptFunction f in All) n.Add(f.Name);
                    names = n;
                }
                return names;
            }
        }
    }

    // Scripts to start from. Every one loops until it is stopped, and says in
    // its first lines which pictures it looks for: they are placeholders,
    // because nobody's game has a picture called "coin" until they cut one
    // out. "Add a picture" does that and writes its name in; the name in
    // the script is then changed to match.
    static class ScriptExamples
    {
        public static readonly string[] Names =
        {
            "Farm loop",
            "Click when a picture shows",
            "Say something in chat every few minutes",
            "Take turns on every Roblox window"
        };

        // The pictures each one looks for: what to cut out and call
        // that, before running it.
        public static string[] Pictures(int index)
        {
            switch (index)
            {
                case 0: return new string[] { "coin", "bag full" };
                case 1: return new string[] { "button" };
                default: return new string[0];
            }
        }

        public static string Code(int index)
        {
            switch (index)
            {
                case 0: return Lines(FARM);
                case 1: return Lines(CLICK);
                case 2: return Lines(CHAT);
                default: return Lines(TURNS);
            }
        }

        static string Lines(string[] lines) { return string.Join("\r\n", lines) + "\r\n"; }

        static readonly string[] FARM =
        {
            "-- Farm loop: walk a route, pick things up, sell when the bag is full.",
            "-- It keeps going until you press Stop.",
            "--",
            "-- Before you run it, click \"Add a picture\" and cut out two pictures from the game:",
            "--   1. the thing you pick up. Then change \"coin\" below to its name.",
            "--   2. the sign that says your bag is full. Then change \"bag full\" below to its name.",
            "-- Also change the keys and the seconds to fit your game.",
            "",
            "local pickup = \"coin\"",
            "local full = \"bag full\"",
            "",
            "status(\"Farming\")",
            "while true do",
            "    -- Walk the route: forward, then right, then back again.",
            "    keydown(\"w\")",
            "    wait(1.5)",
            "    keyup(\"w\")",
            "    keydown(\"d\")",
            "    wait(1)",
            "    keyup(\"d\")",
            "",
            "    -- Pick things up while they are showing, up to 10 at a time.",
            "    for try = 1, 10 do",
            "        if not clickpicture(pickup) then break end",
            "        count(\"collected\")",
            "        wait(0.3)",
            "    end",
            "",
            "    -- Sell when the bag is full. Press the key that talks to the seller in your game.",
            "    if find(full) then",
            "        status(\"Selling\")",
            "        press(\"e\")",
            "        wait(2)",
            "        count(\"sold\")",
            "        status(\"Farming\")",
            "    end",
            "end"
        };

        static readonly string[] CLICK =
        {
            "-- Click when a picture shows: wait for a picture, click it, and do it again.",
            "-- It keeps going until you press Stop.",
            "--",
            "-- Before you run it, click \"Add a picture\" and cut out the button from the game.",
            "-- Then change \"button\" below to its name.",
            "",
            "local picture = \"button\"",
            "",
            "while true do",
            "    -- Waits up to 10 seconds for it to show up.",
            "    if clickpicture(picture, 10) then",
            "        count(\"clicks\")",
            "    end",
            "    wait(0.5)",
            "end"
        };

        static readonly string[] CHAT =
        {
            "-- Say something in chat every few minutes. It keeps going until you press Stop.",
            "-- Change the words, and change 300 (seconds) to how often you want it.",
            "",
            "while true do",
            "    chat(\"Hello everyone!\")",
            "    count(\"said\")",
            "    wait(300)",
            "end"
        };

        static readonly string[] TURNS =
        {
            "-- Take turns on every Roblox window: do the same thing on each one, then start again.",
            "-- It keeps going until you press Stop. Change the keys to what you want to do.",
            "",
            "while true do",
            "    for number, window in ipairs(windows()) do",
            "        use(number)",
            "        status(\"Playing on \" .. window.name)",
            "        press(\"space\")",
            "        wait(1)",
            "        count(\"turns\")",
            "    end",
            "    wait(5)",
            "end"
        };
    }

    // Making scripts: a name that is free, and a macro turned into one.
    static class ScriptMaking
    {
        // "Farm loop", or "Farm loop 2" when that is taken - and cleaned
        // up first, because a macro's name can have things a file name can't.
        public static string UniqueName(ScriptStore store, string wanted)
        {
            string name = Clean(wanted);
            if (name.Length == 0) name = "New script";
            string pick = name;
            for (int n = 2; store.Find(pick) != null || ScriptStore.NameProblem(pick) != null; n++)
            {
                string tail = " " + n;
                pick = (name.Length + tail.Length > ScriptStore.MAX_NAME ? name.Substring(0, ScriptStore.MAX_NAME - tail.Length) : name) + tail;
                if (n > 999) break;
            }
            return pick;
        }

        static string Clean(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in (s ?? ""))
                if (c >= ' ' && Array.IndexOf(bad, c) < 0 && c != '"') sb.Append(c);
            string t = sb.ToString().Trim().TrimEnd('.').TrimStart('.').Trim();
            if (t.Length > ScriptStore.MAX_NAME) t = t.Substring(0, ScriptStore.MAX_NAME).Trim();
            return t;
        }

        // A macro as a new script under this name. Null when it was made;
        // otherwise why not.
        public static string FromMacro(IScriptHost host, Macro m, string name)
        {
            if (host == null) return "Scripts aren't ready yet. Try again in a moment.";
            if (m == null || m.Steps.Count == 0) return "There's nothing in it to turn into a script.";
            string why = host.Store.NameProblem(name, null);
            if (why != null) return why;
            string code = host.MacroAsLua(m);
            if (string.IsNullOrEmpty(code)) return "Couldn't turn it into a script.";
            return host.Store.Create(name, code);
        }
    }
}
