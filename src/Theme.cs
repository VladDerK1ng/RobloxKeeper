using System.Drawing;

namespace RobloxKeeper
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(24, 24, 37);
        public static readonly Color Card = Color.FromArgb(35, 35, 53);
        public static readonly Color Inset = Color.FromArgb(17, 17, 27);
        public static readonly Color Text = Color.FromArgb(205, 214, 244);
        public static readonly Color Muted = Color.FromArgb(127, 132, 156);
        public static readonly Color Accent = Color.FromArgb(122, 111, 240);
        public static readonly Color AccentHover = Color.FromArgb(148, 137, 250);
        // Plain buttons - Cancel, Send a test, Macros. Lighter than the cards
        // and the window they sit on, so they read as buttons; purple is kept
        // for each window's main action.
        public static readonly Color Button = Color.FromArgb(52, 52, 78);
        public static readonly Color ButtonHover = Color.FromArgb(68, 68, 100);
        public static readonly Color Green = Color.FromArgb(129, 216, 143);
        public static readonly Color Amber = Color.FromArgb(249, 226, 175);
        public static readonly Color LogFg = Color.FromArgb(147, 154, 183);
    }
}
