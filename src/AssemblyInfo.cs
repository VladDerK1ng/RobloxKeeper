using System.Reflection;

// What the exe says about itself in Properties > Details - who made it, what
// it is and which version - like any ordinary program. Without it the file
// was anonymous, which is one more thing an antivirus counts against an
// unsigned download.
[assembly: AssemblyTitle("RobloxKeeper")]
[assembly: AssemblyDescription("Anti-AFK and multi-instance manager for Roblox")]
[assembly: AssemblyCompany("VladDerKing")]
[assembly: AssemblyProduct("RobloxKeeper")]
[assembly: AssemblyCopyright("Copyright (c) 2026 VladDerKing. MIT License.")]
[assembly: AssemblyVersion(RobloxKeeper.AppInfo.APP_VERSION)]
[assembly: AssemblyFileVersion(RobloxKeeper.AppInfo.APP_VERSION)]
[assembly: AssemblyInformationalVersion(RobloxKeeper.AppInfo.APP_VERSION)]
