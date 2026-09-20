using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The dropdown threw away roughly half the selections made in it.
    //
    // Clicking a row set DialogResult = OK, which starts closing a modal form.
    // Closing deactivates it, which fired the Deactivate handler that exists to
    // dismiss the list when you click elsewhere - and that handler overwrote the
    // result with Cancel. The caller asked for OK, got Cancel, and discarded the
    // choice. Whether it survived depended on activation timing, which is why
    // changing the nudge keys or the CPU settings worked only sometimes.
    static class PickerTests
    {
        static List<string> Items()
        {
            List<string> items = new List<string>();
            items.Add("Zoom out + in  (O, I)");
            items.Add("Turn camera  (left right)");
            items.Add("Jump  (Space)");
            return items;
        }

        public static void TestClickingAwayWithoutChoosingCancels()
        {
            using (PickerPopup pop = new PickerPopup(Items(), 0, 200))
            {
                Assert.True(pop.ShouldCancelOnDeactivate,
                    "nothing was chosen, so losing focus should dismiss the list");
            }
        }

        // The regression.
        public static void TestAChoiceSurvivesTheDeactivateThatClosingCauses()
        {
            using (PickerPopup pop = new PickerPopup(Items(), 0, 200))
            {
                pop.Chosen = 2;

                Assert.False(pop.ShouldCancelOnDeactivate,
                    "a row was clicked, so the deactivation that closing causes must not cancel it");
            }
        }

        public static void TestChoosingTheFirstRowIsNotMistakenForNoChoice()
        {
            // Index 0 is a real selection - guarding on "Chosen != 0" or truthiness
            // would silently drop the first item in every dropdown.
            using (PickerPopup pop = new PickerPopup(Items(), 1, 200))
            {
                pop.Chosen = 0;

                Assert.False(pop.ShouldCancelOnDeactivate,
                    "row 0 is a genuine selection, not 'nothing chosen'");
            }
        }

        // The picker itself must accept a choice and tell anyone listening.
        public static void TestPickerRaisesChangedWhenTheSelectionMoves()
        {
            using (ThemedPicker picker = new ThemedPicker())
            {
                picker.Items.AddRange(Items());
                int raised = 0;
                picker.SelectedIndexChanged += delegate { raised++; };

                picker.SelectedIndex = 2;

                Assert.Equal(2, picker.SelectedIndex, "selection took");
                Assert.Equal(1, raised, "listeners told exactly once");
                Assert.Contains("Jump", picker.Text, "Text follows the selection");
            }
        }

        public static void TestPickerIgnoresAReselectionOfTheSameRow()
        {
            using (ThemedPicker picker = new ThemedPicker())
            {
                picker.Items.AddRange(Items());
                picker.SelectedIndex = 1;
                int raised = 0;
                picker.SelectedIndexChanged += delegate { raised++; };

                picker.SelectedIndex = 1;

                Assert.Equal(0, raised, "re-picking the current row is not a change");
            }
        }
    }
}
