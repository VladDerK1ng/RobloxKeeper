namespace RobloxKeeper
{
    // Single source of truth for the version. release.bat and the GitHub
    // Actions release workflow both rewrite APP_VERSION in this file, so the
    // published binary and the tag can never disagree.
    static class AppInfo
    {
        public const string APP_VERSION = "1.2.1";

        public const string REPO = "VladDerK1ng/RobloxKeeper";
        public const string RELEASE_API = "https://api.github.com/repos/" + REPO + "/releases/latest";
    }
}

