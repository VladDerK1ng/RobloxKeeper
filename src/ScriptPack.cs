using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RobloxKeeper
{
    // The text in quotes in a Lua script, and where it is - so the pictures a
    // script names can be found, and renamed. Comments and long [[strings]]
    // are read past properly, so a quote inside one is not taken for a
    // string.
    static class ScriptLiterals
    {
        public class Literal
        {
            public int Start, Length;   // the whole literal, quotes and all
            public string Value;        // what it says
            public bool Long;           // a [[long string]]
        }

        public static List<Literal> All(string code)
        {
            List<Literal> found = new List<Literal>();
            if (string.IsNullOrEmpty(code)) return found;
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (c == '-' && i + 1 < n && code[i + 1] == '-')
                {
                    i += 2;
                    int level;
                    if (i < n && code[i] == '[' && LongOpen(code, i, out level))
                    {
                        int end = LongClose(code, i, level);
                        i = end < 0 ? n : end;
                    }
                    else while (i < n && code[i] != '\n') i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int start = i;
                    StringBuilder sb = new StringBuilder();
                    i++;
                    bool closed = false;
                    while (i < n)
                    {
                        char d = code[i];
                        if (d == c) { i++; closed = true; break; }
                        if (d == '\n') break;            // never closed on its line: not a string
                        if (d == '\\' && i + 1 < n) { i = Escape(code, i + 1, sb); continue; }
                        sb.Append(d);
                        i++;
                    }
                    if (closed)
                    {
                        Literal l = new Literal();
                        l.Start = start; l.Length = i - start; l.Value = sb.ToString();
                        found.Add(l);
                    }
                    continue;
                }
                if (c == '[')
                {
                    int level;
                    if (LongOpen(code, i, out level))
                    {
                        int open = i + level + 2;
                        int end = LongClose(code, i, level);
                        if (end < 0) break;
                        string body = code.Substring(open, end - (level + 2) - open);
                        // A line break straight after the opening is not part of it.
                        if (body.StartsWith("\r\n")) body = body.Substring(2);
                        else if (body.StartsWith("\n")) body = body.Substring(1);
                        Literal l = new Literal();
                        l.Start = i; l.Length = end - i; l.Value = body; l.Long = true;
                        found.Add(l);
                        i = end;
                        continue;
                    }
                }
                i++;
            }
            return found;
        }

        // "[[" or "[==[" at i: how many = signs.
        static bool LongOpen(string s, int i, out int level)
        {
            level = 0;
            if (i >= s.Length || s[i] != '[') return false;
            int j = i + 1;
            while (j < s.Length && s[j] == '=') { level++; j++; }
            return j < s.Length && s[j] == '[';
        }

        // Just past the "]]" (or "]==]") that closes the one opening at i, or
        // -1 when it is never closed.
        static int LongClose(string s, int i, int level)
        {
            string close = "]" + new string('=', level) + "]";
            int at = s.IndexOf(close, i + level + 2, StringComparison.Ordinal);
            return at < 0 ? -1 : at + close.Length;
        }

        // One escape after a backslash at i; where reading carries on.
        static int Escape(string s, int i, StringBuilder sb)
        {
            char e = s[i];
            switch (e)
            {
                case 'n': sb.Append('\n'); return i + 1;
                case 't': sb.Append('\t'); return i + 1;
                case 'r': sb.Append('\r'); return i + 1;
                case 'a': sb.Append('\a'); return i + 1;
                case 'b': sb.Append('\b'); return i + 1;
                case 'f': sb.Append('\f'); return i + 1;
                case 'v': sb.Append('\v'); return i + 1;
                case 'x':
                {
                    int v = 0, j = i + 1, k = 0;
                    while (k < 2 && j < s.Length && Uri.IsHexDigit(s[j])) { v = v * 16 + Convert.ToInt32(s[j].ToString(), 16); j++; k++; }
                    sb.Append((char)v);
                    return j;
                }
                case 'z':
                {
                    int j = i + 1;
                    while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                    return j;
                }
                default:
                    if (char.IsDigit(e))
                    {
                        int v = 0, j = i, k = 0;
                        while (k < 3 && j < s.Length && char.IsDigit(s[j])) { v = v * 10 + (s[j] - '0'); j++; k++; }
                        sb.Append((char)v);
                        return j;
                    }
                    sb.Append(e);       // \\ \" \' and a line break
                    return i + 1;
            }
        }

        // The functions whose first argument is a picture's name.
        static readonly string[] PictureCalls = { "find", "waitfor", "waitgone", "clickpicture" };

        // Whether a literal is the picture a picture function is called with:
        // find("egg"), waitfor 'egg' - and not see("egg"), chat("egg"),
        // text:find("egg") or string.find(s, "egg"), which only happen to
        // say the same.
        public static bool IsPictureName(string code, Literal l)
        {
            int j = l.Start - 1;
            while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
            if (j >= 0 && code[j] == '(')
            {
                j--;
                while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
            }
            int end = j + 1;
            while (j >= 0 && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j--;
            string fname = code.Substring(j + 1, end - (j + 1));
            if (Array.IndexOf(PictureCalls, fname) < 0) return false;
            // A name of its own, not a field or method of something.
            while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
            return j < 0 || (code[j] != '.' && code[j] != ':');
        }

        // The code with every picture name (see IsPictureName) saying one of
        // `renames`' keys (any case) saying its value instead. Other text that
        // happens to match - something said, typed or counted - is left as
        // it is, and so are long strings.
        public static string Rename(string code, IDictionary<string, string> renames)
        {
            if (string.IsNullOrEmpty(code) || renames == null || renames.Count == 0) return code;
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> p in renames) map[p.Key] = p.Value;
            StringBuilder sb = new StringBuilder();
            int at = 0;
            foreach (Literal l in All(code))
            {
                string to;
                if (l.Long || !map.TryGetValue(l.Value, out to) || !IsPictureName(code, l)) continue;
                sb.Append(code, at, l.Start - at).Append(ScriptConvert.Quote(to));
                at = l.Start + l.Length;
            }
            sb.Append(code, at, code.Length - at);
            return sb.ToString();
        }
    }

    // What an import did.
    class ScriptImport
    {
        public string Name;                 // the script it made; null when nothing was
        public string Problem;              // why nothing was, in a line
        public readonly List<string> Pictures = new List<string>();     // files put in, or found already there
        public readonly Dictionary<string, string> Renamed = new Dictionary<string, string>();   // a name the script used -> the one it uses now
        public bool KeyLeftOff;             // its key was already something else's here (a shared script never brings its key)
        public bool ScriptRenamed;          // a script by that name was here already
    }

    // A script shared as one .rkscript file: a zip of the script, how it is
    // set to run, and every picture it names (with its painted-out copy).
    //
    // A file from someone else is not trusted. Only the entries this makes
    // are read - nothing in a folder, nothing whose name could reach outside
    // the pictures folder or the scripts folder - and each is read to a size
    // that no real one comes near.
    static class ScriptPack
    {
        public const string EXT = ".rkscript";
        const string CODE = "script.lua";
        const string SETTINGS = "settings.json";
        const string PICTURES = "pictures.json";
        const int MAX_PICTURE_BYTES = 20 * 1024 * 1024;
        const int MAX_SMALL_BYTES = 256 * 1024;
        const int MAX_PICTURES = 200;

        // All that is read from one file, together: a tiny zip can say it
        // holds 200 pictures of 20 MB, which kept in memory would be 4 GB.
        public const int MAX_TOTAL_BYTES = 64 * 1024 * 1024;

        // ---------- export ----------

        // Null when it was written; otherwise why not. shelf: every picture
        // this PC knows, as a running script would find them.
        public static string Export(string zipPath, ScriptStore store, string name, string templatesDir, PictureShelf shelf)
        {
            string code = store == null ? null : store.Load(name);
            if (code == null) return "There's no script called \"" + name + "\" to share.";
            ScriptSettings settings = store.Settings(name);

            // Each picture once, with the name the script calls it by.
            List<string[]> pictures = new List<string[]>();      // name, file, mask
            List<string> files = new List<string>();
            foreach (ScriptLiterals.Literal l in ScriptLiterals.All(code))
            {
                if (l.Long) continue;
                string file, mask;
                if (!ScriptPictures.Resolve(shelf, templatesDir, l.Value, out file, out mask)) continue;
                if (!SafeName(file) || !File.Exists(Path.Combine(templatesDir, file))) continue;
                if (mask != null && (!SafeName(mask) || !File.Exists(Path.Combine(templatesDir, mask)))) mask = null;
                bool seen = false;
                foreach (string[] p in pictures) if (string.Equals(p[0], l.Value, StringComparison.OrdinalIgnoreCase)) seen = true;
                if (seen) continue;
                pictures.Add(new string[] { l.Value, file, mask });
                if (!files.Contains(file)) files.Add(file);
                if (mask != null && !files.Contains(mask)) files.Add(mask);
            }

            StringBuilder list = new StringBuilder("[");
            for (int i = 0; i < pictures.Count; i++)
            {
                if (i > 0) list.Append(',');
                list.Append("{\"name\":").Append(Json.Quote(pictures[i][0]))
                    .Append(",\"file\":").Append(Json.Quote(pictures[i][1]));
                if (pictures[i][2] != null) list.Append(",\"mask\":").Append(Json.Quote(pictures[i][2]));
                list.Append('}');
            }
            list.Append(']');

            string temp = zipPath + ".new";
            try
            {
                using (FileStream fs = File.Create(temp))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    Put(zip, CODE, Encoding.UTF8.GetBytes(code));
                    string s = settings.ToJson();
                    s = "{\"name\":" + Json.Quote(name) + "," + s.Substring(1);
                    Put(zip, SETTINGS, Encoding.UTF8.GetBytes(s));
                    Put(zip, PICTURES, Encoding.UTF8.GetBytes(list.ToString()));
                    foreach (string f in files) Put(zip, f, File.ReadAllBytes(Path.Combine(templatesDir, f)));
                }
                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(temp, zipPath);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return "Couldn't write " + Path.GetFileName(zipPath) + ": " + ex.Message;
            }
        }

        static void Put(ZipArchive zip, string name, byte[] bytes)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open()) s.Write(bytes, 0, bytes.Length);
        }

        // ---------- import ----------

        // A bare file name and nothing else: no folder, no "..", no drive, no
        // character Windows refuses - so nothing read from a zip can be
        // written anywhere but the folder it is meant for.
        public static bool SafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 200) return false;
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return false;
            if (name.Contains("..") || name.StartsWith(".") || name != name.Trim() || name.EndsWith(".")) return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            try { if (Path.IsPathRooted(name) || Path.GetFileName(name) != name) return false; }
            catch { return false; }
            return true;
        }

        static bool IsPicture(string name)
        {
            return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && SafeName(name);
        }

        // Makes a script from a .rkscript file. Its pictures go into
        // templatesDir - renamed where that name is already another picture
        // here, with the script's code changed to match - and the script is
        // renamed if one by its name is here already. keyProblem: the app's
        // one key check, so a key that is already something else's here is
        // left off rather than taken; null keeps no key.
        public static ScriptImport Import(string zipPath, ScriptStore store, string templatesDir, PictureShelf shelf,
                                          Func<byte, string> keyProblem)
        {
            ScriptImport r = new ScriptImport();
            string code = null, settingsText = null, picturesText = null;
            Dictionary<string, byte[]> pngs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Budget left = new Budget(MAX_TOTAL_BYTES);
            try
            {
                using (FileStream fs = File.OpenRead(zipPath))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        // Anything that is not one of ours, by name, is left
                        // where it is - read, never written.
                        string n = e.FullName;
                        if (!SafeName(n)) continue;
                        if (n == CODE) code = Text(e, ScriptStore.MAX_CODE_BYTES, left);
                        else if (n == SETTINGS) settingsText = Text(e, MAX_SMALL_BYTES, left);
                        else if (n == PICTURES) picturesText = Text(e, MAX_SMALL_BYTES, left);
                        else if (IsPicture(n) && pngs.Count < MAX_PICTURES)
                        {
                            byte[] b = Bytes(e, MAX_PICTURE_BYTES, left);
                            if (b != null) pngs[n] = b;
                        }
                        if (left.Over) break;
                    }
                }
            }
            catch (Exception ex)
            {
                r.Problem = "That isn't a script RobloxKeeper can read (" + ex.Message + ").";
                return r;
            }
            if (left.Over)
            {
                r.Problem = "That file is too big - what's in it comes to more than " + MAX_TOTAL_BYTES / (1024 * 1024) + " MB.";
                return r;
            }
            if (code == null) { r.Problem = "That file has no script in it."; return r; }

            object s = Json.Parse(settingsText);
            ScriptSettings settings = ScriptSettings.FromJson(settingsText);
            // Someone else's script: it doesn't run until you have read it
            // and said you trust it, whatever its file says, and it starts on
            // the Roblox window in front rather than the screen or an
            // account of theirs. Its key isn't brought either.
            settings.Trusted = false;
            settings.PlaysOn = ScriptSettings.FRONT;
            string name = Json.Str(s, "name");
            if (ScriptStore.NameProblem(name) != null) name = "Shared script";
            string free = name;
            for (int k = 2; store.NameProblem(free, null) != null; k++)
            {
                string tail = " " + k;
                free = (name.Length + tail.Length > ScriptStore.MAX_NAME ? name.Substring(0, ScriptStore.MAX_NAME - tail.Length) : name) + tail;
                if (k > 999) { r.Problem = "Couldn't find a free name for the script."; return r; }
            }
            r.ScriptRenamed = free != name;
            name = free;

            // The pictures, each under a name nothing here is called already.
            Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Directory.CreateDirectory(templatesDir);
                foreach (object p in Json.List(Json.Parse(picturesText)))
                {
                    string said = Json.Str(p, "name"), file = Json.Str(p, "file"), mask = Json.Str(p, "mask");
                    if (string.IsNullOrEmpty(said) || !IsPicture(file ?? "") || !pngs.ContainsKey(file)) continue;
                    if (mask != null && (!IsPicture(mask) || !pngs.ContainsKey(mask))) mask = null;
                    string now = PlacePicture(templatesDir, shelf, said, file, mask, pngs, r);
                    if (now != null && !string.Equals(now, said, StringComparison.Ordinal)) renames[said] = now;
                }
            }
            catch (Exception ex)
            {
                r.Problem = "Couldn't put its pictures in " + templatesDir + ": " + ex.Message;
                return r;
            }

            code = ScriptLiterals.Rename(code, renames);
            foreach (KeyValuePair<string, string> p in renames) r.Renamed[p.Key] = p.Value;

            // Said only when it clashed here, as before: the window says so.
            if (settings.Key != 0 && (keyProblem == null || keyProblem(settings.Key) != null)) r.KeyLeftOff = true;
            settings.Key = 0;

            // The settings go first, so the script is never on disk without
            // them - with none it would count as made here, and trusted.
            if (!store.SaveSettings(name, settings)) { r.Problem = "Couldn't save the script in " + store.Folder + "."; return r; }
            string why = store.Create(name, code);
            if (why != null) { r.Problem = why; return r; }
            r.Name = name;
            return r;
        }

        // Puts one picture in the folder. What the script should call it now,
        // or null when it couldn't be put in.
        static string PlacePicture(string folder, PictureShelf shelf, string said, string file, string mask,
                                   Dictionary<string, byte[]> pngs, ScriptImport r)
        {
            byte[] bytes = pngs[file];
            string path = Path.Combine(folder, file);

            // The very same picture is here already: nothing to copy. The
            // script still has to find that file by the name it uses.
            if (File.Exists(path) && Same(File.ReadAllBytes(path), bytes))
            {
                // A painted-out copy already here is the one you use with it
                // - left as it is.
                if (mask != null)
                {
                    string m = MaskFor(file, mask);
                    if (!File.Exists(Path.Combine(folder, m))) File.WriteAllBytes(Path.Combine(folder, m), pngs[mask]);
                }
                r.Pictures.Add(file);
                return FindsIt(shelf, folder, said, file) ? said : Path.GetFileNameWithoutExtension(file);
            }

            // Free: no file by that name, and the name the script uses finds
            // nothing here yet.
            string found, fm;
            bool nameTaken = ScriptPictures.Resolve(shelf, folder, said, out found, out fm) || TakenInFolder(folder, said);
            if (!File.Exists(path) && !nameTaken)
            {
                File.WriteAllBytes(path, bytes);
                if (mask != null) WriteNew(folder, MaskFor(file, mask), pngs[mask]);
                r.Pictures.Add(file);
                return said;
            }

            // Taken: called "<name> 2" (3, ...) instead - the file and its
            // painted-out copy both.
            string stem = Path.GetFileNameWithoutExtension(file);
            string label = MacroStep.PictureLabel(file);
            bool byLabel = string.Equals(said, label, StringComparison.OrdinalIgnoreCase);
            string tail = stem.Substring(label.Length);         // "-20261110-213000", or ""
            for (int k = 2; k < 1000; k++)
            {
                string newLabel = label + " " + k;
                string newFile = newLabel + tail + ".png";
                if (File.Exists(Path.Combine(folder, newFile))) continue;
                if (ScriptPictures.Resolve(shelf, folder, newLabel, out found, out fm) || TakenInFolder(folder, newLabel)) continue;
                File.WriteAllBytes(Path.Combine(folder, newFile), bytes);
                if (mask != null) WriteNew(folder, MaskFor(newFile, mask), pngs[mask]);
                r.Pictures.Add(newFile);
                return byLabel ? newLabel : Path.GetFileNameWithoutExtension(newFile);
            }
            return null;
        }

        // The name a painted-out copy is put in under: always "<its
        // picture>-mask-....png", the name the picture tool gives one and
        // the one a script finds beside its picture - whatever the file
        // called it. Taken as it was, a mask called like a picture already
        // here would be written over that picture.
        static string MaskFor(string file, string mask)
        {
            int cut = mask.IndexOf("-mask-", StringComparison.OrdinalIgnoreCase);
            string tail = cut >= 0 ? mask.Substring(cut) : "-mask-1.png";
            string name = Path.GetFileNameWithoutExtension(file) + tail;
            return SafeName(name) && IsPicture(name) ? name : Path.GetFileNameWithoutExtension(file) + "-mask-1.png";
        }

        // Writes a file under this name, or - when another file is called
        // that already - under "<name>_2.png" (3, ...): nothing here is ever
        // written over. The same bytes already there count as written. The
        // "_" sorts after ".", so the new one is the newest a script finds.
        static void WriteNew(string folder, string name, byte[] bytes)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            for (int k = 1; k < 1000; k++)
            {
                string path = Path.Combine(folder, k == 1 ? name : stem + "_" + k + ".png");
                if (!File.Exists(path)) { File.WriteAllBytes(path, bytes); return; }
                if (Same(File.ReadAllBytes(path), bytes)) return;
            }
        }

        // Whether a name finds `file` among this PC's pictures with that file
        // now in the folder.
        static bool FindsIt(PictureShelf shelf, string folder, string said, string file)
        {
            PictureShelf now = PictureShelf.For(null, null, folder);
            if (shelf != null) { string f, m; if (shelf.Find(said, out f, out m)) return string.Equals(f, file, StringComparison.OrdinalIgnoreCase); }
            string ff, mm;
            return now.Find(said, out ff, out mm) && string.Equals(ff, file, StringComparison.OrdinalIgnoreCase);
        }

        // A picture in the folder already called this, by its file's name or
        // what it is called.
        static bool TakenInFolder(string folder, string name)
        {
            string f, m;
            return PictureShelf.For(null, null, folder).Find(name, out f, out m);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // What may still be read from the file. Over once anything would
        // take it past MAX_TOTAL_BYTES: the import stops there.
        class Budget
        {
            public long Left;
            public bool Over;
            public Budget(long left) { Left = left; }
        }

        static byte[] Bytes(ZipArchiveEntry e, int max, Budget left)
        {
            if (e.Length > max || e.Length < 0) return null;
            if (e.Length > left.Left) { left.Over = true; return null; }
            using (Stream s = e.Open())
            using (MemoryStream ms = new MemoryStream())
            {
                // Read to the cap, whatever the entry says its size is.
                byte[] buf = new byte[81920];
                int got;
                while ((got = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + got > left.Left) { left.Over = true; return null; }
                    if (ms.Length + got > max) return null;
                    ms.Write(buf, 0, got);
                }
                left.Left -= ms.Length;
                return ms.ToArray();
            }
        }

        static string Text(ZipArchiveEntry e, int max, Budget left)
        {
            byte[] b = Bytes(e, max, left);
            if (b == null) return null;
            string t = Encoding.UTF8.GetString(b);
            return t.Length > 0 && t[0] == (char)0xFEFF ? t.Substring(1) : t;
        }
    }
}
