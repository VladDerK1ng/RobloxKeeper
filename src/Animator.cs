using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A number that slides from where it is to where it has been told to go.
    //
    // Every animation in the app is one of these driving a repaint. Keeping
    // the number separate from the drawing means the awkward parts - easing,
    // retargeting halfway, stopping on time - are ordinary arithmetic that
    // can be tested, rather than something you can only judge by squinting at
    // a window.
    //
    // The clock is passed in for the same reason.
    class Anim
    {
        double from, to, current;
        DateTime startedAt;
        TimeSpan duration;
        bool running;

        public Anim(double start)
        {
            current = from = to = start;
        }

        public double Value { get { return current; } }
        public bool Running { get { return running; } }

        // Where it is, as a fraction between two numbers. Convenient for the
        // common case of a 0-to-1 animation driving something else.
        public double Between(double a, double b)
        {
            return a + (b - a) * current;
        }

        // The end it is NOT currently at. A pulse asks for this every time it
        // turns round, rather than keeping its own idea of which way it is
        // going - that gets out of step with where the value actually is, and
        // a pulse that asks to go where it already is never starts at all.
        public double FarEndFrom(double low, double high)
        {
            return current > (low + high) / 2.0 ? low : high;
        }

        public void To(double target, TimeSpan howLong, DateTime now)
        {
            if (target == to && running) return;        // already on its way there
            if (target == current && !running) return;  // already there

            // Start from where it ACTUALLY is, not from where the last slide
            // began. Without this, hovering, leaving and hovering again makes
            // the control jump back to the start each time.
            from = current;
            to = target;
            startedAt = now;
            duration = howLong;

            if (howLong <= TimeSpan.Zero)
            {
                // Someone has turned animations off system-wide. Arrive, and
                // never start a timer for it.
                current = target;
                running = false;
                return;
            }
            running = true;
        }

        // Move it on to this moment. True while there is still more to do -
        // the driver uses that to switch its timer off, because a performance
        // tool has no business running a 60-per-second timer over a window
        // where nothing is moving.
        public bool Tick(DateTime now)
        {
            if (!running) return false;

            double elapsed = (now - startedAt).TotalMilliseconds;
            double total = duration.TotalMilliseconds;

            // The clock can go backwards - a manual change, or daylight
            // saving. Treat that as no time having passed rather than
            // sliding the animation into reverse.
            if (elapsed < 0) elapsed = 0;

            if (elapsed >= total)
            {
                current = to;
                running = false;
                return false;
            }

            current = from + (to - from) * Ease(elapsed / total);
            return true;
        }

        // Fast at first, settling gently. A linear slide is what makes an
        // interface feel mechanical, and this is the cheapest curve that does
        // not: one minus the cube of what is left.
        public static double Ease(double t)
        {
            if (t <= 0) return 0.0;
            if (t >= 1) return 1.0;
            double left = 1.0 - t;
            return 1.0 - left * left * left;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                Mix(a.A, b.A, t), Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
        }

        static int Mix(int a, int b, double t)
        {
            int v = (int)Math.Floor(a + (b - a) * t + 0.5);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }

    // Drives every running animation off one timer, and switches that timer
    // off the moment nothing is moving.
    //
    // One timer rather than one per control: a window with thirty animated
    // controls would otherwise have thirty timers, all waking the process at
    // slightly different moments. And switching it off matters more here than
    // in most applications - this app exists to stop Roblox wasting the
    // machine, so it cannot sit there repainting a window nobody is looking
    // at.
    static class Animator
    {
        // Roughly sixty a second. Smooth enough that nothing steps visibly,
        // and only ever running while something is actually moving.
        const int INTERVAL_MS = 16;

        // Short. An animation you notice waiting for is worse than none.
        public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(130);
        public static readonly TimeSpan Slide = TimeSpan.FromMilliseconds(190);

        static readonly List<Entry> entries = new List<Entry>();
        static Timer timer;

        class Entry
        {
            public Anim Anim;
            public Control Owner;       // watched so a closed window stops its animations
            public Action OnFrame;
            public double Drawn = double.NaN;   // the value it was last drawn at
        }

        // Whether the person using this wants interfaces to move at all.
        //
        // Windows has a system-wide setting for it, under Accessibility. If
        // it is off, every animation becomes an instant change - which every
        // control here already handles, because arriving instantly is just a
        // duration of zero.
        public static bool MotionWanted
        {
            get
            {
                try { return SystemInformation.UIEffectsEnabled; }
                catch { return true; }
            }
        }

        public static TimeSpan Time(TimeSpan wanted)
        {
            return MotionWanted ? wanted : TimeSpan.Zero;
        }

        // Repaint this control while this animation is moving.
        //
        // Safe to call repeatedly with the same pair; a control is only ever
        // registered once.
        public static void Follow(Control c, Anim a)
        {
            Run(c, a, null);
        }

        // The general form: do something on every frame while this animation
        // moves. Used where a repaint is not the thing being animated - a
        // window's opacity, for instance.
        public static void Run(Control owner, Anim a, Action onFrame)
        {
            if (owner == null || a == null) return;

            foreach (Entry e in entries)
                if (e.Anim == a && e.Owner == owner) { Start(); return; }

            Entry entry = new Entry();
            entry.Anim = a;
            entry.Owner = owner;
            entry.OnFrame = onFrame;
            entries.Add(entry);

            // A control that goes away must not keep its animation, or its
            // window handle, alive.
            owner.Disposed += delegate { Remove(owner); };
            Start();
        }

        public static void Remove(Control c)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Owner == c) entries.RemoveAt(i);
        }

        static void Start()
        {
            if (timer == null)
            {
                timer = new Timer();
                timer.Interval = INTERVAL_MS;
                timer.Tick += Step;
            }
            if (!timer.Enabled) timer.Enabled = true;
        }

        static void Step(object sender, EventArgs e)
        {
            if (!StepAt(DateTime.Now)) timer.Enabled = false;
        }

        // One frame of everything, at this moment. True while anything is
        // still moving, which is what keeps the timer on.
        public static bool StepAt(DateTime now)
        {
            bool anythingMoving = false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];

                // A disposed control cannot be repainted, and asking it to be
                // throws. Drop it instead.
                if (entry.Owner.IsDisposed) { entries.RemoveAt(i); continue; }

                // Drawn only if it was moving - which includes the last frame,
                // the one that settles it exactly on its final appearance - or
                // its value changed since it was last drawn, which is how a
                // change that arrives at once (animations off) is shown.
                // Everything else has settled and is left alone: drawing it
                // anyway repainted every control that had ever animated, sixty
                // times a second, whenever anything at all was moving.
                bool wasMoving = entry.Anim.Running;
                entry.Anim.Tick(now);
                if (wasMoving || entry.Anim.Value != entry.Drawn)
                {
                    entry.Drawn = entry.Anim.Value;
                    Frame(entry);
                }

                // Asked after the frame, not before: a frame can start the
                // next movement - a pulse turning round - and deciding first
                // switched the timer off with that movement already under way.
                if (entry.Anim.Running) anythingMoving = true;
            }

            return anythingMoving;
        }

        static void Frame(Entry entry)
        {
            if (!entry.Owner.IsHandleCreated) return;
            try
            {
                if (entry.OnFrame != null) entry.OnFrame();
                else entry.Owner.Invalidate();
            }
            catch
            {
                // A window closing underneath a frame. Not worth a crash over
                // something purely decorative.
            }
        }
    }
}
