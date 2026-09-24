using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // What a nudge is going to do, decided before any of it happens.
    //
    // The nudge runs on a worker thread, and a worker may not read controls or
    // the preference map the UI mutates. So every decision is made on the UI
    // thread first and handed over as plain data.
    class NudgePlan
    {
        public readonly List<IntPtr> Targets = new List<IntPtr>();
        public int Skipped;
        public int ClientCount;

        public bool HasWork { get { return Targets.Count > 0; } }

        public static NudgePlan From(IList<ClientInfo> clients, IDictionary<int, bool> prefs)
        {
            NudgePlan plan = new NudgePlan();
            plan.ClientCount = clients.Count;

            foreach (ClientInfo ci in clients)
            {
                bool wanted;
                // A client nobody has expressed an opinion about is nudged.
                if (!prefs.TryGetValue(ci.Pid, out wanted)) wanted = true;
                if (wanted) plan.Targets.Add(ci.Hwnd);
                else plan.Skipped++;
            }
            return plan;
        }
    }

    // The anti-AFK nudge: bring a client forward, send a harmless keypress that
    // Roblox counts as activity, and put it back the way it was.
    partial class MainForm
    {
        void OnAfkToggled(object sender, EventArgs e)
        {
            if (!started) return;          // drawn only: no timer to start
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

        // Both live in NudgePolicy, where they are tested against each other -
        // the short lull is only defensible because a fullscreen game is
        // protected separately.
        static readonly TimeSpan QUIET_THRESHOLD = NudgePolicy.DefaultQuiet;
        static readonly TimeSpan KICK_DEADLINE = NudgePolicy.DefaultDeadline;
        const int DEFER_RETRY_SECONDS = 20;

        // Decides what to nudge, then hands the work to a background thread.
        //
        // This used to run start to finish on the UI thread, sleeping about
        // 1.2-1.6s per client while it focused each one. With two clients that
        // is roughly three seconds per nudge in which the window is frozen and
        // clicks go nowhere - the more accounts, the worse it got. Nothing here
        // needs the message loop, so nothing here runs on it any more.
        void NudgeAll(string reason)
        {
            // Clicking the button or the tray item is an explicit instruction;
            // only the timer defers to what the user is doing.
            if (reason == "timer" && chkIdleOnly.Checked && !DecideTimerNudge()) return;

            nextNudge = DateTime.Now.AddMinutes((double)numInterval.Value);

            if (nudgeRunning)
            {
                // The previous nudge is still working through its clients.
                // Starting a second one would have two threads fighting over
                // which window is in the foreground.
                Log("Skipped a nudge (" + reason + ") - the previous one is still running.");
                return;
            }

            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            NudgePlan plan = NudgePlan.From(clients, nudgePrefs);

            if (plan.ClientCount == 0) { Log("No Roblox clients found (" + reason + ")."); return; }
            if (!plan.HasWork) { Log("No client ticked to nudge (" + reason + ")."); return; }

            // Read on the UI thread; the worker must not touch a control.
            NudgeStep[] steps = NudgeMethod.StepsFor(cmbKeys.SelectedIndex, CustomNudgeVk());
            IntPtr self = Handle;

            lastNudgeAt = DateTime.Now;
            deferLogged = false;

            nudgeRunning = true;
            Thread worker = new Thread(delegate() { NudgeWorker(plan, steps, self, reason); });
            worker.IsBackground = true;
            worker.Name = "Nudge";
            worker.Start();
        }

        // Returns false when this nudge should be skipped for now.
        //
        // Deferring reschedules the timer for a short retry rather than burning
        // the whole interval, so the nudge happens at the first lull instead of
        // a quarter of an hour later.
        bool DecideTimerNudge()
        {
            NudgeDecision decision = NudgePolicy.Decide(
                NudgePolicy.UserIdleFor(), DateTime.Now - lastNudgeAt,
                QUIET_THRESHOLD, KICK_DEADLINE, NudgePolicy.FullscreenGameInFront());

            if (decision == NudgeDecision.Defer)
            {
                nudgeTimer.Stop();
                nudgeTimer.Interval = DEFER_RETRY_SECONDS * 1000;
                nudgeTimer.Start();
                nextNudge = DateTime.Now.AddSeconds(DEFER_RETRY_SECONDS);

                // Once per deferral, not once every twenty seconds for as long
                // as the user keeps playing.
                if (!deferLogged)
                {
                    deferLogged = true;
                    Log(NudgePolicy.FullscreenGameInFront()
                        ? "A fullscreen game is in front - holding the nudge until you're out of it. " +
                          "It will go ahead regardless if a client gets close to the idle kick."
                        : "You're at the keyboard - holding the nudge until you're idle. " +
                          "It will go ahead regardless if a client gets close to the idle kick.");
                }
                return false;
            }

            // Back to the real interval after any deferrals.
            nudgeTimer.Stop();
            nudgeTimer.Interval = numInterval.Value * 60000;
            nudgeTimer.Start();

            if (decision == NudgeDecision.Urgent)
            {
                Log("A client is close to Roblox's idle kick - nudging now even though you're mid-something.");
                try
                {
                    tray.BalloonTipTitle = "Nudging now";
                    tray.BalloonTipText = "An AFK client is about to be idle-kicked, so RobloxKeeper " +
                        "is taking focus for a moment.";
                    tray.BalloonTipIcon = ToolTipIcon.Info;
                    tray.ShowBalloonTip(4000);
                }
                catch { }
                Thread.Sleep(600);   // a moment's warning before focus moves
            }
            return true;
        }

        // Runs off the UI thread. Touches no control and no shared collection -
        // everything it needs arrived in the plan.
        void NudgeWorker(NudgePlan plan, NudgeStep[] steps, IntPtr self, string reason)
        {
            int count = 0;
            bool gate = false;
            try
            {
                // A macro playing has the foreground; the nudge waits for it
                // rather than pulling another client in front mid-macro.
                gate = FocusGate.TryEnter(60000);
                if (!gate) return;

                IntPtr previous = Native.GetForegroundWindow();
                // Bringing a client on another virtual desktop in front takes
                // you to that desktop. Remembered, so you are brought back.
                Guid home = VirtualDesktops.Current();

                foreach (IntPtr hwnd in plan.Targets)
                {
                    // Every sleep here is time the user's foreground is held.
                    // These were 300/250/150 on top of a ~600ms key sequence,
                    // which came to roughly 1.4s per client. Trimmed to the
                    // smallest values that still let the window settle before
                    // input arrives.
                    bool wasMinimized = Native.IsIconic(hwnd);
                    if (wasMinimized) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(180); }

                    InputSender.FocusWindow(hwnd);
                    Thread.Sleep(100);
                    InputSender.Perform(steps);
                    Thread.Sleep(60);

                    if (wasMinimized) Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
                    count++;
                }

                // This app's own window too, when it is showing - it was left
                // out, which left you on a client's desktop after pressing
                // Nudge now.
                if (count > 0 && previous != IntPtr.Zero && (previous != self || Native.IsWindowVisible(self)))
                    InputSender.FocusWindow(previous);
                // Focus alone doesn't bring you back from the wallpaper or the
                // taskbar - they're on every desktop.
                if (count > 0) VirtualDesktops.ReturnTo(home);
            }
            finally
            {
                if (gate) FocusGate.Exit();
                int nudged = count;
                // Back to the UI thread to report, and to clear the guard even
                // if a window vanished mid-nudge and threw. If the form is on
                // its way out there is nothing to report to.
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(delegate
                        {
                            nudgeRunning = false;
                            Log("Nudged " + nudged + " client(s)" +
                                (plan.Skipped > 0 ? ", skipped " + plan.Skipped : "") + " (" + reason + ").");
                        }));
                    else nudgeRunning = false;
                }
                catch { nudgeRunning = false; }
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
