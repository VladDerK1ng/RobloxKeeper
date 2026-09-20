using System;

namespace RobloxKeeper.Tests
{
    // After signing in, the app asks Roblox who it just signed in as, so the
    // account names itself instead of the user having to type it and risk two
    // entries for the same account under different labels.
    //
    // Roblox answers with JSON. There is no JSON parser in .NET Framework worth
    // pulling in for one field, so the field is read directly - which is fine
    // as long as it is tested against the shapes Roblox actually returns.
    static class RobloxUserTests
    {
        public static void TestReadsTheUsername()
        {
            Assert.Equal("CoolAlt42", RobloxAuth.UsernameFromJson(
                "{\"id\":12345,\"name\":\"CoolAlt42\",\"displayName\":\"Cool\"}"),
                "the account's real username");
        }

        public static void TestPrefersNameOverDisplayName()
        {
            // displayName is not unique and can be changed freely; name is the
            // actual account identifier.
            Assert.Equal("realname", RobloxAuth.UsernameFromJson(
                "{\"displayName\":\"Pretty Name\",\"name\":\"realname\",\"id\":1}"),
                "name wins even when displayName comes first");
        }

        public static void TestCopesWithSpacingVariations()
        {
            Assert.Equal("Spaced", RobloxAuth.UsernameFromJson("{ \"name\" : \"Spaced\" }"),
                "whitespace around the colon");
        }

        public static void TestReadsAnUnderscoreName()
        {
            Assert.Equal("some_user_99", RobloxAuth.UsernameFromJson("{\"name\":\"some_user_99\"}"),
                "underscores and digits are legal in Roblox names");
        }

        public static void TestUnauthenticatedResponseHasNoName()
        {
            Assert.Equal(null, RobloxAuth.UsernameFromJson(
                "{\"errors\":[{\"code\":0,\"message\":\"Authorization has been denied for this request.\"}]}"),
                "an expired cookie returns an error body, not a name");
        }

        public static void TestGarbageHasNoName()
        {
            Assert.Equal(null, RobloxAuth.UsernameFromJson("not json at all"), "nonsense");
            Assert.Equal(null, RobloxAuth.UsernameFromJson(""), "empty");
            Assert.Equal(null, RobloxAuth.UsernameFromJson(null), "null");
        }

        // ---------- naming a profile folder ----------

        public static void TestAProfileFolderNameIsSafeToPutOnDisk()
        {
            // Account names can contain characters that are illegal in paths,
            // and a name must never be able to escape the profiles directory.
            string safe = AccountStore.SafeFolderName("bad/name\\with:chars*?");

            foreach (char c in new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
                Assert.False(safe.IndexOf(c) >= 0, "no '" + c + "' survives");
            Assert.True(safe.Length > 0, "still produces something usable");
        }

        public static void TestTraversalCannotEscapeTheProfilesFolder()
        {
            string safe = AccountStore.SafeFolderName("..\\..\\Windows\\System32");
            Assert.False(safe.Contains(".."), "no parent-directory hops");
        }

        public static void TestDifferentAccountsGetDifferentFolders()
        {
            Assert.NotEqual(AccountStore.SafeFolderName("AltOne"),
                            AccountStore.SafeFolderName("AltTwo"),
                            "two accounts must not share a browser profile");
        }

        public static void TestTheSameAccountAlwaysGetsTheSameFolder()
        {
            Assert.Equal(AccountStore.SafeFolderName("StableName"),
                         AccountStore.SafeFolderName("StableName"),
                         "so a saved login is still there next time");
        }
    }
}
