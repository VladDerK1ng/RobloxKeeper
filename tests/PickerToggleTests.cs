using System;

namespace RobloxKeeper.Tests
{
    // Clicking a dropdown that is already open should shut it.
    //
    // The first attempt at this failed for a reason worth recording: the list
    // was shown with ShowDialog, and a modal dialog DISABLES its owner window.
    // While the list was open the whole form was disabled, so a click on the
    // picker never arrived at all - there was nothing to guard against, and the
    // only way out was to pick a row or click another application.
    //
    // With a modeless list the click does arrive, but it arrives just after the
    // list has already closed itself on losing focus. So the picker has to tell
    // "the user wants to open this" from "this is the click that just closed
    // it", and the only thing separating them is a few milliseconds.
    static class PickerToggleTests
    {
        static readonly TimeSpan Guard = TimeSpan.FromMilliseconds(250);

        public static void TestAClickOpensAClosedList()
        {
            Assert.True(ThemedPicker.ShouldOpenOnClick(false, TimeSpan.FromSeconds(10), Guard),
                "nothing open, nothing just closed - open it");
        }

        // The click that dismissed the list must not reopen it.
        public static void TestTheClickThatClosedTheListDoesNotReopenIt()
        {
            Assert.False(ThemedPicker.ShouldOpenOnClick(false, TimeSpan.FromMilliseconds(10), Guard),
                "the list closed a moment ago because of this very click");
        }

        public static void TestReopeningIsAllowedOnceTheMomentHasPassed()
        {
            Assert.True(ThemedPicker.ShouldOpenOnClick(false, TimeSpan.FromMilliseconds(400), Guard),
                "a deliberate second click reopens it");
        }

        public static void TestTheGuardBoundaryCountsAsDeliberate()
        {
            Assert.True(ThemedPicker.ShouldOpenOnClick(false, Guard, Guard),
                "exactly at the threshold is a new click, not the old one");
        }

        // If the list somehow is still open when the click lands, closing it is
        // the answer - never opening a second one.
        public static void TestAnOpenListIsNeverOpenedAgain()
        {
            Assert.False(ThemedPicker.ShouldOpenOnClick(true, TimeSpan.FromSeconds(10), Guard),
                "already open");
            Assert.False(ThemedPicker.ShouldOpenOnClick(true, TimeSpan.FromMilliseconds(1), Guard),
                "already open, whatever the timing");
        }
    }
}
