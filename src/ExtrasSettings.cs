using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RobloxKeeper
{
    // Everything chosen for the on-screen extras: the crosshair, the keys
    // display, zoom, the dim, the read-out and the held key.
    //
    // Kept in a file of its own, as lines of name=value, beside the app's
    // other settings. Anything missing or unreadable is the default, and
    // every number read is brought back inside what the windows offer.
    class ExtrasSettings
    {
        // Where a panel can go: top-left, top-right, bottom-left, bottom-right,
        // then the middle of the top and of the bottom. The first four are
        // what files saved earlier hold, so their numbers must not change.
        public const int CORNERS = 6;
        public static readonly string[] CornerNames = { "Top left", "Top right", "Bottom left", "Bottom right", "Top middle", "Bottom middle" };
        public static readonly string[] SizeNames = { "Small", "Medium", "Large" };
        public static readonly int[] ZoomLevels = { 2, 3, 4, 6 };
        public static readonly string[] ZoomNames = { "A little (2x)", "Closer (3x)", "Close (4x)", "Very close (6x)" };
        public static readonly string[] DarkNames = { "A little", "Medium", "Strong" };
        public static readonly int[] DarkPercent = { 30, 50, 70 };
        public static readonly string[] HoleNames = { "Small", "Medium", "Large" };
        public static readonly int[] HolePercent = { 18, 30, 45 };     // of the game's shorter side, as the clear circle's radius

        // crosshair
        public bool CrossOn;
        public byte CrossKey;                               // switches it; 0 for none
        public CrossStyle Cross = new CrossStyle();
        public List<string> Only = new List<string>();      // accounts or client names; empty means every client

        // keys and clicks display
        public bool KeysOn;
        public int KeysCorner = 2, KeysSize = 1;

        // zoom
        public bool ZoomOn;
        public byte ZoomKey;
        public int ZoomLevel = 1;                           // index into ZoomLevels

        // dim around the middle
        public bool DimOn;
        public int DimDark = 1, DimHole = 1;

        // read-out
        public bool PingOn, MemoryOn;
        public int StatsCorner = 1;

        // hold a key for me
        public bool HoldOn;
        public byte HoldKey = 0x10;                         // Shift
        public byte HoldWith;                               // the key that switches it; 0 means the held key itself

        // the font last given to Roblox from the font window, to say which it is
        public string FontName = "";

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "RobloxKeeper", "extras.txt");
            }
        }

        public bool AnyOn { get { return CrossOn || KeysOn || ZoomOn || DimOn || PingOn || MemoryOn || HoldOn; } }

        public ExtrasSettings Clone()
        {
            ExtrasSettings s = (ExtrasSettings)MemberwiseClone();
            s.Cross = Cross.Clone();
            s.Only = new List<string>(Only);
            return s;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        public void Tidy()
        {
            if (Cross == null) Cross = new CrossStyle();
            Cross.Tidy();
            if (Only == null) Only = new List<string>();
            KeysCorner = Clamp(KeysCorner, 0, CORNERS - 1);
            StatsCorner = Clamp(StatsCorner, 0, CORNERS - 1);
            KeysSize = Clamp(KeysSize, 0, SizeNames.Length - 1);
            ZoomLevel = Clamp(ZoomLevel, 0, ZoomLevels.Length - 1);
            DimDark = Clamp(DimDark, 0, DarkPercent.Length - 1);
            DimHole = Clamp(DimHole, 0, HolePercent.Length - 1);
            if (HoldKey == 0) HoldKey = 0x10;
            if (FontName == null) FontName = "";
            // Zoom with no key chosen can never be used; it is simply off.
            if (ZoomKey == 0) ZoomOn = false;
        }

        // ---------- as lines ----------

        public List<string> ToLines()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            Cross.Write(d);
            d["cross.on"] = B(CrossOn);
            d["cross.key"] = N(CrossKey);
            d["cross.only"] = string.Join("|", Only.ToArray());
            d["keys.on"] = B(KeysOn);
            d["keys.corner"] = N(KeysCorner);
            d["keys.size"] = N(KeysSize);
            d["zoom.on"] = B(ZoomOn);
            d["zoom.key"] = N(ZoomKey);
            d["zoom.level"] = N(ZoomLevel);
            d["dim.on"] = B(DimOn);
            d["dim.dark"] = N(DimDark);
            d["dim.hole"] = N(DimHole);
            d["stats.ping"] = B(PingOn);
            d["stats.memory"] = B(MemoryOn);
            d["stats.corner"] = N(StatsCorner);
            d["hold.on"] = B(HoldOn);
            d["hold.key"] = N(HoldKey);
            d["hold.with"] = N(HoldWith);
            d["font.name"] = FontName ?? "";

            List<string> keys = new List<string>(d.Keys);
            keys.Sort(StringComparer.Ordinal);
            List<string> lines = new List<string>();
            foreach (string k in keys) lines.Add(k + "=" + d[k].Replace("\r", " ").Replace("\n", " "));
            return lines;
        }

        public static ExtrasSettings Parse(IEnumerable<string> lines)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lines != null)
                foreach (string raw in lines)
                {
                    if (raw == null) continue;
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    d[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                }

            ExtrasSettings s = new ExtrasSettings();
            s.Cross = CrossStyle.Read(d);
            s.CrossOn = Bool(d, "cross.on", false);
            s.CrossKey = Key(d, "cross.key", 0);
            string only;
            if (d.TryGetValue("cross.only", out only))
                foreach (string name in only.Split('|'))
                    if (name.Trim().Length > 0) s.Only.Add(name.Trim());
            s.KeysOn = Bool(d, "keys.on", false);
            s.KeysCorner = Int(d, "keys.corner", s.KeysCorner);
            s.KeysSize = Int(d, "keys.size", s.KeysSize);
            s.ZoomOn = Bool(d, "zoom.on", false);
            s.ZoomKey = Key(d, "zoom.key", 0);
            s.ZoomLevel = Int(d, "zoom.level", s.ZoomLevel);
            s.DimOn = Bool(d, "dim.on", false);
            s.DimDark = Int(d, "dim.dark", s.DimDark);
            s.DimHole = Int(d, "dim.hole", s.DimHole);
            s.PingOn = Bool(d, "stats.ping", false);
            s.MemoryOn = Bool(d, "stats.memory", false);
            s.StatsCorner = Int(d, "stats.corner", s.StatsCorner);
            s.HoldOn = Bool(d, "hold.on", false);
            s.HoldKey = Key(d, "hold.key", s.HoldKey);
            s.HoldWith = Key(d, "hold.with", 0);
            string font;
            if (d.TryGetValue("font.name", out font)) s.FontName = font;
            s.Tidy();
            return s;
        }

        static string B(bool b) { return b ? "1" : "0"; }
        static string N(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        static int Int(Dictionary<string, string> d, string key, int fallback)
        {
            string v;
            int n;
            return d.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }

        static bool Bool(Dictionary<string, string> d, string key, bool fallback)
        {
            return Int(d, key, fallback ? 1 : 0) != 0;
        }

        static byte Key(Dictionary<string, string> d, string key, byte fallback)
        {
            int n = Int(d, key, fallback);
            return n < 0 || n > 254 ? fallback : (byte)n;
        }

        // ---------- the file ----------

        public static ExtrasSettings Load(string path)
        {
            try
            {
                if (File.Exists(path)) return Parse(File.ReadAllLines(path));
            }
            catch { }
            return Parse(null);
        }

        public bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".new";
                File.WriteAllLines(temp, ToLines().ToArray());
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch { return false; }
        }
    }
}
