using System;

namespace RobloxKeeper
{
    enum NudgeDecision
    {
        Go,       // nothing to interrupt - nudge now
        Defer,    // the user is playing; try again shortly
        Urgent    // out of time; nudge despite the interruption, after warning
    }

    // When it is acceptable to take the foreground.
    //
    // A nudge focuses each client for roughly a second. In a competitive game
    // that is a lost fight, and it was the single most disruptive thing the app
    // did. So it waits for a lull - and stops waiting when a client is close
    // enough to Roblox's 20-minute idle kick that waiting would cost the
    // account instead of a round.
    static class NudgePolicy
    {
        // How long a pause has to be before a nudge may take the foreground.
        //
        // This was 45 seconds, which was too cautious to be useful: most of the
        // time the window in front is a browser or a chat app, where a 300ms
        // flicker costs nothing, and waiting three quarters of a minute meant
        // nudges were deferred over and over. Seconds is the right scale -
        // AntiAFK-RBX uses three for the same check.
        //
        // What makes that safe here is the fullscreen rule below, which that
        // project has no equivalent of: a game in front is protected outright
        // regardless of how idle the keyboard looks. The two belong together.
        public static readonly TimeSpan DefaultQuiet = TimeSpan.FromSeconds(5);

        // Roblox kicks at 20 minutes - measured, disconnect reason 278. This
        // leaves a minute to actually perform the nudge.
        public static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(19);

        public static NudgeDecision Decide(TimeSpan userIdleFor, TimeSpan sinceLastNudge,
                                           TimeSpan quietThreshold, TimeSpan deadline)
        {
            return Decide(userIdleFor, sinceLastNudge, quietThreshold, deadline, false);
        }

        public static NudgeDecision Decide(TimeSpan userIdleFor, TimeSpan sinceLastNudge,
                                           TimeSpan quietThreshold, TimeSpan deadline,
                                           bool fullscreenGameInFront)
        {
            // A fullscreen game counts as playing even when no key has been
            // pressed for minutes - watching a cutscene, holding a position,
            // lining up a shot. GetLastInputInfo cannot tell that from being
            // away from the desk, so the window in front gets the benefit of
            // the doubt until the deadline says otherwise.
            bool quiet = userIdleFor >= quietThreshold && !fullscreenGameInFront;

            // Idle: nothing to protect, so the interval alone governs.
            if (quiet) return NudgeDecision.Go;

            // Busy, and the kick is due: the account outranks the match, but the
            // user gets told first.
            if (sinceLastNudge >= deadline) return NudgeDecision.Urgent;

            return NudgeDecision.Defer;
        }

        // Is the window in front covering the entire screen?
        //
        // Deliberately stricter than "maximised": a maximised window stops at
        // the taskbar, and interrupting one costs nothing worth protecting. A
        // window at or beyond the screen bounds is the exclusive or borderless
        // fullscreen case - a game.
        public static bool LooksFullscreen(System.Drawing.Rectangle window,
                                           System.Drawing.Rectangle screen)
        {
            if (window.Width <= 0 || window.Height <= 0) return false;
            return window.Left <= screen.Left
                && window.Top <= screen.Top
                && window.Right >= screen.Right
                && window.Bottom >= screen.Bottom;
        }

        // The same question about the window actually in front right now.
        public static bool FullscreenGameInFront()
        {
            try
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) return false;

                System.Drawing.Rectangle window = System.Drawing.Rectangle.FromLTRB(
                    r.Left, r.Top, r.Right, r.Bottom);
                System.Drawing.Rectangle screen =
                    System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;

                return LooksFullscreen(window, screen);
            }
            catch { return false; }
        }

        // How long since the user last touched keyboard or mouse, machine-wide.
        // GetLastInputInfo is the only measure that sees input to other
        // applications - a game in the foreground is exactly the case that
        // matters here, and this app receives none of its input.
        public static TimeSpan UserIdleFor()
        {
            try
            {
                Native.LASTINPUTINFO info = new Native.LASTINPUTINFO();
                info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(Native.LASTINPUTINFO));
                if (!Native.GetLastInputInfo(ref info)) return TimeSpan.MaxValue;

                // Both are unsigned 32-bit tick counts that wrap every ~49 days.
                // Subtracting as uint and widening afterwards keeps the answer
                // right across the wrap; widening first does not.
                uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
                return TimeSpan.FromMilliseconds(elapsed);
            }
            catch
            {
                // Unable to tell - assume idle rather than never nudging again.
                return TimeSpan.MaxValue;
            }
        }
    }
}
