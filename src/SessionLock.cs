using System;
using System.IO;

namespace RobloxKeeper
{
    // Stops two Roblox clients overwriting each other's saved session.
    //
    // Every client on a machine reads and writes ONE cookie jar,
    // %LOCALAPPDATA%\Roblox\LocalStorage\RobloxCookies.dat, alongside a single
    // BrowserTrackerId in appStorage.json. Run two accounts and they take turns
    // rewriting that file. Roblox's session validation eventually decides the
    // account has moved device and the server evicts it:
    //
    //   [FLog::Network] Disconnect reason received: 273
    //   [FLog::Network] Connection lost: connectMode: Peer Disconnected
    //   [FLog::Network] Connection lost: AckTimeout 0
    //   Client has been disconnected with reason:
    //       Disconnected from game, possibly due to game joined from another device
    //
    // AckTimeout 0 means nothing was lost on the wire. The client then shows a
    // dialog blaming the user's internet, which is what made this look like a
    // network fault for so long.
    //
    // The fix is a file handle, not a file change: open the jar for reading and
    // deny writing. Clients keep the read access they need to authenticate and
    // lose the write access that causes the eviction. Same shape as
    // MutexKeeper - a kernel object held from outside the client, with no
    // injection, no memory access and nothing modified on disk.
    //
    // It is only held while two or more clients are running. One client has
    // nothing to contend with, and a permanently locked jar would stop the user
    // signing in at all.
    class SessionLock
    {
        public const string JAR_NAME = "RobloxCookies.dat";
        public const string BACKUP_NAME = "RobloxCookies.backup.dat";

        public Action<string> Log;
        public Func<DateTime> Clock = delegate { return DateTime.Now; };

        readonly string jarPath;
        readonly string backupDir;
        FileStream handle;
        DateTime? pausedUntil;
        bool backedUp;

        public SessionLock(string jarPath, string backupDir)
        {
            this.jarPath = jarPath;
            this.backupDir = backupDir;
        }

        public static string DefaultJarPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "LocalStorage", JAR_NAME);
        }

        public bool Held { get { return handle != null; } }

        // Two clients is the point at which they can start overwriting each
        // other, and a pause is the user asking for the jar back so they can
        // sign in while clients are open.
        public static bool ShouldHold(int clientCount, DateTime? pausedUntil, DateTime now)
        {
            if (clientCount < 2) return false;
            if (pausedUntil.HasValue && now < pausedUntil.Value) return false;
            return true;
        }

        // Called once per tick with the number of live clients.
        public void Update(int clientCount)
        {
            bool want = ShouldHold(clientCount, pausedUntil, Clock());
            if (want && !Held) Acquire();
            else if (!want && Held) Release();
        }

        public void Pause(TimeSpan duration)
        {
            pausedUntil = Clock().Add(duration);
            if (Held)
            {
                Release();
                Log("Session lock paused for " + (int)duration.TotalSeconds +
                    "s - sign in now; it re-arms by itself.");
            }
        }

        public bool Acquire()
        {
            if (Held) return true;

            try
            {
                // Copied before we interfere, so a jar that ends up corrupted
                // for any reason can be put back.
                if (!backedUp) Backup();

                // FileAccess.Read + FileShare.Read: we read, others may read,
                // nobody may write or delete.
                handle = new FileStream(jarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Log("Session lock on - clients share the cookie jar read-only, " +
                    "so neither can evict the other's session.");
                return true;
            }
            catch (Exception ex)
            {
                handle = null;
                // An improvement, not a prerequisite. Multi-instance keeps
                // working without it; the user just keeps the old risk.
                Log("Could not hold the session lock (" + ex.Message +
                    "). Multi-client sessions stay exposed to the duplicate-login disconnect.");
                return false;
            }
        }

        public void Release()
        {
            if (!Held) return;
            try { handle.Dispose(); }
            catch { }
            handle = null;
            Log("Session lock off - the cookie jar is writable again.");
        }

        void Backup()
        {
            try
            {
                if (!File.Exists(jarPath)) return;
                Directory.CreateDirectory(backupDir);
                File.Copy(jarPath, Path.Combine(backupDir, BACKUP_NAME), true);
                backedUp = true;
            }
            catch (Exception ex)
            {
                Log("Could not back up the cookie jar: " + ex.Message);
            }
        }
    }
}
