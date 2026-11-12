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
                Log("Roblox isn't running.");
                return;
            }
            string msg = "Close " + clients.Count + " Roblox window(s)" +
                (windowless > 0 ? " and " + windowless + " hidden Roblox program(s)" : "") +
                "?\n\nYou'll need to rejoin your games. Multi-instance turns on as soon as they're closed.";
            if (MessageBox.Show(this, msg, "RobloxKeeper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (ClientInfo ci in clients)
                Native.PostMessage(ci.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (windowless > 0) ghostCleaner.KillEveryWindowless();
            lastCloseRequest = DateTime.Now;
            Log("Closing " + clients.Count + " Roblox window(s). Multi-instance turns on when they're gone.");
        }

        void CheckLaunchPath()
        {
            if (RobloxInstall.HasActiveThirdPartyLauncher())
                Log("Another Roblox launcher is installed: " + RobloxInstall.ThirdPartyLaunchers() +
                    ". It installs its own copy of Roblox. If your windows keep closing and Roblox " +
                    "keeps reinstalling, use just one launcher: remove the others, then reinstall Roblox once.");

            // Registered, but pointing at a version folder that no longer
            // exists. Windows then runs a missing exe, Roblox's installer fires
            // to repair the install, and that installer closes every open
            // client - which reads as clients closing at random.
            if (RobloxInstall.HandlerTargetMissing())
                Log("Play on roblox.com points at " +
                    RobloxInstall.LaunchPathVersion() + ", which isn't installed. " +
                    "Every launch starts Roblox's installer, and that closes all your open windows. " +
                    "Click Repair in the Multi-instance card to fix it.");

            if (RobloxInstall.UsesLegacyBootstrapper())
                Log("WARNING: Your Roblox install uses an old launcher that closes your open windows " +
                    "every time you launch, even with multi-instance on. " +
                    "To fix it, reinstall Roblox from roblox.com.");
        }

        // Shows or hides the repair row, and keeps its wording current.
        void UpdateLaunchHandlerRow()
        {
            bool broken = RobloxInstall.HandlerTargetMissing();
            if (broken != handlerBrokenShown)
            {
                handlerBrokenShown = broken;
                lblHandler.Visible = broken;
                btnFixHandler.Visible = broken;
                if (broken)
                    lblHandler.Text = "Play points at " + RobloxInstall.LaunchPathVersion() +
                                      ", which isn't installed - every launch closes your windows.";
            }
        }

        // Points the roblox-player registration back at a version that is
        // actually on disk. This is exactly what Roblox does for itself; doing
        // it here means the next Play click launches a client directly instead
        // of triggering the installer that closes everything.
        void RepairLaunchHandler()
        {
            string broken = RobloxInstall.LaunchPathVersion();
            string target = RobloxInstall.NewestInstalledVersion();

            if (target == null)
            {
                Log("Can't repair Play - no installed Roblox version can run. " +
                    "Reinstall Roblox from roblox.com once and it will stay fixed.");
                return;
            }

            if (!RobloxInstall.SetRegisteredVersion(target))
            {
                Log("Couldn't repair Play. Try again, or reinstall Roblox from roblox.com.");
                return;
            }

            lastRegisteredVersion = target;
            RobloxInstall.RetargetShortcuts(target, RobloxInstall.VersionsRoot, Log);
            Log("Play is repaired: " + broken + " -> " + target +
                ". It now opens Roblox directly, so it won't close your other windows.");
            UpdateLaunchHandlerRow();
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
                Log("Updated " + fixedCount + " Roblox shortcut(s) to the current version (" + reg + ").");
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
            Log("Version mismatch: this window runs " + clientVersion + " but Roblox launches " +
                launchVer + ". Two Roblox installs are competing, and each launch closes your open windows. " +
                "To fix it, close Roblox, delete %LOCALAPPDATA%\\Roblox, " +
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
                    LogInstallerKill(ups, "that launch was already redirected a moment ago, and redirecting it twice would break the join");
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
                        Log("This account needs a different Roblox version, so it was opened on " + target +
                            ". No reinstall was needed and your " + openClients +
                            " other window(s) stayed open.");
                    }
                    catch (Exception ex) { Log("Couldn't open Roblox on " + target + ": " + ex.Message); }
                }
                return;
            }

            // No launch waiting: this is a background update, so keep it away from
            // the running clients. It will install once everything is closed.
            LogInstallerKill(ups, "a background update while " + openClients + " window(s) were open");
            foreach (Process p in ups) { try { p.Kill(); } catch { } }
        }

        // Every installer we terminate gets a line. This used to be a one-shot
        // bool that logged the first occurrence and stayed silent forever after,
        // so a Roblox installer could appear, close a client, and be killed by us
        // with nothing in the log to explain the client that just vanished.
        // Rate-limited rather than one-shot: repeated attempts are the symptom.
        void LogInstallerKill(List<Process> ups, string why)
        {
            if (ups.Count == 0) return;
            if ((DateTime.Now - lastInstallerKillLog).TotalSeconds < 30) return;
            lastInstallerKillLog = DateTime.Now;
            Log("Stopped Roblox's installer (" + ups.Count + ") because " + why +
                ". It would have closed your open windows. " +
                "Roblox will update on its own once you close them all.");
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
            catch (Exception ex) { Log("Repair stopped - can't read the Roblox versions folder: " + ex.Message); return; }

            // Accounts can legitimately sit on different Roblox release channels,
            // and then BOTH versions are needed - deleting one forces a reinstall
            // every time the user switches account. Only strip duplicates when the
            // user confirms they are leftovers.
            if (all.Length == 2)
            {
                DialogResult keepBoth = MessageBox.Show(this,
                    "Two Roblox versions are installed.\r\n\r\n" +
                    "That's normal if your accounts need different versions. " +
                    "If you delete one, Roblox reinstalls it every time you switch accounts.\r\n\r\n" +
                    "Do your accounts need different versions?\r\n\r\n" +
                    "Yes = keep both (best if two accounts can't run together)\r\n" +
                    "No = remove the extra copy",
                    "Keep both Roblox versions?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (keepBoth == DialogResult.Cancel) { Log("Repair cancelled."); return; }
                if (keepBoth == DialogResult.Yes)
                {
                    Log("Repair: keeping both versions, since your accounts each need their own. " +
                        "Leave \"Block Roblox updater\" ticked so neither install closes your windows.");
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
                    "Only one Roblox version is installed (" + (keep ?? "none") + "), so there's nothing to fix.\n\n" +
                    "If your windows still close on their own, another launcher may be reinstalling its own copy: " +
                    RobloxInstall.ThirdPartyLaunchers() +
                    "\nUninstall the ones you don't use and open Roblox from just one place.",
                    "Nothing to repair", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("Repair: only one version is installed (" + (keep ?? "none") + "), so there's nothing to remove.");
                return;
            }

            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            string tp = RobloxInstall.ThirdPartyLaunchers();

            StringBuilder msg = new StringBuilder();
            msg.AppendLine("This fixes a Roblox install whose copies keep fighting and closing your windows.");
            msg.AppendLine();
            msg.AppendLine("It will:");
            msg.AppendLine("  - close " + clients.Count + " Roblox window(s) and " + windowless + " hidden Roblox program(s)");
            msg.AppendLine("  - KEEP the version Roblox uses now:  " + (keep ?? "(none)"));
            msg.AppendLine("  - DELETE " + dirs.Length + " extra version folder(s) from:");
            msg.AppendLine("    " + versionsRoot);
            msg.AppendLine();
            msg.AppendLine("Roblox stays installed. Only the extra copies go. You will have to rejoin your games.");
            msg.AppendLine();
            if (RobloxInstall.HasActiveThirdPartyLauncher())
            {
                msg.AppendLine("IMPORTANT - other Roblox launchers found: " + tp);
                msg.AppendLine("They install their own copy of Roblox and will bring the problem back.");
                msg.AppendLine("Uninstall the ones you don't use (Windows Settings > Apps) before opening Roblox again.");
                msg.AppendLine();
            }
            msg.Append("Continue?");

            if (MessageBox.Show(this, msg.ToString(), "Repair Roblox install",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Repair cancelled.");
                return;
            }

            Log("Repair started. Keeping " + (keep ?? "(none)") + " and removing " + dirs.Length + " extra version folder(s).");

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
                    Log("Couldn't remove " + Path.GetFileName(d) + ": " + ex.Message);
                }
            }

            int retargeted = RobloxInstall.RetargetShortcuts(keep, versionsRoot, Log);
            if (retargeted > 0)
                Log("Fixed " + retargeted + " Roblox shortcut(s) that pointed at a removed version.");

            Log("Repair finished. Removed " + removed + " extra version folder(s)" +
                (failed > 0 ? " (" + failed + " couldn't be removed - close everything Roblox and try again)" : "") +
                ". Kept " + (keep ?? "(none)") + ".");
            if (RobloxInstall.HasActiveThirdPartyLauncher())
            {
                Log("IMPORTANT: another Roblox launcher is still installed (" + tp +
                    "). It will reinstall its own copy and the problem will come back unless you remove it.");
                OfferLauncherUninstall();
            }
            else
                Log("No other Roblox launcher found. Open Roblox from just one place from now on.");

            versionConflictLogged = false;
            lastRegisteredVersion = null;
        }

        void OfferLauncherUninstall()
        {
            List<string[]> unins = RobloxInstall.FindLauncherUninstallers();
            if (unins.Count == 0)
            {
                MessageBox.Show(this,
                    "Windows doesn't list an uninstaller for another Roblox launcher.\r\n\r\n" +
                    "If one is still installed, remove it in Windows Settings > Apps > Installed apps, " +
                    "then delete any leftover folder in %LOCALAPPDATA%.",
                    "Nothing to uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("These other Roblox launchers are installed:");
            sb.AppendLine();
            foreach (string[] u in unins) sb.AppendLine("  - " + u[0]);
            sb.AppendLine();
            sb.AppendLine("They install their own copy of Roblox, which can make your windows close by themselves.");
            sb.AppendLine();
            sb.AppendLine("Run their uninstallers now? Each opens its own window - follow the steps.");
            sb.AppendLine("Keep a launcher only if it's the only way you start Roblox.");

            if (MessageBox.Show(this, sb.ToString(), "Uninstall other Roblox launchers",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Uninstall cancelled.");
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
                    Log("Opened the uninstaller for " + u[0] + ".");
                }
                catch (Exception ex)
                {
                    Log("Couldn't open the uninstaller for " + u[0] + ": " + ex.Message +
                        ". Remove it in Windows Settings > Apps instead.");
                }
            }
        }
    }
}
