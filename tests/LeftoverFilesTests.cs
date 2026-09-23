using System;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Disconnect protection is gone, and it left a copy of Roblox's cookie
    // file - every signed-in session - in RobloxKeeper's folder. Nothing reads
    // it any more, so it should not sit there.
    static class LeftoverFilesTests
    {
        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "rk_leftover_" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void TestTheOldCookieBackupIsRemoved()
        {
            string dir = TempDir();
            try
            {
                string backup = Path.Combine(dir, "RobloxCookies.backup.dat");
                File.WriteAllText(backup, "{\"cookies\":\"old\"}");
                Assert.True(LeftoverFiles.RemoveCookieBackup(dir), "reports that it removed one");
                Assert.False(File.Exists(backup), "and it is gone");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestNothingToRemoveIsNotAnError()
        {
            string dir = TempDir();
            try { Assert.False(LeftoverFiles.RemoveCookieBackup(dir), "nothing was there"); }
            finally { Directory.Delete(dir, true); }
        }

        // Only that one file: the accounts, settings and watchers beside it are
        // the user's and stay.
        public static void TestNeighbouringFilesAreLeftAlone()
        {
            string dir = TempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "RobloxCookies.backup.dat"), "x");
                File.WriteAllText(Path.Combine(dir, "accounts.dat"), "a");
                File.WriteAllText(Path.Combine(dir, "settings.txt"), "s");
                LeftoverFiles.RemoveCookieBackup(dir);
                Assert.True(File.Exists(Path.Combine(dir, "accounts.dat")), "accounts kept");
                Assert.True(File.Exists(Path.Combine(dir, "settings.txt")), "settings kept");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestAMissingFolderIsNotAnError()
        {
            Assert.False(LeftoverFiles.RemoveCookieBackup(Path.Combine(Path.GetTempPath(), "rk_no_such_" + Guid.NewGuid().ToString("n"))),
                "no folder, nothing removed, no exception");
            Assert.False(LeftoverFiles.RemoveCookieBackup(null), "no folder given");
        }
    }
}
