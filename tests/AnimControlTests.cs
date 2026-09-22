using System;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The animated controls, and the one timer that drives them. This app
    // exists to stop Roblox wasting the machine, so anything that keeps
    // repainting or keeps a timer alive when nothing is moving is a bug, not
    // a cosmetic.
    static class AnimControlTests
    {
        // How many times drawing this once asks for it to be drawn again.
        // Asking from inside a paint means another paint, which asks again:
        // the control repaints for as long as it is on screen.
        static int RepaintsAskedForBy(Control c)
        {
            IntPtr make = c.Handle;             // painting needs a window
            using (Bitmap b = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));   // settle anything pending
                int asked = 0;
                InvalidateEventHandler count = delegate { asked++; };
                c.Invalidated += count;
                c.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
                c.Invalidated -= count;
                return asked;
            }
        }

        // Measured: each paint of a number box asked for two more, so every
        // one on screen repainted without end.
        public static void TestANumberBoxDoesNotRepaintItself()
        {
            using (ThemedNumeric n = new ThemedNumeric())
            {
                n.Width = 60;
                Assert.Equal(0, RepaintsAskedForBy(n), "drawn once, left alone");
            }
        }

        // And each dropdown asked for one more.
        public static void TestADropdownDoesNotRepaintItself()
        {
            using (ThemedPicker p = new ThemedPicker())
            {
                p.Width = 150;
                p.Items.Add("One");
                p.SelectedIndex = 0;
                Assert.Equal(0, RepaintsAskedForBy(p), "drawn once, left alone");
            }
        }

        public static void TestASwitchAndATickBoxDoNotRepaintThemselves()
        {
            using (ThemedToggle t = new ThemedToggle())
            using (ThemedCheckBox c = new ThemedCheckBox())
            {
                t.Text = "On";
                c.Text = "Tick";
                Assert.Equal(0, RepaintsAskedForBy(t), "the switch");
                Assert.Equal(0, RepaintsAskedForBy(c), "the tick box");
            }
        }

        // A pulse turns round from its own last frame. The driver decided
        // whether anything was moving BEFORE that frame, saw nothing, and
        // switched its timer off - with the next breath already under way.
        public static void TestAnAnimationRestartedFromItsOwnLastFrameKeepsTheTimerGoing()
        {
            using (Control c = new Control())
            {
                IntPtr make = c.Handle;
                DateTime t0 = DateTime.Now.AddMinutes(10);      // later than anything else running
                TimeSpan span = TimeSpan.FromMilliseconds(200);
                Anim a = new Anim(0);
                a.To(1, span, t0);
                bool turned = false;
                Animator.Run(c, a, delegate
                {
                    if (!a.Running && !turned) { turned = true; a.To(a.FarEndFrom(0, 1), span, t0.AddMilliseconds(200)); }
                });
                try
                {
                    Assert.True(Animator.StepAt(t0.AddMilliseconds(100)), "moving part way through");
                    Assert.True(Animator.StepAt(t0.AddMilliseconds(200)), "turned round on its last frame, so still moving");
                    Assert.True(turned, "and it did turn round");
                    Assert.False(Animator.StepAt(t0.AddMilliseconds(400)), "then done, and the timer can stop");
                }
                finally { Animator.Remove(c); }
            }
        }

        // The status dot was set busy and never moved.
        public static void TestABusyDotStartsBreathing()
        {
            if (!Animator.MotionWanted) return;     // Windows' animations are off on this PC
            using (Dot d = new Dot())
            {
                IntPtr make = d.Handle;
                d.Busy = true;
                try { Assert.True(d.Breathing, "breathing as soon as it is busy"); }
                finally { d.Busy = false; Animator.Remove(d); }
            }
        }
    }
}
