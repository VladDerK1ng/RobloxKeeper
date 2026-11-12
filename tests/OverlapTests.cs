using System;
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

        // A 1366x768 laptop, or a 1024x768 one: the window is shorter than it
        // was laid out for. Every page still reaches its last card - it
        // scrolls - and the line at its foot stays below it.
        public static void TestOnAShortScreenEveryPageScrollsToItsEnd()
        {
            MainForm.ScreenHeightForTests = 728;
            try
            {
                using (MainForm f = new MainForm(false))
                {
                    foreach (string key in new string[] { MainForm.PAGE_HOME, MainForm.PAGE_GAMES, MainForm.PAGE_FRIENDS,
                                                          MainForm.PAGE_AUTO, MainForm.PAGE_LOOKS, MainForm.PAGE_SHOTS })
                    {
                        f.ShowPage(key);
                        ShellPage page = f.PageNow;
                        foreach (Control c in page.Controls)
                        {
                            if (c.Top == page.FootTop) continue;        // the foot line itself
                            Assert.True(c.Bottom <= page.FootTop, key + ": " + c.GetType().Name + " \"" + c.Text + "\" ends above the foot line");
                        }
                        Assert.True(page.FootTop + ShellPage.FOOT_H <= Math.Max(page.Height, page.AutoScrollMinSize.Height),
                                    key + ": the foot line can be scrolled to");
                        List<string> covered = CoveredIn(page);
                        Assert.Equal(0, covered.Count, key + ": " + string.Join("; ", covered.ToArray()));
                    }
                }
            }
            finally { MainForm.ScreenHeightForTests = 0; }
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
