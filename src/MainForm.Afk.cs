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

        // When the last nudge was, once this one is over. A nudge kept off
        // the keyboard for its whole wait - a macro holding it - nudged
        // nothing, so the last one stays the last one and the kick deadline
        // still counts from it. One that got in counts from when it did.
        public static DateTime LastNudgeAfter(DateTime before, bool gotIn, DateTime gotInAt)
        {
            return gotIn ? gotInAt : before;
        }

        // Whether to try again in a few seconds rather than a whole interval
        // later: only for a nudge that never got in, and only while the
        // nudges are on.
        public static bool RetrySoon(bool gotIn, bool nudgesOn)
        {
            return !gotIn && nudgesOn;
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
                    ? "Anti-AFK is on - a nudge every " + numInterval.Value + " min while a Roblox window is open."
                    : "Anti-AFK is off.");
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
            if (!initializing) Log("Nudging every " + numInterval.Value + " min.");
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

            // A nudge kept off the keyboard is tried again in a few seconds
            // (see NudgeWorker); the one that goes ahead puts the timer back
            // to the real interval. DecideTimerNudge does that itself, but
            // only when it is asked.
            if (reason == "timer" && !chkIdleOnly.Checked && nudgeTimer.Interval != numInterval.Value * 60000)
            {
                nudgeTimer.Stop();
                nudgeTimer.Interval = numInterval.Value * 60000;
                nudgeTimer.Start();
            }

            nextNudge = DateTime.Now.AddMinutes((double)numInterval.Value);

            if (nudgeRunning)
            {
                // The previous nudge is still working through its clients.
                // Starting a second one would have two threads fighting over
                // which window is in the foreground.
                Log("Skipped a nudge (" + reason + ") - the last one is still going.");
                return;
            }

            int windowless;
            List<ClientInfo> clients = ClientTracker.GetClients(out windowless);
            NudgePlan plan = NudgePlan.From(clients, nudgePrefs);

            if (plan.ClientCount == 0) { Log("No Roblox window is open (" + reason + ")."); return; }
            if (!plan.HasWork) { Log("No Roblox window is ticked for nudges (" + reason + ")."); return; }

            // Read on the UI thread; the worker must not touch a control.
            NudgeStep[] steps = NudgeMethod.StepsFor(cmbKeys.SelectedIndex, CustomNudgeVk());
            IntPtr self = Handle;

            // lastNudgeAt is set when the nudge gets the keyboard, not here:
            // one kept out for a minute nudged nothing.
            deferLogged = false;

            nudgeRunning = true;
            Thread worker = new Thread(delegate() { NudgeWorker(plan, steps, self, reason); });
            worker.IsBackground = true;
            worker.Name = "Nudge";
            worker.Start();
        }

        // A fullscreen game in front is taken to be someone playing, however
        // long since a key - except when it is a macro doing the playing. A
        // session or the autoclicker left running on a fullscreen client, with
        // nobody having really touched anything for a minute, is not a game to
        // protect: the other clients would otherwise only ever be nudged at
        // the last minute, by the override.
        bool LeftToASession()
        {
            return SessionsRunning > 0 && RealInput.Active && RealInput.IdleFor() >= RuleRunner.AwayAfter;
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
                QUIET_THRESHOLD, KICK_DEADLINE, NudgePolicy.FullscreenGameInFront() && !LeftToASession());

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
                        ? "A fullscreen game is in front, so the nudge waits until you leave it. " +
                          "It still goes ahead before Roblox would kick you."
                        : "You're at the keyboard, so the nudge waits until you stop. " +
                          "It still goes ahead before Roblox would kick you.");
                }
                return false;
            }

            // Back to the real interval after any deferrals.
            nudgeTimer.Stop();
            nudgeTimer.Interval = numInterval.Value * 60000;
            nudgeTimer.Start();

            if (decision == NudgeDecision.Urgent)
            {
                Log("A Roblox window is about to be kicked for being idle - nudging it now.");
                try
                {
                    tray.BalloonTipTitle = "Nudging now";
                    tray.BalloonTipText = "A Roblox window is about to be kicked for being idle, so RobloxKeeper " +
                        "is switching to it for a moment.";
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
            DateTime gotInAt = DateTime.MinValue;
            try
            {
                // A macro playing has the foreground; the nudge waits for it
                // rather than pulling another client in front mid-macro.
                // Said to be the nudge, so a macro playing makes way at once
                // rather than at the end of its turn.
                gate = FocusGate.TryEnter(60000, true);
                if (!gate) return;
                gotInAt = DateTime.Now;

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
                bool gotIn = gate;
                DateTime inAt = gotInAt;
                // Back to the UI thread to report, and to clear the guard even
                // if a window vanished mid-nudge and threw. If the form is on
                // its way out there is nothing to report to.
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(delegate
                        {
                            nudgeRunning = false;
                            lastNudgeAt = NudgePlan.LastNudgeAfter(lastNudgeAt, gotIn, inAt);
                            if (gotIn)
                            {
                                Log("Nudged " + nudged + (nudged == 1 ? " Roblox window" : " Roblox windows") +
                                    (plan.Skipped > 0 ? ", skipped " + plan.Skipped : "") + " (" + reason + ").");
                                return;
                            }

                            // Kept out: tried again soon rather than an
                            // interval later, which with the idle kick at
                            // twenty minutes could be too late.
                            if (NudgePlan.RetrySoon(gotIn, chkAfk.Checked && nudgeTimer.Enabled))
                            {
                                nudgeTimer.Stop();
                                nudgeTimer.Interval = DEFER_RETRY_SECONDS * 1000;
                                nudgeTimer.Start();
                                nextNudge = DateTime.Now.AddSeconds(DEFER_RETRY_SECONDS);
                                Log("Couldn't nudge (" + reason + ") - a macro kept the keyboard for a minute. " +
                                    "Trying again in " + DEFER_RETRY_SECONDS + " seconds.");
                            }
                            else Log("Couldn't nudge (" + reason + ") - a macro kept the keyboard for a minute.");
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
                    Log("Roblox is open - anti-AFK started (every " + numInterval.Value + " min).");
            }
            else if (!shouldRun && nudgeTimer.Enabled)
            {
                nudgeTimer.Stop();
                if (!initializing && chkAfk.Checked)
                    Log("No Roblox window left to nudge - anti-AFK waits until one opens.");
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
