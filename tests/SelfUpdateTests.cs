using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RobloxKeeper.Tests
{
    // Updating without looking like malware.
    //
    // A user's antivirus called RobloxKeeper a trojan "executing commands" and
    // kept deleting it. The updater did exactly what droppers do: it wrote a
    // hidden batch file to %TEMP% that polled tasklist, slept with
    // "ping -n 2 127.0.0.1", moved a downloaded exe over this one, started it
    // and deleted itself - and the exe carried that script as text. Windows
    // lets a running exe be renamed, so the swap needs no script at all: this
    // copy is renamed aside, the new one takes its name, and the new one waits
    // for this one to exit before it starts properly.
    static class SelfUpdateTests
    {
        class FakeFiles : IFileOps
        {
            public readonly List<string> Did = new List<string>();
            public readonly HashSet<string> Present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string FailMoveFrom;

            public bool Exists(string path) { return Present.Contains(path); }

            public void Move(string from, string to)
            {
                if (from == FailMoveFrom) throw new IOException("access denied");
                Did.Add("move " + Path.GetFileName(from) + " > " + Path.GetFileName(to));
                Present.Remove(from);
                Present.Add(to);
            }

            public void Delete(string path)
            {
                if (Locked.Contains(path)) throw new IOException("in use");
                Did.Add("delete " + Path.GetFileName(path));
                Present.Remove(path);
            }
        }

        const string EXE = @"C:\Apps\RobloxKeeper.exe";
        const string NEW = @"C:\Apps\RobloxKeeper.new.exe";
        const string OLD = @"C:\Apps\RobloxKeeper.old.exe";

        public static void TestTheSwapIsTwoRenames()
        {
            FakeFiles f = new FakeFiles();
            f.Present.Add(EXE);
            f.Present.Add(NEW);
            Assert.Equal(null, SelfSwap.Swap(EXE, NEW, f), "done");
            Assert.Equal("move RobloxKeeper.exe > RobloxKeeper.old.exe | move RobloxKeeper.new.exe > RobloxKeeper.exe",
                         string.Join(" | ", f.Did.ToArray()), "this copy aside, the new one in its place");
        }

        // An old copy from the last update, still running, can't be deleted:
        // this one goes aside under the next free name.
        public static void TestAnOldCopyStillRunningIsLeftAlone()
        {
            FakeFiles f = new FakeFiles();
            f.Present.Add(EXE);
            f.Present.Add(NEW);
            f.Present.Add(OLD);
            f.Locked.Add(OLD);
            Assert.Equal(null, SelfSwap.Swap(EXE, NEW, f), "done");
            Assert.Contains("move RobloxKeeper.exe > RobloxKeeper.old1.exe", string.Join(" | ", f.Did.ToArray()), "the next name");
        }

        // The new one can't take the name: this copy is put back, so the app
        // is never left missing.
        public static void TestAFailedSwapPutsThisCopyBack()
        {
            FakeFiles f = new FakeFiles();
            f.Present.Add(EXE);
            f.Present.Add(NEW);
            f.FailMoveFrom = NEW;
            string why = SelfSwap.Swap(EXE, NEW, f);
            Assert.True(why != null, "said why");
            Assert.True(f.Present.Contains(EXE), "RobloxKeeper.exe is still there");
            Assert.Contains("move RobloxKeeper.old.exe > RobloxKeeper.exe", string.Join(" | ", f.Did.ToArray()), "put back");
        }

        public static void TestOldCopiesAreTidiedAtStart()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-swap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string exe = Path.Combine(dir, "RobloxKeeper.exe");
                File.WriteAllText(exe, "x");
                File.WriteAllText(Path.Combine(dir, "RobloxKeeper.old.exe"), "x");
                File.WriteAllText(Path.Combine(dir, "RobloxKeeper.old2.exe"), "x");
                File.WriteAllText(Path.Combine(dir, "Other.old.exe"), "x");
                SelfSwap.CleanUp(exe);
                Assert.False(File.Exists(Path.Combine(dir, "RobloxKeeper.old.exe")), "gone");
                Assert.False(File.Exists(Path.Combine(dir, "RobloxKeeper.old2.exe")), "gone too");
                Assert.True(File.Exists(Path.Combine(dir, "Other.old.exe")), "somebody else's left alone");
                Assert.True(File.Exists(exe), "and this one, of course");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // The new copy is started with the old one's process id and waits for
        // it to exit, rather than finding it still running and giving up.
        public static void TestTheNewCopyWaitsForTheOldOne()
        {
            Assert.Equal(4242, Program.WaitForPid(new[] { "RobloxKeeper.exe", "--after", "4242" }), "the old copy");
            Assert.Equal(0, Program.WaitForPid(new[] { "RobloxKeeper.exe", "--minimized" }), "nothing to wait for");
            Assert.Equal(0, Program.WaitForPid(new[] { "RobloxKeeper.exe", "--after", "nonsense" }), "not a process id");
        }

        // ---------- checking the download ----------

        public static void TestTheChecksumIsPublishedBesideTheExe()
        {
            string json = "{\"tag_name\":\"v1.3.2\",\"assets\":[{\"browser_download_url\":\"https://github.com/x/y/releases/download/v1.3.2/RobloxKeeper.exe\"},"
                        + "{\"browser_download_url\":\"https://github.com/x/y/releases/download/v1.3.2/RobloxKeeper.exe.sha256\"}]}";
            Assert.Equal("https://github.com/x/y/releases/download/v1.3.2/RobloxKeeper.exe", Updater.FindAsset(json, ".exe"), "the exe");
            Assert.Equal("https://github.com/x/y/releases/download/v1.3.2/RobloxKeeper.exe.sha256", Updater.FindAsset(json, ".sha256"), "its checksum");
        }

        public static void TestAChecksumFileIsRead()
        {
            Assert.Equal("81cdb6d4c7f75dd6f7d1a988386650bc1033a15225a848c4a6c97ecc9a3a848e",
                Updater.HashIn("81CDB6D4C7F75DD6F7D1A988386650BC1033A15225A848C4A6C97ECC9A3A848E  RobloxKeeper.exe\r\n"), "the hash, lower case");
            Assert.Equal(null, Updater.HashIn("<html>not found</html>"), "not a checksum");
        }

        // ---------- nothing that looks like a dropper ----------

        // The app never writes or runs a script, and doesn't carry one.
        public static void TestNoScriptIsWrittenOrRun()
        {
            foreach (string file in Directory.GetFiles("src", "*.cs"))
            {
                string text = File.ReadAllText(file);
                foreach (string bad in new[] { ".bat\"", "tasklist", "ping -n", "%~f0", "cmd.exe", "powershell" })
                    Assert.False(text.IndexOf(bad, StringComparison.OrdinalIgnoreCase) >= 0,
                                 Path.GetFileName(file) + " has " + bad);
            }
        }

        // An exe that says who made it and what it is, as every normal program does.
        public static void TestTheExeSaysWhatItIs()
        {
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            Assert.Equal("RobloxKeeper", v.ProductName, "product");
            Assert.Equal(AppInfo.APP_VERSION, v.ProductVersion, "its version");
            Assert.True(!string.IsNullOrEmpty(v.CompanyName), "who made it");
            Assert.True(!string.IsNullOrEmpty(v.FileDescription), "what it is");
        }
    }
}
