using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RobloxKeeper
{
    class LooksModPack
    {
        public string Name;
        public bool Enabled;
    }

    // One file a pack supplies, and whose copy of it ends up in Roblox.
    class LooksModFile
    {
        public string Path;
        public string WonBy;
        public bool Wins;
    }

    class LooksModPreview
    {
        public string Pack;
        public readonly List<LooksModFile> Files = new List<LooksModFile>();

        // Why the pack would be left out. A pack with anything here supplies
        // nothing at all.
        public readonly List<string> Problems = new List<string>();
    }

    class LooksModsResult
    {
        public int Versions;     // version folders gone through
        public int Replaced;     // Roblox's files now holding a pack's copy
        public int Added;        // files put in that Roblox does not ship
        public int BackedUp;     // originals copied aside for the first time
        public int Restored;     // originals put back
        public int Removed;      // added files taken out again

        public readonly List<LooksFailure> Failed = new List<LooksFailure>();
    }

    // Mod packs: swapping Roblox's content files - textures, sounds, fonts,
    // cursors - for a pack's own, with every original kept so it can all be
    // undone.
    //
    // A pack is a folder in the library whose files sit where they would sit
    // in a Roblox version folder. Only the three content folders can be
    // reached that way, and nothing that runs: a "texture pack" carrying an
    // exe is not a texture pack, so the whole pack is refused rather than
    // just that file.
    //
    // Roblox installs every update into a new version folder, with the
    // original files back in it. So the packs are copied into every installed
    // version, and NeedsReapply says when a new one has turned up.
    //
    // The undo does not depend on the library. What was changed is written
    // down beside the saved originals, per version, before anything is
    // overwritten - so Restore still works after a pack has been deleted, and
    // after a crash half way through.
    class LooksMods
    {
        public const string MANIFEST = "mods.txt";
        public const string BACKUP_MANIFEST = "manifest.txt";

        // A copy still being made. An original is only trusted once it has its
        // real name, so a copy cut short is never mistaken for one.
        const string PART = ".rk-part";

        const string FLAGS_FOLDER = "ClientSettings";

        public static readonly string[] Folders = { "content", "ExtraContent", "PlatformContent" };

        // File types that run. Kept without their dots: the app's own exe is
        // checked for carrying script file names, because that is one of the
        // things antivirus reads as a dropper, and a list of extensions to
        // refuse should not be what trips it.
        public static readonly string[] Blocked = "exe dll bat cmd ps1 vbs js msi scr com lnk".Split(' ');

        public readonly string Library;

        // In order: where two packs supply the same file, the later one wins.
        public readonly List<LooksModPack> Packs = new List<LooksModPack>();

        public LooksMods(string libraryFolder)
        {
            Library = libraryFolder;
            Load();
        }

        public bool AnyEnabled
        {
            get
            {
                foreach (LooksModPack p in Packs) if (p.Enabled) return true;
                return false;
            }
        }

        // The packs the last Apply put into Roblox, in the order they had
        // then. A tick is a wish until Apply is pressed: what a new Roblox
        // version is given by itself is this, never whatever is ticked now -
        // so a pack just added, and ticked, stays out of Roblox until asked.
        readonly List<string> applied = new List<string>();

        public bool AnyApplied { get { return applied.Count > 0; } }
        public IList<string> Applied { get { return applied; } }

        // Roblox has been put back: nothing is applied any more.
        public bool ForgetApplied()
        {
            applied.Clear();
            return Save();
        }

        public LooksModPack Find(string name)
        {
            foreach (LooksModPack p in Packs)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // ---------- The library ----------

        // The folders are the packs; the manifest only remembers their order
        // and which are on. So a pack somebody dropped in by hand shows up
        // (off, at the end), and one they deleted by hand is simply gone.
        public void Load()
        {
            Packs.Clear();
            applied.Clear();
            List<string> onDisk = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(Library) || !Directory.Exists(Library)) return;
                foreach (string d in Directory.GetDirectories(Library))
                    if (!IsLink(d)) onDisk.Add(Path.GetFileName(d));
            }
            catch { return; }
            onDisk.Sort(StringComparer.OrdinalIgnoreCase);

            try
            {
                string manifest = Path.Combine(Library, MANIFEST);
                if (File.Exists(manifest))
                {
                    string[] lines = File.ReadAllLines(manifest);
                    foreach (LooksModPack p in Parse(lines))
                    {
                        int at = IndexOf(onDisk, p.Name);
                        if (at < 0 || Find(p.Name) != null) continue;
                        // The folder's own spelling, not the manifest's.
                        p.Name = onDisk[at];
                        Packs.Add(p);
                    }
                    // A pack that has gone from the library cannot be applied again.
                    foreach (string line in lines)
                    {
                        if (!line.StartsWith("applied=")) continue;
                        int at = IndexOf(onDisk, line.Substring(8).Trim());
                        if (at >= 0 && IndexOf(applied, onDisk[at]) < 0) applied.Add(onDisk[at]);
                    }
                }
            }
            catch { }

            foreach (string name in onDisk)
            {
                if (Find(name) != null) continue;
                LooksModPack p = new LooksModPack();
                p.Name = name;
                Packs.Add(p);
            }
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(Library);
                File.WriteAllLines(Path.Combine(Library, MANIFEST), ToLines());
                return true;
            }
            catch { return false; }
        }

        // One line a pack, in order: pack=1|Name, the 1 meaning it is on.
        public string[] ToLines()
        {
            List<string> lines = new List<string>();
            foreach (LooksModPack p in Packs)
                lines.Add("pack=" + (p.Enabled ? "1" : "0") + "|" + p.Name);
            foreach (string name in applied) lines.Add("applied=" + name);
            return lines.ToArray();
        }

        public static List<LooksModPack> Parse(IEnumerable<string> lines)
        {
            List<LooksModPack> packs = new List<LooksModPack>();
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                if (line.Substring(0, eq).Trim() != "pack") continue;
                string val = line.Substring(eq + 1);
                int bar = val.IndexOf('|');
                if (bar < 0) continue;
                LooksModPack p = new LooksModPack();
                p.Enabled = val.Substring(0, bar).Trim() == "1";
                p.Name = val.Substring(bar + 1).Trim();
                if (NameProblem(p.Name) == null) packs.Add(p);
            }
            return packs;
        }

        public bool SetEnabled(string name, bool enabled)
        {
            LooksModPack p = Find(name);
            if (p == null) return false;
            p.Enabled = enabled;
            return Save();
        }

        // Moves a pack to a place in the order; further down wins.
        public bool Move(string name, int toIndex)
        {
            LooksModPack p = Find(name);
            if (p == null) return false;
            Packs.Remove(p);
            if (toIndex < 0) toIndex = 0;
            if (toIndex > Packs.Count) toIndex = Packs.Count;
            Packs.Insert(toIndex, p);
            return Save();
        }

        // Null when the pack is gone from the library, otherwise why not.
        // Nothing in Roblox changes until the next Apply or Restore.
        public string Remove(string name)
        {
            LooksModPack p = Find(name);
            if (p == null) return "there is no pack called " + name;
            try
            {
                DeleteTree(Path.Combine(Library, p.Name));
            }
            catch (Exception ex) { return LooksFlags.Why(ex); }
            Packs.Remove(p);
            int was = IndexOf(applied, p.Name);
            if (was >= 0) applied.RemoveAt(was);
            Save();
            return null;
        }

        public string Import(string source)
        {
            string name = "";
            try
            {
                // Only a zip has an extension to drop. A folder called
                // "Pack v1.2" is called exactly that.
                string trimmed = (source ?? "").TrimEnd('\\', '/');
                name = Directory.Exists(trimmed) ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);
            }
            catch { }
            return Import(source, name);
        }

        // Copies a folder or a zip into the library as a pack, switched on and
        // last in the order. Null when it is in, otherwise why not - and then
        // the library is as it was. Everything is checked before the first
        // file is copied, so a refused pack leaves nothing behind.
        public string Import(string source, string name)
        {
            name = (name ?? "").Trim();
            string bad = NameProblem(name);
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(Library)) return "there is no library folder to keep it in";

            string dest = Path.Combine(Library, name);
            if (Find(name) != null || Directory.Exists(dest) || File.Exists(dest))
                return "a pack called " + name + " is already in the library";

            string why;
            try
            {
                if (Directory.Exists(source)) why = ImportFolder(source, dest);
                else if (File.Exists(source) && string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
                    why = ImportZip(source, dest);
                else why = "there is no folder or zip file there";
            }
            catch (InvalidDataException) { why = "that isn't a working zip file"; }
            catch (Exception ex) { why = LooksFlags.Why(ex); }

            if (why != null)
            {
                try { DeleteTree(dest); } catch { }
                return why;
            }

            LooksModPack p = new LooksModPack();
            p.Name = name;
            p.Enabled = true;
            Packs.Add(p);
            Save();
            return null;
        }

        const string NO_FILES = "there are no content files in it";

        string ImportFolder(string source, string dest)
        {
            if (IsLink(source)) return "that folder is a link, and a pack may only hold real files";

            // Zipping a folder, or unzipping one, tends to leave everything a
            // level down inside a folder named after the pack.
            string root = source;
            string[] top = Directory.GetFileSystemEntries(source);
            if (top.Length == 1 && Directory.Exists(top[0]) && !IsLink(top[0]) && IsWrapper(Path.GetFileName(top[0])))
                root = top[0];

            List<string> files = new List<string>();
            List<string> problems = new List<string>();
            Walk(root, "", files, problems);
            if (problems.Count > 0) return Summary(problems);
            if (files.Count == 0) return NO_FILES;

            foreach (string rel in files)
            {
                string to = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(Path.Combine(root, rel), to, false);
                Writable(to);
            }
            return null;
        }

        // Each entry is checked and written out by hand. The framework's own
        // "extract everything" trusts the names inside the zip, and a name
        // with .. in it would be written outside the library.
        string ImportZip(string source, string dest)
        {
            using (FileStream file = File.OpenRead(source))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                List<ZipArchiveEntry> entries = new List<ZipArchiveEntry>();
                List<string> paths = new List<string>();
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string full = e.FullName.Replace('/', '\\');
                    if (full.Length == 0 || full.EndsWith("\\")) continue;   // a folder, not a file
                    entries.Add(e);
                    paths.Add(full);
                }
                Unwrap(paths);

                List<string> problems = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    string why = WhyNot(path);
                    if (why != null) problems.Add(path + " - " + why);
                    else if (!seen.Add(path)) problems.Add(path + " - it is in the zip twice");
                }
                if (problems.Count > 0) return Summary(problems);
                if (entries.Count == 0) return NO_FILES;

                for (int i = 0; i < entries.Count; i++)
                {
                    string to = Path.Combine(dest, paths[i]);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    using (Stream from = entries[i].Open())
                    using (FileStream into = new FileStream(to, FileMode.CreateNew))
                        from.CopyTo(into);
                }
            }
            return null;
        }

        // A folder that could be nothing but packaging: not one of the content
        // folders, and not a name that means something to a path.
        static bool IsWrapper(string name)
        {
            if (name == "." || name == ".." || name.IndexOf(':') >= 0) return false;
            if (string.Equals(name, FLAGS_FOLDER, StringComparison.OrdinalIgnoreCase)) return false;
            return IndexOf(Folders, name) < 0;
        }

        static void Unwrap(List<string> paths)
        {
            string wrapper = null;
            foreach (string p in paths)
            {
                int cut = p.IndexOf('\\');
                if (cut < 1) return;
                string top = p.Substring(0, cut);
                if (wrapper == null) wrapper = top;
                else if (!string.Equals(wrapper, top, StringComparison.OrdinalIgnoreCase)) return;
            }
            if (wrapper == null || !IsWrapper(wrapper)) return;
            for (int i = 0; i < paths.Count; i++) paths[i] = paths[i].Substring(wrapper.Length + 1);
        }

        static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a pack needs a name";
            if (name == "." || name == ".." || name.TrimEnd(' ', '.') != name)
                return "that name cannot be used for a folder";
            // The bar is what the manifest puts between "on" and the name.
            if (name.IndexOf('|') >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "that name has a character a folder name cannot have";
            return null;
        }

        // ---------- What a pack may hold ----------

        // Null when a file at this path, relative to the pack, is acceptable;
        // otherwise why it is not. Everything that comes from outside goes
        // through here: a pack folder's files, a zip's entries, and the paths
        // read back from the backup record - which sits in a folder anybody
        // can edit and is about to be used to overwrite and delete files.
        public static string WhyNot(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return "it has no name";
            // A colon is a drive letter or a hidden stream, never a file name.
            if (relativePath.IndexOf(':') >= 0 || relativePath[0] == '\\' || relativePath[0] == '/')
                return "it is a full path, and a pack's files must sit inside the pack";

            string[] parts = relativePath.Split('\\', '/');
            char[] illegal = Path.GetInvalidFileNameChars();
            foreach (string part in parts)
            {
                if (part == "." || part == "..") return "its path goes outside the pack";
                if (part.Length == 0) return "its path has an empty step in it";
                if (part.IndexOfAny(illegal) >= 0) return "its name has a character Windows does not allow";
                // Windows drops these when it makes the file, so "a.exe." would
                // sail past the check below and land on disk as a.exe.
                if (part.TrimEnd(' ', '.') != part) return "its name ends in a dot or a space";
            }

            if (string.Equals(parts[0], FLAGS_FOLDER, StringComparison.OrdinalIgnoreCase))
                return "packs can't change Roblox's ClientSettings";
            if (IndexOf(Folders, parts[0]) < 0)
                return "only files inside content, ExtraContent or PlatformContent can be replaced";
            if (parts.Length < 2) return "it sits where a folder should be";

            string ext = Path.GetExtension(parts[parts.Length - 1]);
            if (IndexOf(Blocked, ext.TrimStart('.')) >= 0)
                return "programs and scripts (" + ext.ToLowerInvariant() + ") are not allowed in a pack";
            return null;
        }

        static int IndexOf(IList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        // Windows will neither overwrite nor delete a file marked read-only,
        // and copying one copies the mark along with it. Without this a pack
        // made of such files would go into Roblox and never come back out.
        static void Writable(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        // One of our own folders - a pack, or a version's saved originals -
        // removed whole. Links are stepped over rather than into.
        static void DeleteTree(string folder)
        {
            if (!Directory.Exists(folder)) return;
            Unmark(folder);
            Directory.Delete(folder, true);
        }

        static void Unmark(string folder)
        {
            foreach (string entry in Directory.GetFileSystemEntries(folder))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                Writable(entry);
                if ((attributes & FileAttributes.Directory) != 0) Unmark(entry);
            }
        }

        // Every file under a folder, by relative path. Walked by hand because
        // the framework's own recursive listing follows links, and a link in a
        // pack could point at anything on the machine.
        static void Walk(string root, string rel, List<string> files, List<string> problems)
        {
            string[] entries = Directory.GetFileSystemEntries(rel.Length == 0 ? root : Path.Combine(root, rel));
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                string path = rel.Length == 0 ? name : rel + "\\" + name;
                FileAttributes attributes = File.GetAttributes(entry);

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    problems.Add(path + " - it is a link, and a pack may only hold real files");
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Walk(root, path, files, problems);
                    continue;
                }

                string why = WhyNot(path);
                if (why == null) files.Add(path);
                else problems.Add(path + " - " + why);
            }
        }

        static string Summary(List<string> problems)
        {
            return problems.Count == 1
                ? problems[0]
                : problems[0] + " (and " + (problems.Count - 1) + " more)";
        }

        class PackScan
        {
            public LooksModPack Pack;
            public string Root;
            public readonly List<string> Files = new List<string>();
            public readonly List<string> Problems = new List<string>();
        }

        class Pick
        {
            public string Rel;
            public string Pack;
            public string Source;
        }

        // Checked again every time, not only on the way in: the library is an
        // ordinary folder, and what is in it now is not necessarily what was
        // imported.
        List<PackScan> ScanAll()
        {
            List<PackScan> scans = new List<PackScan>();
            foreach (LooksModPack pack in Packs)
            {
                PackScan s = new PackScan();
                s.Pack = pack;
                s.Root = Path.Combine(Library, pack.Name);
                try
                {
                    if (Directory.Exists(s.Root))
                    {
                        if (IsLink(s.Root)) s.Problems.Add("its folder is a link, and a pack may only hold real files");
                        else Walk(s.Root, "", s.Files, s.Problems);
                    }
                }
                catch (Exception ex) { s.Problems.Add("it couldn't be read - " + LooksFlags.Why(ex)); }
                scans.Add(s);
            }
            return scans;
        }

        // Paths are compared the way Windows compares them, so two packs that
        // spell a folder differently are still fighting over the same file.
        // also: a pack to count as on whether or not it is ticked, or null.
        static Dictionary<string, Pick> Winners(List<PackScan> scans, bool all, LooksModPack also)
        {
            Dictionary<string, Pick> picks = new Dictionary<string, Pick>(StringComparer.OrdinalIgnoreCase);
            foreach (PackScan s in scans)
            {
                if ((!all && !s.Pack.Enabled && !ReferenceEquals(s.Pack, also)) || s.Problems.Count > 0) continue;
                foreach (string rel in s.Files)
                {
                    Pick p = new Pick();
                    p.Rel = rel;
                    p.Pack = s.Pack.Name;
                    p.Source = Path.Combine(s.Root, rel);
                    picks[rel] = p;
                }
            }
            return picks;
        }

        // For each pack that is on, in order: the files it supplies and whose
        // copy of each would end up in Roblox.
        public List<LooksModPreview> Preview()
        {
            List<PackScan> scans = ScanAll();
            Dictionary<string, Pick> picks = Winners(scans, false, null);
            List<LooksModPreview> list = new List<LooksModPreview>();
            foreach (PackScan s in scans)
                if (s.Pack.Enabled) list.Add(PreviewOf(s, picks));
            return list;
        }

        // One pack's files, ticked or not: what it would put into Roblox if
        // it were on, and which of them a pack lower in the list takes
        // instead. Null when there is no such pack.
        public LooksModPreview FilesOf(string name)
        {
            LooksModPack pack = Find(name);
            if (pack == null) return null;
            List<PackScan> scans = ScanAll();
            Dictionary<string, Pick> picks = Winners(scans, false, pack);
            foreach (PackScan s in scans)
                if (ReferenceEquals(s.Pack, pack)) return PreviewOf(s, picks);
            return null;
        }

        static LooksModPreview PreviewOf(PackScan s, Dictionary<string, Pick> picks)
        {
            LooksModPreview preview = new LooksModPreview();
            preview.Pack = s.Pack.Name;
            preview.Problems.AddRange(s.Problems);
            if (s.Problems.Count == 0)
                foreach (string rel in s.Files)
                {
                    LooksModFile f = new LooksModFile();
                    f.Path = rel;
                    f.WonBy = picks[rel].Pack;
                    f.Wins = f.WonBy == s.Pack.Name;
                    preview.Files.Add(f);
                }
            return preview;
        }

        // ---------- The record of what was changed ----------

        // Kept per version, beside that version's saved originals: which of
        // Roblox's files have a saved original, which files were not Roblox's
        // at all, and which folders had to be made to hold them.
        class Journal
        {
            public readonly HashSet<string> Originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Made = new List<string>();

            public bool Empty { get { return Originals.Count == 0 && Added.Count == 0; } }

            // Throws when a record is there but cannot be read. Carrying on
            // as if nothing had been changed would take modded files for
            // originals, so the caller has to stop instead.
            public static Journal Load(string backupFolder)
            {
                Journal j = new Journal();
                string path = Path.Combine(backupFolder, BACKUP_MANIFEST);
                if (!File.Exists(path)) return j;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string key = line.Substring(0, eq);
                    string val = line.Substring(eq + 1);
                    if (key == "orig") { if (WhyNot(val) == null) j.Originals.Add(val); }
                    else if (key == "added") { if (WhyNot(val) == null) j.Added.Add(val); }
                    else if (key == "dir")
                    {
                        // A folder is fine wherever a file inside it would be.
                        if (WhyNot(val + "\\x") == null && IndexOf(j.Made, val) < 0) j.Made.Add(val);
                    }
                }
                return j;
            }

            public void Save(string backupFolder)
            {
                List<string> lines = new List<string>();
                foreach (string rel in Sorted(Originals)) lines.Add("orig=" + rel);
                foreach (string rel in Sorted(Added)) lines.Add("added=" + rel);
                foreach (string rel in Made) lines.Add("dir=" + rel);

                Directory.CreateDirectory(backupFolder);
                string path = Path.Combine(backupFolder, BACKUP_MANIFEST);
                string temp = path + PART;
                File.WriteAllLines(temp, lines.ToArray());
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }

            static List<string> Sorted(HashSet<string> set)
            {
                List<string> list = new List<string>(set);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
        }

        // ---------- Apply ----------

        // Makes every installed version hold Roblox's own files plus the
        // winning file from each pack that is on. That includes taking back
        // out what an earlier Apply put in and no pack supplies any more, so
        // switching a pack off and applying again really does switch it off.
        public LooksModsResult Apply(string versionsRoot, string backupRoot)
        {
            return Put(versionsRoot, backupRoot, false);
        }

        // What the last Apply put in, put in again - for the version Roblox
        // has just installed. Ticks changed since, and packs added since,
        // wait for Apply to be pressed.
        public LooksModsResult Reapply(string versionsRoot, string backupRoot)
        {
            return Put(versionsRoot, backupRoot, true);
        }

        LooksModsResult Put(string versionsRoot, string backupRoot, bool again)
        {
            LooksModsResult result = new LooksModsResult();
            if (string.IsNullOrEmpty(backupRoot))
            {
                result.Failed.Add(new LooksFailure("", "there is nowhere to keep the originals, so nothing was changed"));
                return result;
            }
            List<string> versions = LooksFlags.VersionFolders(versionsRoot);
            if (versions.Count == 0)
            {
                result.Failed.Add(new LooksFailure(versionsRoot ?? "", "no Roblox versions were found there"));
                return result;
            }

            List<PackScan> scans = ScanAll();
            if (again)
            {
                // Only the packs that were applied, in the order they had.
                List<PackScan> asApplied = new List<PackScan>();
                foreach (string name in applied)
                    foreach (PackScan s in scans)
                        if (string.Equals(s.Pack.Name, name, StringComparison.OrdinalIgnoreCase)) asApplied.Add(s);
                scans = asApplied;
            }
            foreach (PackScan s in scans)
                if ((again || s.Pack.Enabled) && s.Problems.Count > 0)
                    result.Failed.Add(new LooksFailure(s.Pack.Name, "this pack was left out: " + Summary(s.Problems)));
            Dictionary<string, Pick> picks = Winners(scans, again, null);

            DropBackupsOfMissingVersions(versionsRoot, backupRoot, result);
            foreach (string folder in versions)
                if (ApplyTo(folder, Path.Combine(backupRoot, Path.GetFileName(folder)), picks, again ? scans.Count > 0 : AnyEnabled, result))
                    result.Versions++;

            if (!again)
            {
                // This is what Roblox was asked to hold, and so what a new
                // version of it is given.
                applied.Clear();
                foreach (PackScan s in scans)
                    if (s.Pack.Enabled && s.Problems.Count == 0) applied.Add(s.Pack.Name);
                Save();
            }
            return result;
        }

        static bool ApplyTo(string versionFolder, string backupFolder, Dictionary<string, Pick> picks,
                            bool anyOn, LooksModsResult result)
        {
            string version = Path.GetFileName(versionFolder);
            Journal j;
            try { j = Journal.Load(backupFolder); }
            catch (Exception ex)
            {
                result.Failed.Add(new LooksFailure(version,
                    "the list of changed files couldn't be read - " + LooksFlags.Why(ex)));
                return false;
            }

            Undo(versionFolder, backupFolder, j, picks, result);

            // Nothing on and nothing left to undo: no reason to keep a folder.
            if (!anyOn && j.Empty) return DeleteFolder(backupFolder, version, result);

            try
            {
                // Saved even when it is empty - a pack that is on may supply
                // nothing. A version with a record is one that has been dealt
                // with, which is what NeedsReapply reads.
                j.Save(backupFolder);

                List<string> order = new List<string>(picks.Keys);
                order.Sort(StringComparer.OrdinalIgnoreCase);
                using (StreamWriter record = new StreamWriter(
                    Path.Combine(backupFolder, BACKUP_MANIFEST), true, new UTF8Encoding(false)))
                {
                    record.AutoFlush = true;
                    foreach (string rel in order)
                    {
                        string why = PutIn(versionFolder, backupFolder, j, record, picks[rel], result);
                        if (why != null) result.Failed.Add(new LooksFailure(version + "\\" + rel, why));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                // Without somewhere to write down what is changed, nothing is.
                result.Failed.Add(new LooksFailure(version,
                    "the list of changed files couldn't be saved - " + LooksFlags.Why(ex)));
                return false;
            }
        }

        // The order is the whole point. The original is copied aside, then
        // the change is written down, and only then is Roblox's file touched -
        // so whenever this stops, everything that was changed can be put back.
        static string PutIn(string versionFolder, string backupFolder, Journal j, StreamWriter record,
                            Pick pick, LooksModsResult result)
        {
            try
            {
                string rel = pick.Rel;
                string target = Path.Combine(versionFolder, rel);
                if (ThroughLink(versionFolder, rel)) return "there is a shortcut in its path, so it was left alone";

                bool added = j.Added.Contains(rel);
                if (!added && !j.Originals.Contains(rel))
                {
                    string backup = Path.Combine(backupFolder, rel);
                    if (File.Exists(backup))
                    {
                        // A saved original the record does not mention. It is
                        // kept as it is: what is in Roblox's folder now may
                        // already be a pack's file, and copying that over the
                        // only copy of the original would lose it for good.
                        j.Originals.Add(rel);
                        record.WriteLine("orig=" + rel);
                    }
                    else if (File.Exists(target))
                    {
                        CopyAside(target, backup);
                        j.Originals.Add(rel);
                        record.WriteLine("orig=" + rel);
                        result.BackedUp++;
                    }
                    else
                    {
                        added = true;
                        j.Added.Add(rel);
                        record.WriteLine("added=" + rel);
                    }
                }

                MakeFolders(versionFolder, rel, j, record);
                if (File.Exists(target)) Writable(target);
                File.Copy(pick.Source, target, true);
                Writable(target);
                if (added) result.Added++;
                else result.Replaced++;
                return null;
            }
            catch (Exception ex) { return LooksFlags.Why(ex); }
        }

        static void CopyAside(string original, string backup)
        {
            string temp = backup + PART;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (File.Exists(temp)) Writable(temp);
                File.Copy(original, temp, true);
                File.Move(temp, backup);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        // Folders Roblox did not have are written down before they are made,
        // so the undo can remove exactly those and no folder of Roblox's own.
        static void MakeFolders(string versionFolder, string rel, Journal j, StreamWriter record)
        {
            string[] parts = rel.Split('\\', '/');
            string full = versionFolder;
            string made = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                full = Path.Combine(full, parts[i]);
                made = made.Length == 0 ? parts[i] : made + "\\" + parts[i];
                if (Directory.Exists(full)) continue;
                if (IndexOf(j.Made, made) < 0)
                {
                    j.Made.Add(made);
                    record.WriteLine("dir=" + made);
                }
                Directory.CreateDirectory(full);
            }
        }

        // A link anywhere along the path would send the write somewhere other
        // than Roblox's folder.
        static bool ThroughLink(string versionFolder, string rel)
        {
            string full = versionFolder;
            foreach (string part in rel.Split('\\', '/'))
            {
                full = Path.Combine(full, part);
                if (!Directory.Exists(full) && !File.Exists(full)) return false;
                if (IsLink(full)) return true;
            }
            return false;
        }

        // ---------- Undo ----------

        // Puts back whatever the record lists that is not in keep (everything,
        // when keep is null). An entry leaves the record only once it really
        // is undone: a file a running client is holding stays listed, with
        // its original still saved, for the next attempt.
        static void Undo(string versionFolder, string backupFolder, Journal j, Dictionary<string, Pick> keep,
                         LooksModsResult result)
        {
            string version = Path.GetFileName(versionFolder);

            foreach (string rel in new List<string>(j.Originals))
            {
                if (keep != null && keep.ContainsKey(rel)) continue;
                try
                {
                    if (ThroughLink(versionFolder, rel)) throw new IOException("there is a shortcut in its path, so it was left alone");
                    string backup = Path.Combine(backupFolder, rel);
                    if (File.Exists(backup))
                    {
                        string target = Path.Combine(versionFolder, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) Writable(target);
                        // The copy carries the original's own marks back with
                        // it, read-only included, so it returns as it left.
                        File.Copy(backup, target, true);
                        Writable(backup);
                        File.Delete(backup);
                        result.Restored++;
                    }
                    j.Originals.Remove(rel);
                }
                catch (Exception ex) { result.Failed.Add(new LooksFailure(version + "\\" + rel, LooksFlags.Why(ex))); }
            }

            foreach (string rel in new List<string>(j.Added))
            {
                if (keep != null && keep.ContainsKey(rel)) continue;
                try
                {
                    if (ThroughLink(versionFolder, rel)) throw new IOException("there is a shortcut in its path, so it was left alone");
                    string target = Path.Combine(versionFolder, rel);
                    if (File.Exists(target))
                    {
                        Writable(target);
                        File.Delete(target);
                        result.Removed++;
                    }
                    j.Added.Remove(rel);
                }
                catch (Exception ex) { result.Failed.Add(new LooksFailure(version + "\\" + rel, LooksFlags.Why(ex))); }
            }

            // Deepest first, so a folder is empty by the time its turn comes.
            List<string> made = new List<string>(j.Made);
            made.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string rel in made)
            {
                try
                {
                    string full = Path.Combine(versionFolder, rel);
                    if (Directory.Exists(full))
                    {
                        if (Directory.GetFileSystemEntries(full).Length > 0) continue;
                        Directory.Delete(full);
                    }
                    j.Made.Remove(rel);
                }
                catch { }
            }
        }

        // Every original back where it was, every added file gone, and the
        // record cleared. It needs nothing from the library, so it works
        // whatever has happened to the packs since, and running it again
        // finds nothing left to do.
        public static LooksModsResult Restore(string versionsRoot, string backupRoot)
        {
            LooksModsResult result = new LooksModsResult();
            string[] backups;
            try
            {
                if (string.IsNullOrEmpty(backupRoot) || !Directory.Exists(backupRoot)) return result;
                backups = Directory.GetDirectories(backupRoot, "version-*");
            }
            catch (Exception ex)
            {
                result.Failed.Add(new LooksFailure(backupRoot, "the saved originals couldn't be read - " + LooksFlags.Why(ex)));
                return result;
            }
            if (backups.Length == 0) return result;

            // A version that has gone has no use for its originals. But if
            // there are no versions at all, the likelier story is a wrong or
            // unplugged folder, and throwing every original away on the
            // strength of that would be the one mistake with no way back.
            if (LooksFlags.VersionFolders(versionsRoot).Count == 0)
            {
                result.Failed.Add(new LooksFailure(versionsRoot ?? "",
                    "no Roblox versions were found there, so the saved originals were kept"));
                return result;
            }

            foreach (string backup in backups)
            {
                string version = Path.GetFileName(backup);
                string folder = Path.Combine(versionsRoot, version);
                if (!Directory.Exists(folder))
                {
                    DeleteFolder(backup, version, result);
                    continue;
                }
                if (RestoreOne(folder, backup, result)) result.Versions++;
            }
            return result;
        }

        static bool RestoreOne(string versionFolder, string backupFolder, LooksModsResult result)
        {
            string version = Path.GetFileName(versionFolder);
            Journal j;
            try { j = Journal.Load(backupFolder); }
            catch (Exception ex)
            {
                result.Failed.Add(new LooksFailure(version,
                    "the list of changed files couldn't be read - " + LooksFlags.Why(ex)));
                return false;
            }

            // A saved original goes back whether or not the record lists it.
            // The file is the proof; the record is only the index.
            try
            {
                List<string> saved = new List<string>();
                Walk(backupFolder, "", saved, new List<string>());
                foreach (string rel in saved)
                    if (!rel.EndsWith(PART, StringComparison.OrdinalIgnoreCase)) j.Originals.Add(rel);
            }
            catch { }

            Undo(versionFolder, backupFolder, j, null, result);

            if (j.Empty) return DeleteFolder(backupFolder, version, result);
            try { j.Save(backupFolder); } catch { }
            return true;
        }

        static void DropBackupsOfMissingVersions(string versionsRoot, string backupRoot, LooksModsResult result)
        {
            try
            {
                if (!Directory.Exists(backupRoot)) return;
                foreach (string backup in Directory.GetDirectories(backupRoot, "version-*"))
                {
                    string version = Path.GetFileName(backup);
                    if (!Directory.Exists(Path.Combine(versionsRoot, version)))
                        DeleteFolder(backup, version, result);
                }
            }
            catch { }
        }

        static bool DeleteFolder(string folder, string version, LooksModsResult result)
        {
            try
            {
                DeleteTree(folder);
                return true;
            }
            catch (Exception ex)
            {
                result.Failed.Add(new LooksFailure(version,
                    "its saved originals couldn't be removed - " + LooksFlags.Why(ex)));
                return false;
            }
        }

        // True when packs have been applied and an installed version has no
        // record of having had them - which is what a Roblox update looks
        // like from here: a new version folder full of original files. Never
        // true before Apply has been pressed: a pack that is only ticked has
        // not been asked into Roblox yet.
        public bool NeedsReapply(string versionsRoot, string backupRoot)
        {
            if (!AnyApplied || string.IsNullOrEmpty(backupRoot)) return false;
            foreach (string folder in LooksFlags.VersionFolders(versionsRoot))
                if (!File.Exists(Path.Combine(backupRoot, Path.GetFileName(folder), BACKUP_MANIFEST)))
                    return true;
            return false;
        }
    }
}
