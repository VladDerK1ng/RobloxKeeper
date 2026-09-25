using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // The file operations an update needs, so their order can be tested.
    interface IFileOps
    {
        bool Exists(string path);
        void Move(string from, string to);
        void Delete(string path);
    }

    class LiveFileOps : IFileOps
    {
        public bool Exists(string path) { return File.Exists(path); }
        public void Move(string from, string to) { File.Move(from, to); }
        public void Delete(string path) { File.Delete(path); }
    }

    // Putting a new version in place of the one that is running.
    //
    // Windows won't overwrite a running exe, but it will rename one. So this
    // copy is renamed aside - it goes on running - and the new one takes its
    // name. The next start tidies the old copy away. No script is written or
    // run for any of it: the one this replaced wrote a hidden batch file to
    // the temp folder that waited for the app to exit, swapped the files and
    // deleted itself - which is what droppers do, and a user's antivirus
    // called the app a trojan for it.
    static class SelfSwap
    {
        const int MAX_OLD = 10;

        static string OldName(string exe, int n)
        {
            return Path.Combine(Path.GetDirectoryName(exe),
                Path.GetFileNameWithoutExtension(exe) + ".old" + (n == 0 ? "" : n.ToString()) + ".exe");
        }

        // Null when the new version is in place, otherwise why not. On any
        // failure this copy keeps its name, so the app is never left missing.
        public static string Swap(string exe, string staged, IFileOps files)
        {
            // An old copy from the last update may still be running; it can't
            // be deleted, so this one goes aside under the next free name.
            string old = null;
            for (int n = 0; n < MAX_OLD && old == null; n++)
            {
                string candidate = OldName(exe, n);
                if (!files.Exists(candidate)) { old = candidate; break; }
                try { files.Delete(candidate); old = candidate; }
                catch { }
            }
            if (old == null) return "too many older copies are still running";

            try { files.Move(exe, old); }
            catch (Exception ex) { return "couldn't move this copy aside (" + ex.Message + ")"; }

            try
            {
                files.Move(staged, exe);
                return null;
            }
            catch (Exception ex)
            {
                try { files.Move(old, exe); } catch { }
                return "couldn't put the new version in place (" + ex.Message + ")";
            }
        }

        // Old copies left by an update or a build, once they have stopped.
        // One still running stays until the next start.
        public static void CleanUp(string exe)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe);
                string name = Path.GetFileNameWithoutExtension(exe);
                Regex old = new Regex("^" + Regex.Escape(name) + @"\.old\d*\.exe$", RegexOptions.IgnoreCase);
                foreach (string file in Directory.GetFiles(dir, name + ".old*.exe"))
                {
                    if (!old.IsMatch(Path.GetFileName(file))) continue;
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }
    }
}
