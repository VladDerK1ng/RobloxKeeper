using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace RobloxKeeper
{
    // The anti-AFK nudge: bring a client forward, send a harmless keypress that
    // Roblox counts as activity, and put it back the way it was.
    partial class MainForm
    {
        void OnAfkToggled(object sender, EventArgs e)
        {
            if (!chkAfk.Checked) nudgeTimer.Stop();
            if (!initializing)
                Log(chkAfk.Checked
                    ? "Anti-AFK enabled - interval " + numInterval.Value + " min. The timer runs while a client is open."
                    : "Anti-AFK disabled.");
            UpdateAfkTimer(NudgeableClientCount());
            UpdateCountdown();
            SaveSettings();
        }

        void OnIntervalChanged(object sender, EventArgs e)
        {
            if (chkAfk.Checked && nudgeTimer.Enabled)
            {
                nudgeTimer.Stop();
                nudgeTimer.Interval = numInterval.Value * 60000;
                nextNudge = DateTime.Now.AddMilliseconds(nudgeTimer.Interval);
                nudgeTimer.Start();
            }
            if (!initializing) Log("Interval set to " + numInterval.Value + " min.");
            SaveSettings();
        }

        void NudgeAll(string reason)
        {
            nextNudge = DateTime.Now.AddMinutes((double)numInterval.Value);
            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            int count = 0, skipped = 0;
            IntPtr previous = Native.GetForegroundWindow();

            foreach (ClientInfo ci in clients)
            {
                bool wanted;
                if (!nudgePrefs.TryGetValue(ci.Pid, out wanted)) wanted = true;
                if (!wanted) { skipped++; continue; }

                bool wasMinimized = Native.IsIconic(ci.Hwnd);
                if (wasMinimized) { Native.ShowWindow(ci.Hwnd, Native.SW_RESTORE); Thread.Sleep(300); }

                InputSender.FocusWindow(ci.Hwnd);
                Thread.Sleep(250);
                SendNudgeKeys();
                Thread.Sleep(150);

                if (wasMinimized) Native.ShowWindow(ci.Hwnd, Native.SW_MINIMIZE);
                count++;
            }

            if (count > 0 && previous != IntPtr.Zero && previous != Handle)
                InputSender.FocusWindow(previous);

            if (clients.Count == 0)
                Log("No Roblox clients found (" + reason + ").");
            else
                Log("Nudged " + count + " client(s)" + (skipped > 0 ? ", skipped " + skipped : "") + " (" + reason + ").");
        }

        void SendNudgeKeys()
        {
            switch (cmbKeys.SelectedIndex)
            {
                case 1: // turn camera left, then back right
                    InputSender.TapKey(Native.VK_LEFT, 180);
                    Thread.Sleep(250);
                    InputSender.TapKey(Native.VK_RIGHT, 180);
                    break;
                case 2: // jump
                    InputSender.TapKey(Native.VK_SPACE, 90);
                    break;
                default: // zoom out one notch, zoom back in
                    InputSender.TapKey(Native.VK_O, 90);
                    Thread.Sleep(350);
                    InputSender.TapKey(Native.VK_I, 90);
                    break;
            }
        }

        // The countdown only means something once there is a ticked client to
        // nudge, so it starts when one opens and stops when the last one closes -
        // rather than ticking away against nothing.
        int NudgeableClientCount()
        {
            int windowless;
            int n = 0;
            foreach (ClientInfo ci in ClientTracker.GetClients(out windowless))
            {
                bool wanted;
                if (!nudgePrefs.TryGetValue(ci.Pid, out wanted)) wanted = true;
                if (wanted) n++;
            }
            return n;
        }

        void UpdateAfkTimer(int nudgeable)
        {
            bool shouldRun = chkAfk.Checked && nudgeable > 0;
            if (shouldRun && !nudgeTimer.Enabled)
            {
                nudgeTimer.Interval = numInterval.Value * 60000;
                nextNudge = DateTime.Now.AddMilliseconds(nudgeTimer.Interval);
                nudgeTimer.Start();
                if (!initializing)
                    Log("Roblox client detected - anti-AFK timer started (" + numInterval.Value + " min).");
            }
            else if (!shouldRun && nudgeTimer.Enabled)
            {
                nudgeTimer.Stop();
                if (!initializing && chkAfk.Checked)
                    Log("No client left to nudge - anti-AFK timer paused until one opens.");
            }
        }

        void UpdateCountdown()
        {
            if (!chkAfk.Checked) { SetCountdown("Disabled", false); return; }
            if (!nudgeTimer.Enabled) { SetCountdown("Waiting for Roblox", false); return; }

            TimeSpan left = nextNudge - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            SetCountdown(((int)left.TotalMinutes).ToString() + ":" + left.Seconds.ToString("00"), true);
        }

        // The big numeral suits a clock, but "Waiting for Roblox" at 19pt is too
        // wide for the well, so word states drop a size. The label is fixed-width
        // and centres its own text, so swapping the font re-centres by itself.
        void SetCountdown(string text, bool clock)
        {
            Font want = clock ? countdownClock : countdownWord;
            if (!ReferenceEquals(lblCountdown.Font, want)) lblCountdown.Font = want;
            if (lblCountdown.Text != text) lblCountdown.Text = text;
        }
    }
}
