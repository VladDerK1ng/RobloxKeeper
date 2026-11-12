using System;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Where the crosshair, the font picker and the on-screen extras are
    // reached from: three buttons in the head of the Looks page.
    static class LooksExtras
    {
        const int CROSS_W = 100, FONT_W = 70, MORE_W = 136, GAP = 8, H = 26;

        public static void AddTo(ShellPage page, IShell shell, LooksPlaces at)
        {
            Extras.UseShell(shell);
            // Flush with the right edge of the page's two columns of cards.
            int right = page.Width - CardLayout.PAD;
            int x = right - (CROSS_W + FONT_W + MORE_W + GAP * 2);

            // The title label is 30 px high at y = 10; centre the 26 px buttons on it.
            const int Y = 10 + (30 - H) / 2;

            Button cross = WatchUi.Secondary("Crosshair...", x, Y, CROSS_W, H);
            cross.Click += delegate { using (CrosshairDialog d = new CrosshairDialog(shell)) d.ShowDialog(page.FindForm()); };
            page.Controls.Add(cross);
            cross.BringToFront();

            Button font = WatchUi.Secondary("Font...", x + CROSS_W + GAP, Y, FONT_W, H);
            font.Click += delegate
            {
                using (FontPickDialog d = new FontPickDialog(shell, at)) d.ShowDialog(page.FindForm());
                // The font window changes the list of packs on disk. The page
                // reads that list when it comes into view and keeps it; left
                // holding the older one, its next save would write the font
                // pack out of the list (seen live). So it comes into view again.
                if (page.IsShowing) { page.GoOut(); page.ComeIn(); }
            };
            page.Controls.Add(font);
            font.BringToFront();

            Button more = WatchUi.Secondary("On-screen extras...", x + CROSS_W + FONT_W + GAP * 2, Y, MORE_W, H);
            more.Click += delegate { using (ExtrasDialog d = new ExtrasDialog(shell)) d.ShowDialog(page.FindForm()); };
            page.Controls.Add(more);
            more.BringToFront();
        }
    }
}
