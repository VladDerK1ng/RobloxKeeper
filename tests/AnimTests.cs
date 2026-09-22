using System;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // The value behind every animation: a number that slides from where it is
    // to where it has been told to go.
    //
    // Two things here are worth testing and easy to get wrong. Retargeting
    // mid-slide has to carry on from where it actually IS, or a control that
    // is hovered, unhovered and hovered again jumps. And it has to stop -
    // this is a performance tool, and an animation that never finishes keeps
    // a timer alive forever.
    static class AnimTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 22, 12, 0, 0);
        static DateTime At(int ms) { return T0.AddMilliseconds(ms); }
        static readonly TimeSpan Span = TimeSpan.FromMilliseconds(200);

        public static void TestItStartsWhereItWasPutAndIsNotRunning()
        {
            Anim a = new Anim(0.0);
            Assert.Equal(0.0, a.Value, "starts where it was put");
            Assert.False(a.Running, "and is not going anywhere");
        }

        public static void TestItReachesItsTargetExactlyAtTheEnd()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(200));
            Assert.Equal(1.0, a.Value, "exactly there, not nearly");
            Assert.False(a.Running, "and finished");
        }

        // An animation that is still running after its time is up keeps a
        // timer alive for nothing.
        public static void TestItStopsAndStaysStopped()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(5000));
            Assert.Equal(1.0, a.Value, "clamped, not overshot");
            Assert.False(a.Running, "stopped");
            Assert.False(a.Tick(At(9000)), "and ticking it again changes nothing");
        }

        public static void TestItIsPartWayAlongPartWayThrough()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(100));
            Assert.True(a.Value > 0.0 && a.Value < 1.0, "somewhere in between");
            Assert.True(a.Running, "still going");
        }

        // Eased, so it moves fast at first and settles gently - a linear
        // slide is what makes an animation feel mechanical.
        public static void TestItIsMoreThanHalfwayAtTheHalfwayPoint()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(100));
            Assert.True(a.Value > 0.5, "eased out, not linear");
        }

        // The one that stops a control jumping: hover, unhover, hover again in
        // quick succession must carry on from where it is.
        public static void TestRetargetingCarriesOnFromWhereItActuallyIs()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(100));
            double midway = a.Value;

            a.To(0.0, Span, At(100));
            Assert.Equal(midway, a.Value, "no jump at the moment it turns round");

            a.Tick(At(140));
            Assert.True(a.Value < midway, "and it is now heading back");
        }

        public static void TestRetargetingToWhereItIsAlreadyGoingDoesNotRestart()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(At(150));
            double far = a.Value;
            a.To(1.0, Span, At(150));
            a.Tick(At(160));
            Assert.True(a.Value >= far, "it kept going rather than starting again");
        }

        public static void TestAskingForWhereItAlreadyIsDoesNothing()
        {
            Anim a = new Anim(1.0);
            a.To(1.0, Span, T0);
            Assert.False(a.Running, "nothing to do");
        }

        // Windows has a system-wide setting for people who do not want
        // interfaces moving. Honouring it means duration zero, and duration
        // zero must land on the target immediately rather than divide by it.
        public static void TestZeroDurationArrivesImmediately()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, TimeSpan.Zero, T0);
            Assert.Equal(1.0, a.Value, "straight there");
            Assert.False(a.Running, "with nothing to animate");
        }

        public static void TestTimeGoingBackwardsDoesNotBreakIt()
        {
            Anim a = new Anim(0.0);
            a.To(1.0, Span, T0);
            a.Tick(T0.AddMilliseconds(-500));
            Assert.True(a.Value >= 0.0 && a.Value <= 1.0, "still a sane value");
        }

        // ---------- turning round ----------

        // A pulse asks for the far end every time it turns round. Keeping its
        // own idea of which way it is going gets out of step with where the
        // value actually is, and then it asks to go where it already is -
        // which is nothing at all, so the pulse never starts. That is exactly
        // how the status dot shipped not pulsing.
        public static void TestTheFarEndOfSomethingAtRestIsTheOtherEnd()
        {
            Assert.Equal(1.0, new Anim(0.0).FarEndFrom(0, 1), "from the bottom, go up");
            Assert.Equal(0.0, new Anim(1.0).FarEndFrom(0, 1), "from the top, go down");
        }

        public static void TestTheFarEndOfSomethingPartWayIsWhicheverIsFurther()
        {
            Assert.Equal(1.0, new Anim(0.2).FarEndFrom(0, 1), "nearer the bottom");
            Assert.Equal(0.0, new Anim(0.8).FarEndFrom(0, 1), "nearer the top");
        }

        // A pulse driven this way keeps going instead of stalling.
        public static void TestAskingForTheFarEndRepeatedlyKeepsItMoving()
        {
            Anim a = new Anim(0.0);
            int ms = 0;
            for (int turn = 0; turn < 4; turn++)
            {
                a.To(a.FarEndFrom(0, 1), Span, At(ms));
                Assert.True(a.Running, "turn " + turn + " actually started");
                ms += 200;
                a.Tick(At(ms));
            }
            Assert.Equal(0.0, a.Value, "four turns lands back where it began");
        }

        // ---------- easing ----------

        public static void TestEasingStartsAtNothingAndEndsAtEverything()
        {
            Assert.Equal(0.0, Anim.Ease(0.0), "nothing");
            Assert.Equal(1.0, Anim.Ease(1.0), "everything");
        }

        public static void TestEasingNeverGoesBackwards()
        {
            double last = -1;
            for (int i = 0; i <= 100; i++)
            {
                double v = Anim.Ease(i / 100.0);
                Assert.True(v >= last, "never turns back at " + i);
                last = v;
            }
        }

        public static void TestEasingIsClampedOutsideItsRange()
        {
            Assert.Equal(0.0, Anim.Ease(-3.0), "before the start");
            Assert.Equal(1.0, Anim.Ease(7.0), "after the end");
        }

        // ---------- colour ----------

        public static void TestBlendingEndsAreTheColoursThemselves()
        {
            Color a = Color.FromArgb(255, 10, 20, 30);
            Color b = Color.FromArgb(255, 200, 100, 50);
            Assert.Equal(a.ToArgb(), Anim.Blend(a, b, 0.0).ToArgb(), "all of the first");
            Assert.Equal(b.ToArgb(), Anim.Blend(a, b, 1.0).ToArgb(), "all of the second");
        }

        public static void TestBlendingHalfwayIsHalfway()
        {
            Color mid = Anim.Blend(Color.FromArgb(255, 0, 0, 0),
                                   Color.FromArgb(255, 100, 200, 50), 0.5);
            Assert.Equal(50, (int)mid.R, "red");
            Assert.Equal(100, (int)mid.G, "green");
            Assert.Equal(25, (int)mid.B, "blue");
        }

        public static void TestBlendingIsClampedSoAColourIsAlwaysValid()
        {
            Color a = Color.FromArgb(255, 10, 20, 30);
            Color b = Color.FromArgb(255, 200, 100, 50);
            Assert.Equal(a.ToArgb(), Anim.Blend(a, b, -5.0).ToArgb(), "before the start");
            Assert.Equal(b.ToArgb(), Anim.Blend(a, b, 9.0).ToArgb(), "after the end");
        }

        public static void TestBlendingCarriesAlphaToo()
        {
            Color mid = Anim.Blend(Color.FromArgb(0, 255, 255, 255),
                                   Color.FromArgb(200, 255, 255, 255), 0.5);
            Assert.Equal(100, (int)mid.A, "half as transparent");
        }

        // ---------- moving between two numbers ----------

        public static void TestItSlidesBetweenAnyTwoNumbersNotJustZeroAndOne()
        {
            Anim a = new Anim(10.0);
            a.To(30.0, Span, T0);
            a.Tick(At(200));
            Assert.Equal(30.0, a.Value, "arrived");
        }

        public static void TestItCanSlideDownwards()
        {
            Anim a = new Anim(1.0);
            a.To(0.0, Span, T0);
            a.Tick(At(100));
            Assert.True(a.Value < 0.5, "eased out on the way down as well");
            a.Tick(At(200));
            Assert.Equal(0.0, a.Value, "arrived");
        }
    }
}
