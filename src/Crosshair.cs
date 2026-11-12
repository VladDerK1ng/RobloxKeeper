using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace RobloxKeeper
{
    enum CrossShape { Cross, Dot, Circle, Tee, Picture }

    // One crosshair, as it was chosen in the editor. Every number is in
    // real pixels on the screen.
    class CrossStyle
    {
        public const int MIN_SIZE = 2, MAX_SIZE = 60, MAX_GAP = 30, MIN_THICK = 1, MAX_THICK = 10,
                         MIN_OPACITY = 10, MAX_OPACITY = 100, MAX_NUDGE = 300,
                         MIN_PICTURE = 16, MAX_PICTURE = 256;

        public CrossShape Shape = CrossShape.Cross;
        public int Size = 10;                   // how long an arm is; a dot's or circle's radius
        public int Gap = 4;                     // the hole in the middle of a cross
        public int Thick = 2;
        public Color Colour = Color.FromArgb(0, 255, 80);
        public bool Outline = true;             // a dark edge, so it shows on snow as it does on night
        public bool Dot;                        // a dot in the middle of a cross or circle
        public int Opacity = 100;               // percent
        public string Picture = "";             // the user's own picture, for CrossShape.Picture
        public int PictureSize = 64;            // its longer side on screen
        public int NudgeX, NudgeY;              // moved off the exact middle, for games whose own middle is elsewhere
        public bool AimOnly;                    // only while the game holds the mouse in its middle (first person, shift-lock)

        public CrossStyle Clone() { return (CrossStyle)MemberwiseClone(); }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // This crosshair as it must be drawn while the screen is magnified
        // `zoom` times, so that once magnified it is about the size chosen.
        // Never thinner or shorter than a pixel, and a gap that was there
        // stays there (no gap at all fills the middle, a different look).
        //
        // The nudge is NOT divided. The magnifier grows everything about
        // the game's middle, the crosshair's window included: a point n
        // pixels off the middle shows n * zoom off it, and so does the
        // game's own point under it. Dividing the nudge would pull the
        // crosshair off the spot it was nudged onto.
        //
        // Not Tidy()'d afterwards: the editor's least size would undo it.
        public CrossStyle ScaledFor(int zoom)
        {
            CrossStyle s = Clone();
            if (zoom <= 1) return s;
            s.Size = Math.Max(1, Div(Size, zoom));
            s.Gap = Gap > 0 ? Math.Max(1, Div(Gap, zoom)) : 0;
            s.Thick = Math.Max(1, Div(Thick, zoom));
            s.PictureSize = Math.Max(1, Div(PictureSize, zoom));
            return s;
        }

        static int Div(int v, int by) { return (int)Math.Round(v / (double)by, MidpointRounding.AwayFromZero); }

        // Whatever was read from a file is brought back inside what the
        // editor allows, so a hand-edited number can't make a window the
        // size of the screen.
        public void Tidy()
        {
            if (!Enum.IsDefined(typeof(CrossShape), Shape)) Shape = CrossShape.Cross;
            Size = Clamp(Size, MIN_SIZE, MAX_SIZE);
            Gap = Clamp(Gap, 0, MAX_GAP);
            Thick = Clamp(Thick, MIN_THICK, MAX_THICK);
            Opacity = Clamp(Opacity, MIN_OPACITY, MAX_OPACITY);
            PictureSize = Clamp(PictureSize, MIN_PICTURE, MAX_PICTURE);
            NudgeX = Clamp(NudgeX, -MAX_NUDGE, MAX_NUDGE);
            NudgeY = Clamp(NudgeY, -MAX_NUDGE, MAX_NUDGE);
            if (Picture == null) Picture = "";
            Colour = Color.FromArgb(255, Colour.R, Colour.G, Colour.B);
        }

        // ---------- as lines of a settings file ----------

        public void Write(IDictionary<string, string> into)
        {
            into["cross.shape"] = Shape.ToString();
            into["cross.size"] = Size.ToString(CultureInfo.InvariantCulture);
            into["cross.gap"] = Gap.ToString(CultureInfo.InvariantCulture);
            into["cross.thick"] = Thick.ToString(CultureInfo.InvariantCulture);
            into["cross.colour"] = Hex(Colour);
            into["cross.outline"] = Outline ? "1" : "0";
            into["cross.dot"] = Dot ? "1" : "0";
            into["cross.opacity"] = Opacity.ToString(CultureInfo.InvariantCulture);
            into["cross.picture"] = Picture ?? "";
            into["cross.picturesize"] = PictureSize.ToString(CultureInfo.InvariantCulture);
            into["cross.nudgex"] = NudgeX.ToString(CultureInfo.InvariantCulture);
            into["cross.nudgey"] = NudgeY.ToString(CultureInfo.InvariantCulture);
            into["cross.aimonly"] = AimOnly ? "1" : "0";
        }

        public static CrossStyle Read(IDictionary<string, string> from)
        {
            CrossStyle s = new CrossStyle();
            string v;
            if (from.TryGetValue("cross.shape", out v))
            {
                try { s.Shape = (CrossShape)Enum.Parse(typeof(CrossShape), v, true); } catch { }
            }
            s.Size = Int(from, "cross.size", s.Size);
            s.Gap = Int(from, "cross.gap", s.Gap);
            s.Thick = Int(from, "cross.thick", s.Thick);
            if (from.TryGetValue("cross.colour", out v)) s.Colour = FromHex(v, s.Colour);
            s.Outline = Int(from, "cross.outline", s.Outline ? 1 : 0) != 0;
            s.Dot = Int(from, "cross.dot", 0) != 0;
            s.Opacity = Int(from, "cross.opacity", s.Opacity);
            if (from.TryGetValue("cross.picture", out v)) s.Picture = v;
            s.PictureSize = Int(from, "cross.picturesize", s.PictureSize);
            s.NudgeX = Int(from, "cross.nudgex", 0);
            s.NudgeY = Int(from, "cross.nudgey", 0);
            s.AimOnly = Int(from, "cross.aimonly", 0) != 0;
            s.Tidy();
            return s;
        }

        static int Int(IDictionary<string, string> from, string key, int fallback)
        {
            string v;
            int n;
            return from.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }

        public static string Hex(Color c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static Color FromHex(string hex, Color fallback)
        {
            int n;
            if (hex == null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return fallback;
            return Color.FromArgb(255, (n >> 16) & 255, (n >> 8) & 255, n & 255);
        }
    }

    // A crosshair to start from.
    class CrossPreset
    {
        public string Name;
        public CrossStyle Style;

        public CrossPreset(string name, CrossShape shape, int size, int gap, int thick, Color colour, bool dot)
        {
            Name = name;
            Style = new CrossStyle();
            Style.Shape = shape; Style.Size = size; Style.Gap = gap; Style.Thick = thick; Style.Colour = colour; Style.Dot = dot;
        }
    }

    static class CrossPresets
    {
        public static readonly Color Green = Color.FromArgb(0, 255, 80), Red = Color.FromArgb(255, 50, 50),
                                     Cyan = Color.FromArgb(0, 230, 255), Yellow = Color.FromArgb(255, 230, 0),
                                     Pink = Color.FromArgb(255, 80, 200);

        // The colours offered as squares to click. "Other..." opens Windows'
        // own colour window for anything else.
        public static readonly Color[] Colours = { Color.White, Green, Red, Cyan, Yellow, Pink, Color.FromArgb(255, 140, 0), Color.Black };

        public static readonly CrossPreset[] All =
        {
            new CrossPreset("Classic green", CrossShape.Cross, 10, 4, 2, Green, false),
            // Two pixels thick, not one: a one-pixel line between two edge
            // pixels was more edge than colour.
            new CrossPreset("Small white", CrossShape.Cross, 5, 3, 2, Color.White, false),
            new CrossPreset("Red dot", CrossShape.Dot, 3, 0, 2, Red, false),
            new CrossPreset("Cross with dot", CrossShape.Cross, 9, 5, 2, Cyan, true),
            new CrossPreset("Ring", CrossShape.Circle, 12, 0, 2, Color.White, true),
            new CrossPreset("T sight", CrossShape.Tee, 12, 4, 2, Yellow, false),
            new CrossPreset("Big and bold", CrossShape.Cross, 18, 6, 4, Pink, false)
        };
    }

    // The crosshair as a picture: an odd number of pixels across, so that
    // one pixel is its exact middle.
    static class CrossArt
    {
        // Two-thirds black, not solid: a solid edge on each side of a thin
        // line outweighed the line itself, and on a bright game a white
        // ring read as two black ones. This still shows it on snow.
        static readonly Color Edge = Color.FromArgb(170, 0, 0, 0);

        // How many pixels across the picture of this crosshair is.
        public static int SideOf(CrossStyle s)
        {
            if (s.Shape == CrossShape.Picture) return Math.Max(3, s.PictureSize | 1);
            int reach = s.Shape == CrossShape.Dot ? s.Size : s.Gap + s.Size;
            if (s.Shape == CrossShape.Circle) reach = s.Size + s.Thick;
            return (reach + s.Thick + 3) * 2 + 1;
        }

        // Where the middle of the drawn shape is, counted in half pixels from
        // the picture's top-left corner, across and down. An even number is
        // the line between two pixels; an odd one is the middle of a pixel.
        //
        // The game's own middle is on the line between two pixels when its
        // picture is an even number of pixels across (1920: between 959 and
        // 960), and in the middle of a pixel when it is odd (1111: pixel
        // 555). A cross of even thickness is centred on a line, an odd one on
        // a pixel - that is fixed by the thickness. The smoothed shapes (dot,
        // circle, the user's picture) are drawn to match the game instead:
        // evenWide/evenHigh say whether its picture is even across and down.
        public static Point Middle2(CrossStyle s, bool evenWide, bool evenHigh)
        {
            int c = SideOf(s) / 2;
            if (s.Shape == CrossShape.Cross || s.Shape == CrossShape.Tee)
            {
                int m = s.Thick % 2 == 0 ? 2 * c : 2 * c + 1;
                return new Point(m, m);
            }
            return new Point(evenWide ? 2 * c : 2 * c + 1, evenHigh ? 2 * c : 2 * c + 1);
        }

        // Null only for a picture of the user's that can't be read; why says
        // what was wrong with it. Smoothed shapes centred on a pixel.
        public static Bitmap Draw(CrossStyle s, out string why)
        {
            return Draw(s, false, false, out why);
        }

        // The same, for a game whose picture is this even or odd across and
        // down (see Middle2).
        public static Bitmap Draw(CrossStyle s, bool evenWide, bool evenHigh, out string why)
        {
            why = null;
            if (s.Shape == CrossShape.Picture) return DrawPicture(s, evenWide, evenHigh, out why);

            int side = SideOf(s), c = side / 2;
            Bitmap b = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            if (s.Shape == CrossShape.Dot || s.Shape == CrossShape.Circle)
            {
                // Twice: a dark shape one pixel bigger all round, then the
                // colour on top of it.
                Point m2 = Middle2(s, evenWide, evenHigh);
                int[] px = new int[side * side];
                if (s.Outline) Smooth(px, side, s, m2, 1, Edge);
                Smooth(px, side, s, m2, 0, s.Colour);
                BitmapData d = b.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length); }
                finally { b.UnlockBits(d); }
                return b;
            }
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                if (s.Outline) Paint(g, s, c, 1, Edge);
                Paint(g, s, c, 0, s.Colour);
            }
            return b;
        }

        // A dot, or a ring and its dot, smoothed - worked out here rather
        // than by GDI+, whose smoothing is not the same on both sides of a
        // circle (measured: 223 on one edge pixel, 247 on its mirror), so a
        // dot drawn by it looked a little off its middle.
        //
        // Each pixel is tried at 8 x 8 points spread evenly over it and
        // inked by the share inside. Distances are counted in sixteenths of
        // a pixel, whole numbers, so a pixel and its mirror come out the same.
        static void Smooth(int[] px, int side, CrossStyle s, Point middle2, int grow, Color colour)
        {
            if (s.Shape == CrossShape.Dot) { Ring(px, side, middle2, 0, s.Size + grow, colour); return; }
            float half = s.Thick / 2f;
            Ring(px, side, middle2, Math.Max(0f, s.Size - half - grow), s.Size + half + grow, colour);
            if (s.Dot) Ring(px, side, middle2, 0, Math.Max(1f, s.Thick * 0.75f) + grow, colour);
        }

        static void Ring(int[] px, int side, Point middle2, float inner, float outer, Color colour)
        {
            const int N = 8;
            double in2 = inner * 16.0 * inner * 16.0, out2 = outer * 16.0 * outer * 16.0;
            int mx = middle2.X * 8, my = middle2.Y * 8;        // the middle, in sixteenths
            int lo = Math.Max(0, (int)Math.Floor(middle2.X / 2.0 - outer) - 1), hi = Math.Min(side - 1, (int)Math.Ceiling(middle2.X / 2.0 + outer) + 1);
            int top = Math.Max(0, (int)Math.Floor(middle2.Y / 2.0 - outer) - 1), bottom = Math.Min(side - 1, (int)Math.Ceiling(middle2.Y / 2.0 + outer) + 1);
            for (int y = top; y <= bottom; y++)
                for (int x = lo; x <= hi; x++)
                {
                    int hits = 0;
                    for (int j = 0; j < N; j++)
                    {
                        long dy = 16 * y + 2 * j + 1 - my;
                        for (int i = 0; i < N; i++)
                        {
                            long dx = 16 * x + 2 * i + 1 - mx;
                            long d2 = dx * dx + dy * dy;
                            if (d2 <= out2 && d2 >= in2) hits++;
                        }
                    }
                    if (hits > 0) px[y * side + x] = Over(px[y * side + x], colour, hits / (double)(N * N));
                }
        }

        // colour, this much of it, laid over what a pixel already has.
        static int Over(int under, Color colour, double share)
        {
            double sa = colour.A / 255.0 * share, ua = ((under >> 24) & 255) / 255.0;
            double a = sa + ua * (1 - sa);
            if (a <= 0) return 0;
            double r = (colour.R * sa + ((under >> 16) & 255) * ua * (1 - sa)) / a;
            double g = (colour.G * sa + ((under >> 8) & 255) * ua * (1 - sa)) / a;
            double b = (colour.B * sa + (under & 255) * ua * (1 - sa)) / a;
            return ((int)Math.Round(a * 255) << 24) | ((int)Math.Round(r) << 16) | ((int)Math.Round(g) << 8) | (int)Math.Round(b);
        }

        static void Paint(Graphics g, CrossStyle s, int c, int grow, Color colour)
        {
            using (SolidBrush ink = new SolidBrush(colour))
            {
                if (s.Shape == CrossShape.Cross || s.Shape == CrossShape.Tee)
                {
                    // Straight lines are whole pixels, never smoothed: a
                    // smoothed one-pixel line is two grey ones.
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    // Everything is measured from one middle, so the arms and
                    // the bar they belong to cannot drift apart (they did, by
                    // a pixel, and the cross looked lopsided). An even
                    // thickness has its middle on the line between two
                    // pixels; an odd one has it in the middle pixel, and the
                    // far-side arms start one pixel later to leave that pixel
                    // in the centre of the gap.
                    int t = s.Thick, bar = c - t / 2;
                    int lo = c, hi = t % 2 == 0 ? c : c + 1;
                    int g2 = grow * 2;
                    // left, right, down - and up, unless it is a T
                    Fill(g, ink, lo - s.Gap - s.Size - grow, bar - grow, s.Size + g2, t + g2);
                    Fill(g, ink, hi + s.Gap - grow, bar - grow, s.Size + g2, t + g2);
                    Fill(g, ink, bar - grow, hi + s.Gap - grow, t + g2, s.Size + g2);
                    if (s.Shape == CrossShape.Cross)
                        Fill(g, ink, bar - grow, lo - s.Gap - s.Size - grow, t + g2, s.Size + g2);
                    // With no gap the arms meet round an empty middle; it is filled.
                    if (s.Dot || s.Gap == 0) Fill(g, ink, bar - grow, bar - grow, t + g2, t + g2);
                }
            }
        }

        static void Fill(Graphics g, Brush ink, int x, int y, int w, int h)
        {
            if (w > 0 && h > 0) g.FillRectangle(ink, x, y, w, h);
        }

        // The user's own picture, fitted inside a square with its middle on
        // the square's middle - on a line between pixels or on a pixel, as
        // Middle2 says. It is put down on whole pixels rather than smoothed
        // half a pixel over: one side is made a pixel shorter (or longer)
        // when that is what puts its middle in the right place.
        static Bitmap DrawPicture(CrossStyle s, bool evenWide, bool evenHigh, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(s.Picture)) { why = "no picture has been chosen yet"; return null; }
            try
            {
                FileInfo f = new FileInfo(s.Picture);
                if (!f.Exists) { why = "the picture is no longer where it was"; return null; }
                if (f.Length > 8 * 1024 * 1024) { why = "the picture is bigger than 8 MB"; return null; }
                // Read into memory first, so the file is not held open.
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(s.Picture)))
                using (Image src = Image.FromStream(ms))
                {
                    int side = SideOf(s);
                    float scale = Math.Min(side / (float)src.Width, side / (float)src.Height);
                    int c = side / 2;
                    int w = Matched(Math.Max(1, (int)Math.Round(src.Width * scale)), evenWide);
                    int h = Matched(Math.Max(1, (int)Math.Round(src.Height * scale)), evenHigh);
                    Bitmap b = new Bitmap(side, side, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // Even: c - w/2 to c + w/2 - 1, its middle the line at c.
                        // Odd: c - w/2 to c + w/2, its middle pixel c.
                        g.DrawImage(src, c - w / 2, c - h / 2, w, h);
                    }
                    return b;
                }
            }
            catch (Exception) { why = "that file isn't a picture Windows can read"; return null; }
        }

        // n, or the nearest length beside it that is even (or odd) as asked.
        static int Matched(int n, bool even)
        {
            if ((n % 2 == 0) == even) return n;
            return n > 1 ? n - 1 : n + 1;
        }
    }

    // Where the crosshair goes and whether it shows. No window is touched
    // here, so every answer can be checked without one.
    static class CrossPlace
    {
        // The top-left corner for a crosshair picture whose shape has its
        // middle at middle2 (half pixels from its corner, CrossArt.Middle2),
        // so that middle lands on the true middle of the game's picture,
        // moved by the nudge in whole pixels.
        //
        // Counted in half pixels the game's middle is 2 * left + width: on
        // a line between pixels when that is even, in a pixel when odd.
        // When the shape's middle and the game's are the same kind it is
        // exact. When they cannot be (an odd-thick cross on a 1920-wide
        // game) it is half a pixel left or up - always that way, wherever
        // the window is: rounded down, never toward zero, so a monitor at
        // negative coordinates rounds as one at positive ones does.
        public static Point TopLeft(Rectangle gameOnScreen, Point middle2, int nudgeX, int nudgeY)
        {
            int x2 = 2 * gameOnScreen.Left + gameOnScreen.Width - middle2.X;
            int y2 = 2 * gameOnScreen.Top + gameOnScreen.Height - middle2.Y;
            return new Point(Half(x2) + nudgeX, Half(y2) + nudgeY);
        }

        static int Half(int v) { return v >> 1; }      // rounds down, negatives too

        // How close to the game's middle the mouse must be for the game to be
        // holding it there. Roblox puts it back every frame while it has it.
        public const int AIM_SLACK = 3;

        // For a crosshair that shows only while aiming: whether it shows
        // now. The game is holding the mouse (first person, shift-lock) when
        // the mouse is within a few pixels of its picture's middle on both
        // axes; anywhere else it is a menu or a free mouse, and it hides.
        public static bool AimShows(bool aimOnly, Point mouse, Rectangle gameOnScreen)
        {
            if (!aimOnly) return true;
            // Both in half pixels: the middle of the mouse's pixel, the game's middle.
            int dx = 2 * mouse.X + 1 - (2 * gameOnScreen.Left + gameOnScreen.Width);
            int dy = 2 * mouse.Y + 1 - (2 * gameOnScreen.Top + gameOnScreen.Height);
            return Math.Abs(dx) <= 2 * AIM_SLACK + 1 && Math.Abs(dy) <= 2 * AIM_SLACK + 1;
        }

        // Only over a Roblox window that is the one in front, is not
        // minimised, and is big enough to be a game rather than a login box.
        public static bool Shows(bool switchedOn, bool isRoblox, bool inFront, bool minimised, bool chosen, Size game)
        {
            if (!switchedOn || !isRoblox || !inFront || minimised || !chosen) return false;
            return game.Width >= 200 && game.Height >= 200;
        }

        // "Everyone" when nothing is named; otherwise only the clients whose
        // account or label is in the list.
        public static bool Chosen(IList<string> only, string account, string label)
        {
            if (only == null || only.Count == 0) return true;
            foreach (string name in only)
            {
                if (!string.IsNullOrEmpty(account) && string.Equals(name, account, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(label) && string.Equals(name, label, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
