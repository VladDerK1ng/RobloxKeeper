using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The first control added to a panel is drawn on top of the ones after it.
    // A page's heading line runs the width of the page and is added before the
    // buttons at its top right, and it hid the bottom of those buttons: they
    // looked short, with their text sitting low. Nothing a person clicks or
    // types in may have another control drawn over it.
    static class OverlapTests
    {
        static List<string> CoveredIn(Control parent)
        {
            List<string> found = new List<string>();
            List<Control> kids = new List<Control>();
            foreach (Control k in parent.Controls) if (k.Visible && k.Width > 0 && k.Height > 0) kids.Add(k);
            for (int j = 0; j < kids.Count; j++)
            {
                Control below = kids[j];
                if (!(below is ButtonBase || below is TextBox || below is ThemedPicker)) continue;
                for (int i = 0; i < j; i++)
                {
                    Rectangle hit = Rectangle.Intersect(kids[i].Bounds, below.Bounds);
                    if (hit.Width > 1 && hit.Height > 1)
                        found.Add(below.GetType().Name + " \"" + below.Text + "\" under " + kids[i].GetType().Name + " \"" + kids[i].Text + "\"");
                }
            }
            foreach (Control k in kids) found.AddRange(CoveredIn(k));
            return found;
        }

        public static void TestNoPageHidesItsButtons()
        {
            using (MainForm f = new MainForm(false))
            {
                foreach (string key in new string[] { MainForm.PAGE_HOME, MainForm.PAGE_GAMES, MainForm.PAGE_FRIENDS,
                                                      MainForm.PAGE_AUTO, MainForm.PAGE_LOOKS, MainForm.PAGE_SHOTS })
                {
                    f.ShowPage(key);
                    List<string> covered = CoveredIn(f.PageNow);
                    Assert.Equal(0, covered.Count, key + ": " + string.Join("; ", covered.ToArray()));
                }
                f.ShowPage(MainForm.PAGE_KEEPER);
                List<string> keeper = CoveredIn(f);
                Assert.Equal(0, keeper.Count, "keeper: " + string.Join("; ", keeper.ToArray()));
            }
        }
    }
}
