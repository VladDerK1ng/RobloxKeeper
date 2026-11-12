using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // A font on this PC that Roblox can be given.
    class FontChoice
    {
        public string Name;     // as Windows lists it, without "(TrueType)"
        public string File;     // full path
    }

    // Choosing a font for Roblox, and making the mod pack that puts it there.
    //
    // How Roblox is wired, read from an installed client rather than
    // assumed: content\fonts holds the font files, and each file in
    // content\fonts\families says, for one family, which file or which
    // online asset every weight comes from. Swapping the font files alone
    // would leave the weights that come from online assets as they were. So
    // the pack carries the chosen font once, as CustomFont, and a copy of
    // every families file with each weight pointed at it - the way
    // Bloxstrap's custom font works. It is an ordinary pack: it goes through
    // LooksMods, with the same checks, the same backup and the same undo.
    static class FontPick
    {
        public const string PACK = "My font";
        public const string FONTS = "content\\fonts", FAMILIES = "content\\fonts\\families";
        public const string CUSTOM = "CustomFont";

        // Families left as Roblox has them, so that writing this app has no
        // letters for still shows: other scripts fall back to these.
        static readonly string[] LeftAlone = { "NotoSansCJKFallback.json" };

        public static bool Skips(string jsonFileName)
        {
            foreach (string s in LeftAlone)
                if (string.Equals(s, jsonFileName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------- reading a font file ----------

        static int U16(byte[] b, int at) { return (b[at] << 8) | b[at + 1]; }
        static long U32(byte[] b, int at) { return ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3]; }

        // The font's own name for itself, from inside the file. Null when
        // the file is not a font Roblox can be given; why says what it is
        // instead. Never throws, whatever the bytes are.
        public static string NameIn(byte[] f, out string why)
        {
            string family;
            return NameIn(f, out family, out why);
        }

        // The same, and the family it belongs to - "Arial" for "Arial Bold".
        public static string NameIn(byte[] f, out string family, out string why)
        {
            why = null;
            family = null;
            try
            {
                if (f == null || f.Length < 12) { why = "it's too small to be a font"; return null; }
                string tag = Encoding.ASCII.GetString(f, 0, 4);
                if (tag == "ttcf") { why = "it's a font collection (.ttc) with several fonts in one file"; return null; }
                if (tag == "wOFF" || tag == "wOF2") { why = "it's a web font (.woff), which Roblox can't read"; return null; }
                bool trueType = f[0] == 0 && f[1] == 1 && f[2] == 0 && f[3] == 0;
                if (!trueType && tag != "OTTO" && tag != "true") { why = "it's not a TrueType or OpenType font"; return null; }

                int tables = U16(f, 4);
                long nameAt = -1;
                for (int i = 0; i < tables; i++)
                {
                    int rec = 12 + i * 16;
                    if (rec + 16 > f.Length) break;
                    if (Encoding.ASCII.GetString(f, rec, 4) != "name") continue;
                    nameAt = U32(f, rec + 8);
                }
                if (nameAt < 0 || nameAt + 6 > f.Length) { why = "it has no name inside it"; return null; }

                int n = (int)nameAt, count = U16(f, n + 2), strings = n + U16(f, n + 4);
                string full = null;
                int fullRank = -1, familyRank = -1;
                for (int i = 0; i < count; i++)
                {
                    int rec = n + 6 + i * 12;
                    if (rec + 12 > f.Length) break;
                    int platform = U16(f, rec), encoding = U16(f, rec + 2), language = U16(f, rec + 4), id = U16(f, rec + 6);
                    int len = U16(f, rec + 8), at = strings + U16(f, rec + 10);
                    if (id != 1 && id != 4) continue;
                    if (len <= 0 || at < 0 || at + len > f.Length) continue;

                    string text;
                    int rank;
                    if (platform == 3 || platform == 0)
                    {
                        text = Encoding.BigEndianUnicode.GetString(f, at, len);
                        rank = platform == 3 && language == 0x409 ? 3 : 2;      // Windows, English first
                    }
                    else if (platform == 1 && encoding == 0)
                    {
                        text = Encoding.ASCII.GetString(f, at, len);
                        rank = 1;
                    }
                    else continue;
                    text = text.Trim('\0', ' ');
                    if (text.Length == 0) continue;
                    if (id == 4 && rank > fullRank) { full = text; fullRank = rank; }
                    if (id == 1 && rank > familyRank) { family = text; familyRank = rank; }
                }
                string name = full ?? family;
                if (name == null) why = "it has no name inside it";
                return name;
            }
            catch { why = "it couldn't be read as a font"; return null; }
        }

        public static string NameOf(string path, out string why)
        {
            why = null;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) { why = "the file is no longer there"; return null; }
                if (fi.Length > 40 * 1024 * 1024) { why = "it's bigger than 40 MB"; return null; }
                return NameIn(File.ReadAllBytes(path), out why);
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        // ---------- the fonts on this PC ----------

        // Windows keeps a list of installed fonts as name -> file. Names end
        // in "(TrueType)" or "(OpenType)"; files are bare names when they sit
        // in the Fonts folder and full paths when installed for one user.
        // Only single .ttf and .otf files are kept, sorted by name.
        public static List<FontChoice> FromRegistry(IEnumerable<KeyValuePair<string, string>> entries, string fontsFolder)
        {
            Dictionary<string, FontChoice> byName = new Dictionary<string, FontChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> e in entries)
            {
                if (string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.Value)) continue;
                string ext = "";
                try { ext = Path.GetExtension(e.Value).ToLowerInvariant(); } catch { continue; }
                if (ext != ".ttf" && ext != ".otf") continue;
                string name = Regex.Replace(e.Key, @"\s*\((TrueType|OpenType)\)\s*$", "", RegexOptions.IgnoreCase).Trim();
                if (name.Length == 0) continue;
                string file = e.Value;
                try { if (!Path.IsPathRooted(file)) file = Path.Combine(fontsFolder, file); } catch { continue; }
                FontChoice c = new FontChoice();
                c.Name = name;
                c.File = file;
                byName[name] = c;       // a font installed for this user wins over the PC-wide one of the same name
            }
            List<FontChoice> list = new List<FontChoice>(byName.Values);
            list.Sort(delegate(FontChoice a, FontChoice b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public static List<FontChoice> Installed()
        {
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
            const string KEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
            foreach (RegistryKey root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using (RegistryKey k = root.OpenSubKey(KEY))
                    {
                        if (k == null) continue;
                        foreach (string name in k.GetValueNames())
                        {
                            string file = k.GetValue(name) as string;
                            if (file != null) entries.Add(new KeyValuePair<string, string>(name, file));
                        }
                    }
                }
                catch { }
            }
            List<FontChoice> all = FromRegistry(entries, Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            all.RemoveAll(delegate(FontChoice c) { try { return !File.Exists(c.File); } catch { return true; } });
            return all;
        }

        // ---------- the pack ----------

        // One families file with every weight pointed at the custom font.
        // The rest of the file - names, weights, styles - is left exactly as
        // it was, so Roblox finds every face it asks for.
        public static string Rewrite(string familyJson, string customFileName)
        {
            return Regex.Replace(familyJson ?? "", "(\"assetId\"\\s*:\\s*\")[^\"]*(\")",
                                 "$1rbxasset://fonts/" + customFileName + "$2");
        }

        // The pack's files, written under a folder laid out as Roblox's is:
        // the font, and a rewritten copy of each families file found in the
        // installed client. Null when it is made; otherwise why not.
        public static string Build(string fontFile, string versionFolder, string packFolder, out int families)
        {
            families = 0;
            string why;
            if (NameOf(fontFile, out why) == null) return why;
            string from = Path.Combine(versionFolder ?? "", FAMILIES);
            if (!Directory.Exists(from)) return "Couldn't find Roblox's font list, so nothing was changed";

            string ext = Path.GetExtension(fontFile).ToLowerInvariant() == ".otf" ? ".otf" : ".ttf";
            string custom = CUSTOM + ext;
            Directory.CreateDirectory(Path.Combine(packFolder, FAMILIES));
            File.Copy(fontFile, Path.Combine(packFolder, FONTS, custom), true);
            foreach (string json in Directory.GetFiles(from, "*.json"))
            {
                string name = Path.GetFileName(json);
                if (Skips(name)) continue;
                string text = File.ReadAllText(json);
                if (text.IndexOf("\"assetId\"", StringComparison.Ordinal) < 0) continue;
                File.WriteAllText(Path.Combine(packFolder, FAMILIES, name), Rewrite(text, custom), new UTF8Encoding(false));
                families++;
            }
            return families == 0 ? "Roblox's font list had nothing to change" : null;
        }

        // The newest installed version that has a list of fonts.
        public static string VersionWithFonts(string versionsRoot)
        {
            string best = null;
            DateTime newest = DateTime.MinValue;
            foreach (string folder in LooksFlags.VersionFolders(versionsRoot))
            {
                if (!Directory.Exists(Path.Combine(folder, FAMILIES))) continue;
                DateTime t = Directory.GetLastWriteTimeUtc(folder);
                if (best == null || t > newest) { best = folder; newest = t; }
            }
            return best;
        }

        // Makes the pack from this font and puts it in the library, ticked,
        // in place of any font pack made before. Null when it is in;
        // otherwise why not. Nothing in Roblox changes until packs are applied.
        public static string Use(LooksMods mods, string fontFile, string versionsRoot)
        {
            string version = VersionWithFonts(versionsRoot);
            if (version == null) return "Roblox isn't installed where it usually is";
            string temp = Path.Combine(Path.GetTempPath(), "rk-font-" + Guid.NewGuid().ToString("N"));
            try
            {
                int families;
                string why = Build(fontFile, version, temp, out families);
                if (why != null) return why;
                if (mods.Find(PACK) != null)
                {
                    why = mods.Remove(PACK);
                    if (why != null) return why;
                }
                DropLeftover(mods);
                why = mods.Import(temp, PACK);
                if (why != null) return why;
                mods.Save();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        // Takes the font pack out of the library. True when there was one.
        public static bool Drop(LooksMods mods)
        {
            bool listed = mods.Find(PACK) != null;
            if (listed)
            {
                mods.Remove(PACK);
                mods.Save();
            }
            return DropLeftover(mods) || listed;
        }

        // The font pack's folder with no line for it in the library's list.
        // Seen live: the font was applied from its window while the Looks
        // page behind it still held the list as it was before, and the
        // page's next save wrote that older list back. The folder is this
        // app's own and is made afresh from the font each time, so it is
        // simply removed. True when there was one.
        static bool DropLeftover(LooksMods mods)
        {
            try
            {
                string folder = Path.Combine(mods.Library, PACK);
                if (mods.Find(PACK) != null || !Directory.Exists(folder)) return false;
                foreach (string f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(folder, true);
                return true;
            }
            catch { return false; }
        }

        // Whether a font of the user's is in Roblox right now, asked of
        // Roblox's own folder rather than of any list.
        public static bool InRoblox(string versionsRoot)
        {
            foreach (string folder in LooksFlags.VersionFolders(versionsRoot))
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, FONTS, CUSTOM + ".ttf")) || File.Exists(Path.Combine(folder, FONTS, CUSTOM + ".otf"))) return true;
                }
                catch { }
            }
            return false;
        }
    }
}
