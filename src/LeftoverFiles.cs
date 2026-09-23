using System.IO;

namespace RobloxKeeper
{
    // Files earlier versions left behind that nothing uses any more.
    static class LeftoverFiles
    {
        // Disconnect protection copied Roblox's cookie file here before holding
        // it. That feature is gone, and a stale copy of every signed-in session
        // should not stay on disk. True when a copy was found and removed.
        public static bool RemoveCookieBackup(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            try
            {
                string backup = Path.Combine(folder, "RobloxCookies.backup.dat");
                if (!File.Exists(backup)) return false;
                File.Delete(backup);
                return true;
            }
            catch { return false; }
        }
    }
}
