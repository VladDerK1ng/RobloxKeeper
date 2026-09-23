using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The Accounts window remembers what was ticked and where they were sent,
    // so launching the same five alts into the same kind of server is one
    // press next time too.
    static class AccountsPrefsTests
    {
        public static void TestTheChoicesSurviveASave()
        {
            AccountsPrefs p = new AccountsPrefs();
            p.Where = JoinWhere.Emptiest;
            p.Together = true;
            p.Player = "MainAccount";
            p.KeepFollowing = true;
            p.Ticked.Add("alt1");
            p.Ticked.Add("alt2");

            List<string> lines = new List<string>();
            p.Write(lines);
            AccountsPrefs back = new AccountsPrefs();
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                Assert.True(back.Read(line.Substring(0, eq), line.Substring(eq + 1)), "its own key: " + line);
            }

            Assert.Equal(JoinWhere.Emptiest, back.Where, "where");
            Assert.True(back.Together, "together");
            Assert.Equal("MainAccount", back.Player, "the player");
            Assert.True(back.KeepFollowing, "following");
            Assert.Equal("alt1,alt2", string.Join(",", back.Ticked.ToArray()), "what was ticked");
        }

        public static void TestSomethingElsesKeyIsLeftAlone()
        {
            Assert.False(new AccountsPrefs().Read("interval", "15"), "not an Accounts setting");
        }

        // A hand-edited file can't pick a choice that doesn't exist.
        public static void TestANonsenseChoiceIsAnyServer()
        {
            AccountsPrefs p = new AccountsPrefs();
            p.Read("acc_where", "42");
            Assert.Equal(JoinWhere.Any, p.Where, "the default");
        }
    }
}
