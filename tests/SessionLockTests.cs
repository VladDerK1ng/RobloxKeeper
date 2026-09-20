using System;
using System.IO;

namespace RobloxKeeper.Tests
{
    // A: every Roblox client on a machine shares one cookie jar, so two accounts
    // overwrite each other's session until Roblox evicts one as a duplicate
    // device login - error 273, "joined from another device", with no packet
    // loss. Holding the jar open for reading but not writing leaves clients able
    // to authenticate and unable to clobber each other.
    static class SessionLockTests
    {
        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string JarIn(string dir)
        {
            string jar = Path.Combine(dir, "RobloxCookies.dat");
            File.WriteAllText(jar, "{\"CookieJar\":\"original\"}");
            return jar;
        }

        static bool CanWrite(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None)) return true;
            }
            catch { return false; }
        }

        static bool CanRead(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return true;
            }
            catch { return false; }
        }

        // ---------- when to hold ----------

        public static void TestLeavesASingleClientAlone()
        {
            // One client has nothing to contend with, and holding the jar would
            // stop the user signing in.
            Assert.False(SessionLock.ShouldHold(0, null, DateTime.Now), "no clients");
            Assert.False(SessionLock.ShouldHold(1, null, DateTime.Now), "one client");
        }

        public static void TestHoldsOnceASecondClientExists()
        {
            Assert.True(SessionLock.ShouldHold(2, null, DateTime.Now), "two clients");
            Assert.True(SessionLock.ShouldHold(5, null, DateTime.Now), "five clients");
        }

        public static void TestAPauseSuppressesTheLock()
        {
            DateTime now = new DateTime(2026, 9, 20, 12, 0, 0);
            Assert.False(SessionLock.ShouldHold(3, now.AddSeconds(30), now),
                "paused so the user can sign in");
        }

        public static void TestThePauseExpires()
        {
            DateTime now = new DateTime(2026, 9, 20, 12, 0, 0);
            Assert.True(SessionLock.ShouldHold(3, now.AddSeconds(-1), now),
                "lock returns once the pause runs out");
        }

        // ---------- the lock itself ----------

        public static void TestHoldingLetsClientsReadAndStopsThemWriting()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };

                Assert.True(sl.Acquire(), "lock acquired");
                Assert.True(sl.Held, "reports itself held");
                Assert.True(CanRead(jar), "clients can still authenticate");
                Assert.False(CanWrite(jar), "clients cannot clobber the session store");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestReleasingHandsTheJarBack()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };
                sl.Acquire();
                sl.Release();

                Assert.False(sl.Held, "no longer held");
                Assert.True(CanWrite(jar), "writing works again once released");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestBacksUpTheJarBeforeHoldingIt()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };
                sl.Acquire();
                sl.Release();

                string backup = Path.Combine(dir, "RobloxCookies.backup.dat");
                Assert.True(File.Exists(backup), "a copy exists before we interfere");
                Assert.Contains("original", File.ReadAllText(backup), "the copy is the real jar");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // The lock is an improvement, not a prerequisite - a missing jar must
        // not take the app down with it.
        public static void TestAMissingJarIsReportedNotThrown()
        {
            string dir = TempDir();
            try
            {
                string missing = Path.Combine(dir, "RobloxCookies.dat");
                string logged = null;
                SessionLock sl = new SessionLock(missing, dir);
                sl.Log = delegate(string m) { logged = m; };

                Assert.False(sl.Acquire(), "cannot hold what is not there");
                Assert.False(sl.Held, "not held");
                Assert.NotEqual(null, logged, "the reason was logged");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestAcquiringTwiceIsHarmless()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };

                Assert.True(sl.Acquire(), "first acquire");
                Assert.True(sl.Acquire(), "second acquire is a no-op, not a failure");
                Assert.True(sl.Held, "still held");

                sl.Release();
                Assert.True(CanWrite(jar), "one release is enough");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- the per-tick lifecycle ----------

        public static void TestUpdateTakesAndDropsTheLockAsClientsComeAndGo()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };
                sl.Clock = delegate { return now[0]; };

                sl.Update(1);
                Assert.False(sl.Held, "one client, no lock");

                sl.Update(2);
                Assert.True(sl.Held, "second client arrived");
                Assert.False(CanWrite(jar), "jar is protected while two are running");

                sl.Update(1);
                Assert.False(sl.Held, "back to one client");
                Assert.True(CanWrite(jar), "sign-in works again");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestPausingReleasesAnAlreadyHeldLock()
        {
            string dir = TempDir();
            try
            {
                string jar = JarIn(dir);
                DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
                SessionLock sl = new SessionLock(jar, dir);
                sl.Log = delegate { };
                sl.Clock = delegate { return now[0]; };

                sl.Update(2);
                Assert.True(sl.Held, "held with two clients");

                sl.Pause(TimeSpan.FromSeconds(60));
                sl.Update(2);
                Assert.False(sl.Held, "paused on request");
                Assert.True(CanWrite(jar), "user can sign in during the pause");

                now[0] = now[0].AddSeconds(61);
                sl.Update(2);
                Assert.True(sl.Held, "lock returns by itself when the pause expires");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
