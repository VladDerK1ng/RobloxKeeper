using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // Scrolling past a setting must not change it.
    //
    // Windows sends the mouse wheel to whatever is under the pointer, focused
    // or not. The dropdowns and number boxes used to step on every notch, so
    // scrolling the window or the activity list with the pointer resting on
    // one quietly changed a setting and saved it - which is how a Performance
    // default can end up on something nobody picked.
    static class WheelTests
    {
        class WheelPicker : ThemedPicker
        {
            public void Wheel(int delta) { OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 5, 5, delta)); }
        }

        class WheelNumber : ThemedNumeric
        {
            public void Wheel(int delta) { OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 5, 5, delta)); }
        }

        public static void TestScrollingOverADropdownLeavesItAlone()
        {
            using (WheelPicker p = new WheelPicker())
            {
                p.Items.Add("Low"); p.Items.Add("Below normal"); p.Items.Add("Normal");
                p.SelectedIndex = 2;
                bool changed = false;
                p.SelectedIndexChanged += delegate { changed = true; };

                p.Wheel(120);
                p.Wheel(-120);
                p.Wheel(120);

                Assert.Equal(2, p.SelectedIndex, "scrolling past it is not a choice");
                Assert.False(changed, "and nothing was told it changed");
            }
        }

        public static void TestScrollingOverANumberBoxLeavesItAlone()
        {
            using (WheelNumber n = new WheelNumber())
            {
                n.Minimum = 1; n.Maximum = 19; n.Value = 15;
                bool changed = false;
                n.ValueChanged += delegate { changed = true; };

                n.Wheel(120);
                n.Wheel(120);

                Assert.Equal(15, n.Value, "the nudge interval cannot creep toward Roblox's idle kick");
                Assert.False(changed, "and nothing was told it changed");
            }
        }
    }
}
