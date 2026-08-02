using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Roblox reinstalling itself is the single biggest cause of "my clients
    // closed on their own": every install run terminates every open client, and
    // two competing copies take turns doing it forever. Everything here exists to
    // detect that, work around it, or repair it.
    partial class MainForm
    {
        void CloseAllRoblox()
        {
            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            if (clients.Count == 0 && windowless == 0)
            {
                Log("No Roblox processes to close.");
                return;
            }
            string msg = "Close " + clients.Count + " Roblox client(s)" +
                (windowless > 0 ? " and end " + windowless + " background process(es)" : "") +
                "?\n\nYou'll need to rejoin your games, but multi-instance activates the moment they're gone.";
            if (MessageBox.Show(this, msg, "RobloxKeeper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (ClientInfo ci in clients)
                Native.PostMessage(ci.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (windowless > 0) ghostCleaner.KillEveryWindowless();
            lastCloseRequest = DateTime.Now;
            Log("Close request sent to " + clients.Count + " client(s) - taking the mutex as soon as they exit.");
        }

        void CheckLaunchPath()
        {
            if (RobloxInstall.HasActiveThirdPartyLauncher())
                Log("Third-party Roblox launcher detected: " + RobloxInstall.ThirdPartyLaunchers() +
                    ". These install and register their OWN Roblox version. If clients keep " +
                    "closing and Roblox keeps reinstalling, use only ONE launcher - remove the " +
                    "others, then reinstall Roblox once.");

            if (RobloxInstall.UsesLegacyBootstrapper())
                Log("WARNING: Roblox launches through the legacy bootstrapper " +
                    "(RobloxPlayerLauncher). It closes running clients on every launch, " +
                    "even while RobloxKeeper holds the mutex. Fix: reinstall Roblox from " +
                    "roblox.com so Play opens RobloxPlayerBeta.exe directly.");
        }

        // Desktop/Start-menu shortcuts hard-code a version path, so they bypass
        // the version this app points Roblox at. Repoint them silently; with
        // versions alternating per account, a mismatch here is expected rather
        // than something worth warning about.
        void FixStaleShortcuts()
        {
            string reg = RobloxInstall.LaunchPathVersion();
            if (reg == "?") return;
            int stale = 0;
            foreach (string[] s in RobloxInstall.FindRobloxShortcuts())
                if (!string.Equals(s[2], reg, StringComparison.OrdinalIgnoreCase)) stale++;
            if (stale == 0) return;

            int fixedCount = RobloxInstall.RetargetShortcuts(reg, RobloxInstall.VersionsRoot, Log);
            if (fixedCount > 0)
                Log("Repointed " + fixedCount + " Roblox shortcut(s) at the current version (" + reg + ").");
        }

        // Two installed Roblox versions take turns re-registering themselves.
        // Each hand-over runs that version's installer, which closes every open
        // client - so multi-instance appears to "randomly" break every few minutes.
        void WarnOnVersionConflict(string clientVersion)
        {
            string launchVer = RobloxInstall.LaunchPathVersion();
            if (clientVersion == "?" || launchVer == "?") return;
            if (string.Equals(clientVersion, launchVer, StringComparison.OrdinalIgnoreCase)) return;
            if (versionConflictLogged) return;
            versionConflictLogged = true;
            Log("VERSION CONFLICT: this client runs " + clientVersion + " but Roblox is registered to launch " +
                launchVer + ". Two Roblox installs are competing - each launch re-runs the installer, " +
                "which closes your open clients. FIX: close Roblox, delete %LOCALAPPDATA%\\Roblox, " +
                "then reinstall once from roblox.com.");
        }

        // A Roblox client that is starting but has not drawn a window yet, holding
        // the roblox-player:// URL from the browser. This is the launch that is
        // about to make Roblox reinstall, and its URL is what lets us redirect it.
        bool FindPendingLaunch(IList<Process> snapshot, out int pid, out string args, out string version)
        {
            pid = 0; args = null; version = null;
            DateTime best = DateTime.MinValue;
            foreach (Process p in snapshot)
            {
                try
                {
                    if (!ClientTracker.IsClient(p)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero) continue;
                    DateTime started;
                    try { started = p.StartTime; } catch { continue; }
                    if ((DateTime.Now - started).TotalSeconds > ClientTracker.GHOST_GRACE_SECONDS) continue;
                    string a = RobloxInstall.ArgsOf(RobloxInstall.CommandLineOf(p.Id));
                    if (string.IsNullOrEmpty(a)) continue;
                    if (started > best)
                    {
                        best = started; pid = p.Id; args = a;
                        version = RobloxInstall.VersionFolderOf(RobloxInstall.PathOf(p));
                    }
                }
                catch { }   // snapshot is owned and disposed by the caller
            }
            return pid != 0;
        }

        string PickOtherVersion(string notThis)
        {
            List<string> installed = RobloxInstall.InstalledVersionList();
            string other = null;
            foreach (string v in installed)
            {
                if (string.Equals(v, notThis, StringComparison.OrdinalIgnoreCase)) continue;
                if (seenClientVersions.Contains(v)) return v;   // one an account really used
                if (other == null) other = v;
            }
            return other;
        }

        // Roblox hands different accounts different client versions and reinstalls
        // to switch, and that installer closes every open client. When the version
        // an account wants is already installed, no install is needed at all: point
        // Roblox at it and start that client directly with the same join URL. The
        // running clients are then never touched.
        void HandleVersionSwitch(int openClients, List<Process> ups, IList<Process> snapshot)
        {
            if (ups.Count == 0) return;
            if (openClients == 0) return;   // nothing to protect - let Roblox update normally

            int pendingPid; string pendingArgs, pendingVersion;
            bool hasPending = FindPendingLaunch(snapshot, out pendingPid, out pendingArgs, out pendingVersion);
            string target = hasPending ? PickOtherVersion(pendingVersion) : null;

            // A join ticket is single-use: redirecting the same launch twice burns
            // it and Roblox answers "Authentication Failed 403". Redirect any one
            // launch once, and never twice in quick succession.
            if (hasPending && target != null)
            {
                if (pendingArgs == lastRedirectArgs ||
                    (DateTime.Now - lastRedirectAt).TotalSeconds < 20)
                {
                    foreach (Process p in ups) { try { p.Kill(); } catch { } }
                    return;
                }
                lastRedirectArgs = pendingArgs;
                lastRedirectAt = DateTime.Now;
            }

            if (hasPending && target != null)
            {
                foreach (Process p in ups) { try { p.Kill(); } catch { } }
                try { using (Process p = Process.GetProcessById(pendingPid)) p.Kill(); } catch { }

                string exe = Path.Combine(RobloxInstall.VersionsRoot, target, "RobloxPlayerBeta.exe");
                if (File.Exists(exe) && RobloxInstall.SetRegisteredVersion(target))
                {
                    lastRegisteredVersion = target;
                    try
                    {
                        Process.Start(new ProcessStartInfo(exe, pendingArgs) { UseShellExecute = false });
                        Log("This account needs a different Roblox version - started it on " + target +
                            " directly, so no reinstall was needed and your " + openClients +
                            " other client(s) stayed open.");
                    }
                    catch (Exception ex) { Log("Could not start the client on " + target + ": " + ex.Message); }
                }
                return;
            }

            // No launch waiting: this is a background update, so keep it away from
            // the running clients. It will install once everything is closed.
            foreach (Process p in ups) { try { p.Kill(); } catch { } }
            if (!updateHeldLogged)
            {
                updateHeldLogged = true;
                Log("Roblox tried to update while you were playing - held it back so your clients stay open. " +
                    "It will update by itself once you close them all.");
            }
        }

        // The duplicate-install ping-pong can only be broken by removing the
        // competing copies. Roblox re-downloads a clean version on next launch,
        // so this is recoverable - but it closes running games, and third-party
        // launchers must be uninstalled by the user, so both are spelled out first.
        //
        // NOTE: nothing calls this yet - it has never had a button in the UI.
        void RepairInstall()
        {
            string versionsRoot = RobloxInstall.VersionsRoot;

            string[] all;
            try { all = Directory.Exists(versionsRoot) ? Directory.GetDirectories(versionsRoot) : new string[0]; }
            catch (Exception ex) { Log("Repair aborted - can't read Versions folder: " + ex.Message); return; }

            // Accounts can legitimately sit on different Roblox release channels,
            // and then BOTH versions are needed - deleting one forces a reinstall
            // every time the user switches account. Only strip duplicates when the
            // user confirms they are leftovers.
            if (all.Length == 2)
            {
                DialogResult keepBoth = MessageBox.Show(this,
                    "Two Roblox versions are installed.\r\n\r\n" +
                    "That is normal if your accounts are on different Roblox release channels " +
                    "(a premium/main account often is) - each account needs its own version, and " +
                    "deleting one makes Roblox reinstall it every time you switch.\r\n\r\n" +
                    "Do your accounts need DIFFERENT versions?\r\n\r\n" +
                    "Yes  = keep both versions (recommended if two accounts fail to run together)\r\n" +
                    "No   = remove the extra copy",
                    "Keep both Roblox versions?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (keepBoth == DialogResult.Cancel) { Log("Repair cancelled."); return; }
                if (keepBoth == DialogResult.Yes)
                {
                    Log("Repair: keeping BOTH versions - accounts on different channels each need their own. " +
                        "Leave \"Block Roblox updater\" ticked so neither install can close your clients.");
                    RobloxInstall.RetargetShortcuts(RobloxInstall.LaunchPathVersion(), versionsRoot, Log);
                    return;
                }
            }

            // Keep the version Roblox is currently registered to (fall back to the
            // newest client), and remove every competing copy. One version left
            // means there is nothing for the installer loop to fight over.
            string keep = RobloxInstall.LaunchPathVersion();
            if (keep == "?" || !Directory.Exists(Path.Combine(versionsRoot, keep)))
            {
                keep = null;
                DateTime newest = DateTime.MinValue;
                foreach (string d in all)
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    DateTime t = File.GetLastWriteTime(exe);
                    if (t > newest) { newest = t; keep = Path.GetFileName(d); }
                }
            }

            List<string> dirList = new List<string>();
            foreach (string d in all)
                if (keep == null || !string.Equals(Path.GetFileName(d), keep, StringComparison.OrdinalIgnoreCase))
                    dirList.Add(d);
            string[] dirs = dirList.ToArray();

            if (dirs.Length == 0)
            {
                MessageBox.Show(this,
                    "Only one Roblox version is installed (" + (keep ?? "none") + "), so there is nothing " +
                    "for duplicate installs to fight over.\n\nIf clients still close on their own, the cause is a " +
                    "third-party launcher reinstalling its own copy: " + RobloxInstall.ThirdPartyLaunchers() +
                    "\nUninstall the ones you don't use, then launch Roblox from one source only.",
                    "Nothing to repair", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("Repair: only one version folder present (" + (keep ?? "none") + ") - nothing to remove.");
                return;
            }

            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            string tp = RobloxInstall.ThirdPartyLaunchers();

            StringBuilder msg = new StringBuilder();
            msg.AppendLine("This repairs a Roblox install whose copies keep fighting and closing your clients.");
            msg.AppendLine();
            msg.AppendLine("It will:");
            msg.AppendLine("  - close " + clients.Count + " running client(s) and " + windowless + " background process(es)");
            msg.AppendLine("  - KEEP the version Roblox currently uses:  " + (keep ?? "(none)"));
            msg.AppendLine("  - DELETE " + dirs.Length + " leftover version folder(s) from:");
            msg.AppendLine("    " + versionsRoot);
            msg.AppendLine();
            msg.AppendLine("Your working Roblox stays installed - only the duplicate copies go, so there is");
            msg.AppendLine("nothing left to fight over. You WILL have to rejoin your games.");
            msg.AppendLine();
            if (RobloxInstall.HasActiveThirdPartyLauncher())
            {
                msg.AppendLine("IMPORTANT - third-party launchers found: " + tp);
                msg.AppendLine("These install their OWN Roblox version and will recreate the conflict.");
                msg.AppendLine("Uninstall the ones you don't use (Windows Settings > Apps) BEFORE relaunching,");
                msg.AppendLine("then always launch Roblox from one source only.");
                msg.AppendLine();
            }
            msg.Append("Continue?");

            if (MessageBox.Show(this, msg.ToString(), "Repair Roblox install",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Repair cancelled.");
                return;
            }

            Log("Repair started - keeping " + (keep ?? "(none)") + ", removing " + dirs.Length + " duplicate version folder(s).");

            foreach (ClientInfo ci in clients)
                Native.PostMessage(ci.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(2500);

            foreach (Process p in Process.GetProcessesByName(ClientTracker.ROBLOX_PROCESS))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
            foreach (string helper in new string[] { "RobloxPlayerInstaller", "RobloxPlayerLauncher", "RobloxCrashHandler" })
            {
                foreach (Process p in Process.GetProcessesByName(helper))
                {
                    try { p.Kill(); } catch { }
                    p.Dispose();
                }
            }
            Thread.Sleep(1500);

            int removed = 0, failed = 0;
            foreach (string d in dirs)
            {
                try { Directory.Delete(d, true); removed++; }
                catch (Exception ex)
                {
                    failed++;
                    Log("Could not remove " + Path.GetFileName(d) + ": " + ex.Message);
                }
            }

            int retargeted = RobloxInstall.RetargetShortcuts(keep, versionsRoot, Log);
            if (retargeted > 0)
                Log("Fixed " + retargeted + " stale Roblox shortcut(s) that still pointed at a removed version - " +
                    "those were launching the wrong client and triggering the repair loop.");

            Log("Repair finished - removed " + removed + " duplicate version folder(s)" +
                (failed > 0 ? ", " + failed + " could not be removed (try again once everything Roblox is closed)" : "") +
                ". Kept " + (keep ?? "(none)") + ".");
            if (RobloxInstall.HasActiveThirdPartyLauncher())
            {
                Log("IMPORTANT: a third-party launcher is still installed (" + tp +
                    ") - it will reinstall its own copy and the conflict returns unless you remove it.");
                OfferLauncherUninstall();
            }
            else
                Log("No active third-party launcher found. Launch Roblox from one source only from now on.");

            versionConflictLogged = false;
            lastRegisteredVersion = null;
        }

        void OfferLauncherUninstall()
        {
            List<string[]> unins = RobloxInstall.FindLauncherUninstallers();
            if (unins.Count == 0)
            {
                MessageBox.Show(this,
                    "No third-party launcher uninstaller was found in Windows' installed-programs list.\r\n\r\n" +
                    "If one is still installed, remove it from Windows Settings > Apps > Installed apps, " +
                    "then delete any leftover folder in %LOCALAPPDATA%.",
                    "Nothing to uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("These third-party Roblox launchers are installed:");
            sb.AppendLine();
            foreach (string[] u in unins) sb.AppendLine("  - " + u[0]);
            sb.AppendLine();
            sb.AppendLine("They install and register their OWN Roblox version, which is what makes");
            sb.AppendLine("clients close by themselves when a second install disagrees.");
            sb.AppendLine();
            sb.AppendLine("Run their uninstallers now? Each one opens its own uninstall window;");
            sb.AppendLine("follow the prompts. Keep a launcher only if it is the ONLY way you start Roblox.");

            if (MessageBox.Show(this, sb.ToString(), "Uninstall third-party launchers",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Launcher uninstall cancelled.");
                return;
            }

            foreach (string[] u in unins)
            {
                try
                {
                    string cmd = u[1].Trim();
                    string exe, args = "";
                    if (cmd.StartsWith("\""))
                    {
                        int close = cmd.IndexOf('"', 1);
                        exe = cmd.Substring(1, close - 1);
                        args = cmd.Substring(close + 1).Trim();
                    }
                    else
                    {
                        int sp = cmd.IndexOf(' ');
                        exe = sp > 0 ? cmd.Substring(0, sp) : cmd;
                        args = sp > 0 ? cmd.Substring(sp + 1) : "";
                    }
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    Log("Started uninstaller for " + u[0] + ".");
                }
                catch (Exception ex)
                {
                    Log("Could not start uninstaller for " + u[0] + ": " + ex.Message +
                        " - remove it from Windows Settings > Apps instead.");
                }
            }
        }
    }
}
