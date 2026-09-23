using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // A nudge on a client on another virtual desktop brings you back to the
    // one you were on - asked for by the owner.
    //
    // Bringing a client in front switches Windows to its desktop. Giving focus
    // back to the window you had only switches back if that window lives on
    // one desktop: the wallpaper and the taskbar are on all of them, and the
    // app's own window wasn't given focus back at all. So which desktop you
    // were on is remembered, and if you aren't there afterwards, the nudge
    // steps back with Windows' own Win+Ctrl+arrow, the right number of times.
    static class DesktopReturnTests
    {
        static readonly Guid A = new Guid("92654adc-96cc-4e38-bfb1-48d74c1c0faf");
        static readonly Guid B = new Guid("45679d32-5247-4690-b10d-9f901366ed99");
        static readonly Guid C = new Guid("11111111-2222-3333-4444-555555555555");

        // Windows keeps the list as one blob of 16-byte ids, in order.
        public static void TestTheDesktopListIsReadInOrder()
        {
            List<byte> blob = new List<byte>();
            blob.AddRange(A.ToByteArray());
            blob.AddRange(B.ToByteArray());
            IList<Guid> order = VirtualDesktops.ParseIds(blob.ToArray());
            Assert.Equal(2, order.Count, "two desktops");
            Assert.True(order[0] == A && order[1] == B, "in Windows' order");
            Assert.Equal(0, VirtualDesktops.ParseIds(null).Count, "none when there is no list");
            Assert.Equal(1, VirtualDesktops.ParseIds(new byte[20]).Count, "a stray tail is ignored");
        }

        public static void TestStepsBackToTheDesktopYouWereOn()
        {
            List<Guid> order = new List<Guid> { A, B, C };
            Assert.Equal(-1, VirtualDesktops.StepsBack(order, A, B), "one to the left");
            Assert.Equal(2, VirtualDesktops.StepsBack(order, C, A), "two to the right");
            Assert.Equal(0, VirtualDesktops.StepsBack(order, B, B), "already there");
        }

        // Anything unknown - a desktop closed meanwhile, no list at all - and
        // it stays put rather than guessing a direction.
        public static void TestNothingIsGuessed()
        {
            List<Guid> order = new List<Guid> { A, B };
            Assert.Equal(0, VirtualDesktops.StepsBack(order, C, A), "home not in the list");
            Assert.Equal(0, VirtualDesktops.StepsBack(order, A, Guid.Empty), "where it is now unknown");
            Assert.Equal(0, VirtualDesktops.StepsBack(new List<Guid>(), A, B), "no list");
        }
    }
}
