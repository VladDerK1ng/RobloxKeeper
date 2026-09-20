using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The roblox-player:// registration, and whether it points at anything real.
    //
    // Found live on a real machine: both roblox-player and roblox registered to
    //
    //   "...\Versions\version-9affbe66b2624d20\RobloxPlayerBeta.exe" %1
    //
    // with no such folder on disk. Every Play click on roblox.com then launches
    // a missing executable, Roblox's installer fires to repair the install, and
    // that installer closes every open client. It reads as "clients randomly
    // close and Roblox keeps updating", and nothing in the app noticed, because
    // nothing ever checked the target existed.
    static class LaunchHandlerTests
    {
        const string GOOD = "\"C:\\Roblox\\Versions\\version-aaa\\RobloxPlayerBeta.exe\" %1";
        const string MISSING = "\"C:\\Roblox\\Versions\\version-9affbe66b2624d20\\RobloxPlayerBeta.exe\" %1";

        static Func<string, bool> Exists(params string[] present)
        {
            List<string> set = new List<string>(present);
            return delegate(string path) { return set.Contains(path); };
        }

        // ---------- reading the command ----------

        public static void TestPullsTheExeOutOfAQuotedCommand()
        {
            Assert.Equal("C:\\Roblox\\Versions\\version-aaa\\RobloxPlayerBeta.exe",
                RobloxInstall.ExeFromCommand(GOOD), "quoted path with a %1 after it");
        }

        public static void TestPullsTheExeOutOfAnUnquotedCommand()
        {
            Assert.Equal("C:\\Roblox\\Player.exe",
                RobloxInstall.ExeFromCommand("C:\\Roblox\\Player.exe %1"), "unquoted path");
        }

        public static void TestAnUnquotedPathWithNoArgumentsStillReads()
        {
            Assert.Equal("C:\\Roblox\\Player.exe",
                RobloxInstall.ExeFromCommand("C:\\Roblox\\Player.exe"), "no %1 at all");
        }

        public static void TestNothingRegisteredReadsAsNothing()
        {
            Assert.Equal(null, RobloxInstall.ExeFromCommand("(not registered)"), "the sentinel");
            Assert.Equal(null, RobloxInstall.ExeFromCommand(""), "empty");
            Assert.Equal(null, RobloxInstall.ExeFromCommand(null), "null");
        }

        // ---------- judging it ----------

        public static void TestAHandlerPointingAtAMissingExeIsBroken()
        {
            Assert.True(RobloxInstall.HandlerTargetMissing(MISSING, Exists()),
                "the exact failure found on the real machine");
        }

        public static void TestAHandlerPointingAtARealExeIsFine()
        {
            Assert.False(RobloxInstall.HandlerTargetMissing(
                GOOD, Exists("C:\\Roblox\\Versions\\version-aaa\\RobloxPlayerBeta.exe")),
                "target is there");
        }

        // Absence of a registration is a different problem with a different fix,
        // and must not be reported as a dangling pointer.
        public static void TestNoRegistrationIsNotReportedAsBroken()
        {
            Assert.False(RobloxInstall.HandlerTargetMissing("(not registered)", Exists()),
                "nothing registered is not the same as registered-and-dangling");
        }

        // ---------- choosing the repair target ----------

        public static void TestRepairPicksTheNewestInstalledVersion()
        {
            List<string> versions = new List<string>(new string[] { "version-old", "version-new", "version-mid" });
            Func<string, DateTime> stamp = delegate(string v)
            {
                if (v == "version-new") return new DateTime(2026, 9, 19);
                if (v == "version-mid") return new DateTime(2026, 8, 15);
                return new DateTime(2026, 6, 14);
            };

            Assert.Equal("version-new", RobloxInstall.NewestVersion(versions, stamp),
                "the most recently installed client is the one Roblox wants");
        }

        public static void TestRepairHasNoTargetWhenNothingIsInstalled()
        {
            Assert.Equal(null, RobloxInstall.NewestVersion(new List<string>(),
                delegate(string v) { return DateTime.MinValue; }),
                "nothing to repair to - Roblox genuinely needs reinstalling");
        }

        public static void TestASingleInstalledVersionIsTheTarget()
        {
            List<string> one = new List<string>(new string[] { "version-only" });
            Assert.Equal("version-only", RobloxInstall.NewestVersion(one,
                delegate(string v) { return new DateTime(2026, 1, 1); }), "the only candidate");
        }

        // A version folder without a player executable is not a candidate. One
        // such folder was present on the real machine.
        public static void TestAVersionFolderWithNoPlayerIsNotACandidate()
        {
            // InstalledVersionList already filters these out, so NewestVersion
            // only ever sees real candidates - this pins that contract.
            List<string> versions = new List<string>(new string[] { "version-real" });
            Assert.Equal("version-real", RobloxInstall.NewestVersion(versions,
                delegate(string v) { return new DateTime(2026, 9, 1); }),
                "only versions that can actually launch are offered");
        }
    }
}
