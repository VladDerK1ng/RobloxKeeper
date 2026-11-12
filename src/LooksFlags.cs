using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RobloxKeeper
{
    enum LooksAntiAliasing { Default, X1, X2, X4, X8 }

    enum LooksGrass { Default, None, Short, Far }

    enum LooksTextures { Default, Low, Medium, High }

    // The graphics choices as the user sees them. Everything starts at
    // "default", which means "say nothing and let Roblox decide" - a default
    // choice writes no flag at all, rather than a flag holding a guess at
    // what Roblox would have picked.
    class LooksChoices
    {
        public const int QUALITY_MIN = 1;
        public const int QUALITY_MAX = 21;

        // How far out grass is drawn, in studs, as nearest and farthest. These
        // are this app's own idea of "short" and "far"; nothing from Roblox
        // says what they should be.
        public const int GRASS_NONE_MIN = 0;
        public const int GRASS_NONE_MAX = 0;
        public const int GRASS_SHORT_MIN = 50;
        public const int GRASS_SHORT_MAX = 120;
        public const int GRASS_FAR_MIN = 300;
        public const int GRASS_FAR_MAX = 900;

        // The texture override counts up from 0, the lowest. High is the top
        // of the range rather than the step below it.
        public const int TEXTURES_LOW = 0;
        public const int TEXTURES_MEDIUM = 1;
        public const int TEXTURES_HIGH = 3;

        public LooksAntiAliasing AntiAliasing;

        // 0 leaves the quality to Roblox's own slider; 1 to 21 pins it.
        public int QualityLevel;

        public LooksGrass Grass;
        public bool DpiScalingOff;

        // The 0.746 client no longer contains either texture flag name, so
        // setting this does nothing there. It stays in the model because older
        // clients still read it, and the window is free to hide it.
        public LooksTextures Textures;

        // Only the choices that are not "default", as flag name to JSON text -
        // the same form every other dictionary in LooksFlags uses, so this can
        // go straight to Merge or Write. Roblox's own flag lists carry every
        // value as a string, with True and False capitalised, so these do too.
        public Dictionary<string, string> ToFlags()
        {
            Dictionary<string, string> flags = new Dictionary<string, string>(StringComparer.Ordinal);

            if (AntiAliasing == LooksAntiAliasing.X1) flags[LooksFlags.MSAA] = LooksFlags.Quoted("1");
            else if (AntiAliasing == LooksAntiAliasing.X2) flags[LooksFlags.MSAA] = LooksFlags.Quoted("2");
            else if (AntiAliasing == LooksAntiAliasing.X4) flags[LooksFlags.MSAA] = LooksFlags.Quoted("4");
            else if (AntiAliasing == LooksAntiAliasing.X8) flags[LooksFlags.MSAA] = LooksFlags.Quoted("8");

            if (QualityLevel >= QUALITY_MIN)
                flags[LooksFlags.QUALITY] = Number(Math.Min(QualityLevel, QUALITY_MAX));

            if (Grass == LooksGrass.None) SetGrass(flags, GRASS_NONE_MIN, GRASS_NONE_MAX);
            else if (Grass == LooksGrass.Short) SetGrass(flags, GRASS_SHORT_MIN, GRASS_SHORT_MAX);
            else if (Grass == LooksGrass.Far) SetGrass(flags, GRASS_FAR_MIN, GRASS_FAR_MAX);

            if (DpiScalingOff) flags[LooksFlags.DPI_OFF] = LooksFlags.Quoted("True");

            if (Textures != LooksTextures.Default)
            {
                // The level is ignored unless the switch beside it is on.
                flags[LooksFlags.TEXTURES_ON] = LooksFlags.Quoted("True");
                flags[LooksFlags.TEXTURES_LEVEL] = Number(
                    Textures == LooksTextures.Low ? TEXTURES_LOW
                    : Textures == LooksTextures.Medium ? TEXTURES_MEDIUM
                    : TEXTURES_HIGH);
            }
            return flags;
        }

        // The choices a settings file amounts to. A value somebody typed by
        // hand that is not one of ours shows as the nearest choice, so the
        // window always has something to select.
        public static LooksChoices FromFlags(IDictionary<string, string> flags)
        {
            LooksChoices c = new LooksChoices();
            if (flags == null) return c;
            int n;

            if (ReadInt(flags, LooksFlags.MSAA, out n))
                c.AntiAliasing = n >= 8 ? LooksAntiAliasing.X8
                               : n >= 4 ? LooksAntiAliasing.X4
                               : n >= 2 ? LooksAntiAliasing.X2
                               : n >= 1 ? LooksAntiAliasing.X1
                               : LooksAntiAliasing.Default;

            if (ReadInt(flags, LooksFlags.QUALITY, out n) && n >= QUALITY_MIN)
                c.QualityLevel = Math.Min(n, QUALITY_MAX);

            // The far distance decides: with it at nothing there is no grass,
            // whatever the near one says.
            if (ReadInt(flags, LooksFlags.GRASS_MAX, out n) || ReadInt(flags, LooksFlags.GRASS_MIN, out n))
                c.Grass = n <= GRASS_NONE_MAX ? LooksGrass.None
                        : n <= (GRASS_SHORT_MAX + GRASS_FAR_MAX) / 2 ? LooksGrass.Short
                        : LooksGrass.Far;

            c.DpiScalingOff = ReadBool(flags, LooksFlags.DPI_OFF);

            if (ReadBool(flags, LooksFlags.TEXTURES_ON) && ReadInt(flags, LooksFlags.TEXTURES_LEVEL, out n))
                c.Textures = n <= TEXTURES_LOW ? LooksTextures.Low
                           : n <= TEXTURES_MEDIUM ? LooksTextures.Medium
                           : LooksTextures.High;
            return c;
        }

        static string Number(int n)
        {
            return LooksFlags.Quoted(n.ToString(CultureInfo.InvariantCulture));
        }

        static void SetGrass(Dictionary<string, string> flags, int min, int max)
        {
            flags[LooksFlags.GRASS_MIN] = Number(min);
            flags[LooksFlags.GRASS_MAX] = Number(max);
        }

        static bool ReadInt(IDictionary<string, string> flags, string name, out int value)
        {
            value = 0;
            string token;
            if (!flags.TryGetValue(name, out token)) return false;
            return int.TryParse(LooksFlags.Text(token).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }

        // People write true, "true", "True" and 1 for the same thing.
        static bool ReadBool(IDictionary<string, string> flags, string name)
        {
            string token;
            if (!flags.TryGetValue(name, out token)) return false;
            string text = LooksFlags.Text(token).Trim();
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }
    }

    // Something that could not be done, and why, in words fit to show.
    class LooksFailure
    {
        public string Where;
        public string Why;

        public LooksFailure(string where, string why)
        {
            Where = where;
            Why = why;
        }
    }

    class LooksFlagsResult
    {
        // Version folders left the way they were asked to be.
        public int Written;

        // Version folders that were not, by folder name.
        public readonly List<LooksFailure> Failed = new List<LooksFailure>();
    }

    // Graphics presets through the one settings file Roblox reads for them:
    // <version folder>\ClientSettings\ClientAppSettings.json.
    //
    // Since 2025-09-30 Roblox only honours a short list of flags in that file.
    // Anything else is ignored, and getting other flags past that check is
    // what gets accounts banned. So nothing here can put a name on disk that
    // is not on the list - not Merge, and not Write if it is called directly.
    //
    // The file is the user's as much as ours. Whatever else is already in it
    // stays exactly as written, including names Roblox no longer honours:
    // those do nothing, and removing somebody's own edits is not our call.
    //
    // Every dictionary here is flag name to JSON text - "4" with its quotes,
    // or a bare true or 12 - exactly as it sits in the file, so a value we do
    // not own goes back byte for byte. Text() gives the plain value.
    static class LooksFlags
    {
        public const string FOLDER = "ClientSettings";
        public const string FILE = "ClientAppSettings.json";
        public const string PLAYER_EXE = "RobloxPlayerBeta.exe";

        public const string MSAA = "FIntDebugForceMSAASamples";
        public const string QUALITY = "DFIntDebugFRMQualityLevelOverride";
        public const string GRASS_MAX = "FIntFRMMaxGrassDistance";
        public const string GRASS_MIN = "FIntFRMMinGrassDistance";
        public const string DPI_OFF = "DFFlagDisableDPIScale";
        public const string TEXTURES_ON = "DFFlagTextureQualityOverrideEnabled";
        public const string TEXTURES_LEVEL = "DFIntTextureQualityOverride";

        // Roblox's published list, name for name.
        public static readonly string[] Allowed =
        {
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            "FFlagHandleAltEnterFullscreenManually",
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples",
            "DFFlagDisableDPIScale",
            "FFlagDebugGraphicsPreferD3D11",
            "FFlagDebugGraphicsPreferVulkan",
            "FFlagDebugGraphicsPreferOpenGL",
            "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer",
            "DFIntDebugFRMQualityLevelOverride",
            "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance",
            "FIntGrassMovementReducedMotionFactor"
        };

        // The ones the choices above set. Only these are ever removed.
        public static readonly string[] Owned =
        {
            MSAA, QUALITY, GRASS_MAX, GRASS_MIN, DPI_OFF, TEXTURES_ON, TEXTURES_LEVEL
        };

        // Flag names are case-sensitive to Roblox, so they are here too.
        public static bool IsAllowed(string name)
        {
            return name != null && Array.IndexOf(Allowed, name) >= 0;
        }

        public static bool IsOwned(string name)
        {
            return name != null && Array.IndexOf(Owned, name) >= 0;
        }

        public static string PathIn(string versionFolder)
        {
            return Path.Combine(versionFolder, FOLDER, FILE);
        }

        // Full paths of the installed versions under a root: the version-*
        // folders that hold a player. Roblox Studio lives beside them in a
        // folder of the same shape, and it has no use for any of this.
        public static List<string> VersionFolders(string versionsRoot)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(versionsRoot)) return list;
            try
            {
                if (!Directory.Exists(versionsRoot)) return list;
                foreach (string d in Directory.GetDirectories(versionsRoot, "version-*"))
                {
                    // Writing into a link would land somewhere nobody chose.
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    if (File.Exists(Path.Combine(d, PLAYER_EXE))) list.Add(d);
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // ---------- Reading ----------

        public static Dictionary<string, string> Read(string path)
        {
            bool unreadable;
            return Read(path, out unreadable);
        }

        // A file that is not there is simply no flags. One that is there but
        // cannot be made sense of also comes back empty, with unreadable set,
        // so the caller can tell "nothing set" from "don't touch this".
        public static Dictionary<string, string> Read(string path, out bool unreadable)
        {
            string problem;
            Dictionary<string, string> flags = Load(path, out problem);
            unreadable = problem != null;
            return flags;
        }

        static Dictionary<string, string> Load(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) return Empty();
                bool unreadable;
                Dictionary<string, string> flags = Parse(File.ReadAllText(path), out unreadable);
                if (unreadable) problem = "the settings file is broken (not valid JSON), so it was left as it is";
                return flags;
            }
            catch (Exception ex)
            {
                problem = "the settings file couldn't be read - " + Why(ex);
                return Empty();
            }
        }

        static Dictionary<string, string> Empty()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // One flat JSON object. Written by hand because the project has no
        // JSON library, and because a library would hand back parsed values
        // where this needs the text each one was written as.
        public static Dictionary<string, string> Parse(string json, out bool unreadable)
        {
            unreadable = false;
            Dictionary<string, string> flags = Empty();
            if (json == null) return flags;

            int i = 0;
            // Notepad saves with a byte order mark; that is not a broken file.
            if (json.Length > 0 && json[0] == (char)0xFEFF) i = 1;
            SkipSpace(json, ref i);
            if (i == json.Length) return flags;

            bool ok = ReadObject(json, ref i, flags);
            if (ok)
            {
                SkipSpace(json, ref i);
                ok = i == json.Length;
            }
            if (ok) return flags;

            unreadable = true;
            return Empty();
        }

        static bool ReadObject(string s, ref int i, Dictionary<string, string> into)
        {
            if (i >= s.Length || s[i] != '{') return false;
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return true; }

            while (true)
            {
                string key;
                if (!ReadString(s, ref i, out key)) return false;
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') return false;
                i++;
                SkipSpace(s, ref i);

                int start = i;
                if (!SkipValue(s, ref i, 0)) return false;
                // A name given twice: the later one is what any reader keeps.
                if (into != null) into[key] = s.Substring(start, i - start);

                SkipSpace(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == '}') { i++; return true; }
                if (s[i] != ',') return false;
                i++;
                SkipSpace(s, ref i);
            }
        }

        // A file of nothing but brackets must not be able to run the stack out.
        const int MAX_DEPTH = 32;

        static bool SkipValue(string s, ref int i, int depth)
        {
            if (i >= s.Length || depth > MAX_DEPTH) return false;
            char c = s[i];

            if (c == '"')
            {
                string ignored;
                return ReadString(s, ref i, out ignored);
            }

            // Flags are never objects or lists, but one sitting in the user's
            // file is theirs to keep, so it is stepped over whole.
            if (c == '{') return SkipNested(s, ref i, depth, '}', true);
            if (c == '[') return SkipNested(s, ref i, depth, ']', false);

            int start = i;
            while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            string word = s.Substring(start, i - start);
            return word == "true" || word == "false" || word == "null" || IsNumber(word);
        }

        static bool SkipNested(string s, ref int i, int depth, char close, bool named)
        {
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == close) { i++; return true; }

            while (true)
            {
                if (named)
                {
                    string ignored;
                    if (!ReadString(s, ref i, out ignored)) return false;
                    SkipSpace(s, ref i);
                    if (i >= s.Length || s[i] != ':') return false;
                    i++;
                    SkipSpace(s, ref i);
                }
                if (!SkipValue(s, ref i, depth + 1)) return false;

                SkipSpace(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == close) { i++; return true; }
                if (s[i] != ',') return false;
                i++;
                SkipSpace(s, ref i);
            }
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static bool ReadString(string s, ref int i, out string value)
        {
            value = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c < ' ') return false;
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) return false;
                char e = s[i++];
                if (e == '"' || e == '\\' || e == '/') sb.Append(e);
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'n') sb.Append('\n');
                else if (e == 'r') sb.Append('\r');
                else if (e == 't') sb.Append('\t');
                else if (e == 'u')
                {
                    int code;
                    if (i + 4 > s.Length) return false;
                    if (!int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out code)) return false;
                    sb.Append((char)code);
                    i += 4;
                }
                else return false;
            }
            return false;
        }

        static bool IsNumber(string w)
        {
            int i = 0;
            if (i < w.Length && w[i] == '-') i++;
            if (i >= w.Length) return false;
            if (w[i] == '0') i++;
            else if (!Digits(w, ref i)) return false;

            if (i < w.Length && w[i] == '.')
            {
                i++;
                if (!Digits(w, ref i)) return false;
            }
            if (i < w.Length && (w[i] == 'e' || w[i] == 'E'))
            {
                i++;
                if (i < w.Length && (w[i] == '+' || w[i] == '-')) i++;
                if (!Digits(w, ref i)) return false;
            }
            return i == w.Length;
        }

        static bool Digits(string w, ref int i)
        {
            int start = i;
            while (i < w.Length && w[i] >= '0' && w[i] <= '9') i++;
            return i > start;
        }

        // ---------- Values ----------

        // The plain value behind a piece of JSON text: "4" and 4 are both 4.
        public static string Text(string token)
        {
            if (token == null) return "";
            if (token.Length > 0 && token[0] == '"')
            {
                int i = 0;
                string value;
                if (ReadString(token, ref i, out value) && i == token.Length) return value;
            }
            return token;
        }

        // A plain value as a JSON string.
        public static string Quoted(string plain)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in plain ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // A value handed over plain rather than as JSON text is quoted, never
        // written raw: one stray word must not cost the user the whole file.
        static string Token(string value)
        {
            if (value != null)
            {
                int i = 0;
                if (SkipValue(value, ref i, 0) && i == value.Length) return value;
            }
            return Quoted(value);
        }

        // ---------- Merging ----------

        public static Dictionary<string, string> Merge(IDictionary<string, string> existing,
                                                       IDictionary<string, string> wanted)
        {
            return Merge(existing, wanted, null);
        }

        // What the file should hold: everything already in it that is not
        // ours, plus what is wanted. A flag we own that is not wanted goes -
        // that is how a choice put back to "default" stops applying. A wanted
        // name that is not on the allowlist is left out and named in refused.
        public static Dictionary<string, string> Merge(IDictionary<string, string> existing,
                                                       IDictionary<string, string> wanted,
                                                       List<string> refused)
        {
            Dictionary<string, string> merged = Empty();
            if (existing != null)
                foreach (KeyValuePair<string, string> kv in existing)
                    if (!IsOwned(kv.Key)) merged[kv.Key] = kv.Value;

            if (wanted != null)
                foreach (KeyValuePair<string, string> kv in wanted)
                {
                    if (IsAllowed(kv.Key)) merged[kv.Key] = Token(kv.Value);
                    else if (refused != null) refused.Add(kv.Key);
                }
            return merged;
        }

        // ---------- Writing ----------

        // Names in a fixed order, so writing the same flags twice gives the
        // same bytes and a file does not reshuffle every time it is saved.
        public static string ToJson(IDictionary<string, string> flags)
        {
            List<string> names = new List<string>();
            if (flags != null) names.AddRange(flags.Keys);
            if (names.Count == 0) return "{}\r\n";
            names.Sort(StringComparer.Ordinal);

            StringBuilder sb = new StringBuilder("{\r\n");
            for (int n = 0; n < names.Count; n++)
            {
                sb.Append("    ").Append(Quoted(names[n])).Append(": ").Append(Token(flags[names[n]]));
                sb.Append(n < names.Count - 1 ? ",\r\n" : "\r\n");
            }
            return sb.Append("}\r\n").ToString();
        }

        // Null when the file is written, otherwise why not - and then nothing
        // on disk has changed.
        //
        // This is the last thing between a flag and the disk, so the allowlist
        // is held here as well as in Merge. A name that is not on it is only
        // ever written back the way the file already has it: asking to add
        // one, or to change one, refuses the whole write rather than quietly
        // writing the rest. And one that is in the file stays in the file
        // whether or not it was handed in.
        public static string Write(string path, IDictionary<string, string> flags)
        {
            string problem;
            Dictionary<string, string> onDisk = Load(path, out problem);
            if (problem != null) return problem;

            Dictionary<string, string> final = Empty();
            if (flags != null)
                foreach (KeyValuePair<string, string> kv in flags)
                {
                    if (IsAllowed(kv.Key)) { final[kv.Key] = Token(kv.Value); continue; }
                    string was;
                    if (!onDisk.TryGetValue(kv.Key, out was) || was != kv.Value)
                        return kv.Key + " isn't a setting Roblox allows, so nothing was saved";
                }
            foreach (KeyValuePair<string, string> kv in onDisk)
                if (!IsAllowed(kv.Key)) final[kv.Key] = kv.Value;

            string dir = Path.GetDirectoryName(path);
            string temp = path + ".tmp";
            bool madeFolder = false;
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    madeFolder = true;
                }

                // Written whole beside the real file and then swapped in, so a
                // crash or a full disk leaves the old file rather than half a
                // new one. No byte order mark: nothing says Roblox skips one.
                File.WriteAllText(temp, ToJson(final), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                try { if (madeFolder) Directory.Delete(dir); } catch { }
                return Why(ex);
            }
        }

        // ---------- Every installed version ----------

        // Roblox keeps several versions installed and decides at launch which
        // one an account gets, so the choices go into all of them. A folder
        // whose file cannot be read or written is reported and the rest carry
        // on.
        public static LooksFlagsResult Apply(string versionsRoot, LooksChoices choices)
        {
            Dictionary<string, string> wanted = (choices ?? new LooksChoices()).ToFlags();
            LooksFlagsResult result = new LooksFlagsResult();
            foreach (string folder in VersionFolders(versionsRoot))
            {
                string why = Settle(folder, wanted);
                if (why == null) result.Written++;
                else result.Failed.Add(new LooksFailure(Path.GetFileName(folder), why));
            }
            return result;
        }

        // Takes our flags back out and leaves everything else. Clearing is
        // applying no choices at all, so the two cannot drift apart.
        public static LooksFlagsResult Clear(string versionsRoot)
        {
            return Apply(versionsRoot, new LooksChoices());
        }

        static string Settle(string versionFolder, Dictionary<string, string> wanted)
        {
            string path = PathIn(versionFolder);
            string problem;
            Dictionary<string, string> existing = Load(path, out problem);
            if (problem != null) return problem;

            Dictionary<string, string> merged = Merge(existing, wanted);
            return merged.Count == 0 ? Remove(path) : Write(path, merged);
        }

        // With nothing left to say the file goes, and the folder after it if
        // that was all it held. There is nowhere to note whether the folder
        // was ours to begin with - a marker file would be one more thing in
        // Roblox's folder - and to Roblox an empty file, a missing file and a
        // missing folder all mean the same: no flags.
        static string Remove(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                File.Delete(path);
                string dir = Path.GetDirectoryName(path);
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        // Why a file operation failed, in words a person can act on. Windows'
        // own wording for the commonest case - a running client holding the
        // file - names a process nobody recognises.
        public static string Why(Exception ex)
        {
            int code = ex.HResult & 0xFFFF;
            if (ex is IOException && (code == 32 || code == 33))
                return "it is in use - close Roblox and try again";
            if (ex is UnauthorizedAccessException)
                return "Windows won't allow it - the file is read-only or protected";
            return ex.Message;
        }
    }
}
