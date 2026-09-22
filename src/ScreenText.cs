using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RobloxKeeper
{
    // One line of text, and where on the picture it was read.
    struct TextLine
    {
        public Rectangle Box;
        public string Text;

        public TextLine(Rectangle box, string text)
        {
            Box = box;
            Text = text;
        }
    }

    // Reading the writing in a picture, using the recogniser built into
    // Windows.
    //
    // No library, no download, no file in lib\. It compiles against the
    // .winmd files in C:\Windows\System32\WinMetadata, which ship with every
    // Windows 10 and 11, and it runs entirely offline.
    //
    // The one trap is awaiting. The usual WinRT-to-Task bridge lives in
    // System.Runtime.WindowsRuntime.dll, which demands the unified
    // Windows.winmd that only comes with the Windows SDK - referencing it
    // would mean this project no longer builds on a machine that has nothing
    // but Windows itself. So the waiting is done by hand, which needs nothing
    // the operating system does not already have.
    static class ScreenText
    {
        static OcrEngine engine;
        static bool looked;
        static readonly object gate = new object();

        // The command that installs the recogniser on a machine without it.
        // Needs administrator rights, so the app asks rather than assuming.
        public const string InstallCommand =
            "DISM /Online /Add-Capability /CapabilityName:Language.OCR~~~en-US~0.0.1.0";

        // Whether this machine can read text at all.
        //
        // Asked once at startup and remembered, so the answer cannot change
        // between one look at the watchers list and the next.
        public static bool Available
        {
            get { return Engine != null; }
        }

        static OcrEngine Engine
        {
            get
            {
                lock (gate)
                {
                    if (looked) return engine;
                    looked = true;
                    try
                    {
                        // English first because that is what Roblox's own
                        // interface uses whatever the user's Windows is set
                        // to; their own languages as a fallback.
                        engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
                        if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    }
                    catch
                    {
                        // A Windows edition without the media stack at all.
                        // Not an error worth showing - the watchers list says
                        // text reading is unavailable and offers to install it.
                        engine = null;
                    }
                    return engine;
                }
            }
        }

        // What Windows read, one line per line of text. Empty when there is
        // nothing to read, nothing to read it with, or nothing was found -
        // those are all the same thing to a caller and none of them is a
        // fault.
        public static string Read(Pixels image)
        {
            if (image == null || image.IsEmpty) return "";
            OcrEngine eng = Engine;
            if (eng == null) return "";

            try
            {
                // Read twice - as it is, and with colour turned into
                // brightness - and keep the better reading of each line. The
                // recogniser sees brightness, not colour: a dark red egg name
                // on Roblox's chat panel measured 54 against the panel's 40 and
                // was not read at all. Turned into brightness it is. But the
                // same turn makes red or blue text on white vanish, and a whole
                // window has both, so the choice is made line by line rather
                // than for the picture as a whole.
                List<TextLine> asItIs = ReadLines(image, eng);
                List<TextLine> inColour = ReadLines(ColourAsBrightness(image), eng);

                // Without positions the two readings cannot be matched line by
                // line, and merging blind would list every line twice. Keep
                // whichever reading found more instead.
                if (!Placed(asItIs) || !Placed(inColour))
                    return Join(Letters(Join(inColour)) > Letters(Join(asItIs)) ? inColour : asItIs);

                // A third reading for dark letters inside a black outline,
                // which neither of the others can see at all - see DarkFill.
                List<TextLine> darkFill = ReadLines(DarkFill(image), eng);
                List<TextLine> both = Merge(asItIs, inColour);
                return Join(Placed(darkFill) ? MergeExtra(both, darkFill) : both);
            }
            catch { return ""; }
        }

        static List<TextLine> ReadLines(Pixels image, OcrEngine eng)
        {
            using (Bitmap bmp = image.ToBitmap())
                return ReadLines(bmp, eng);
        }

        static List<TextLine> ReadLines(Bitmap bmp, OcrEngine eng)
        {
            byte[] bytes;
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Bmp);
                bytes = ms.ToArray();
            }

            InMemoryRandomAccessStream ras = new InMemoryRandomAccessStream();
            DataWriter writer = new DataWriter(ras.GetOutputStreamAt(0));
            writer.WriteBytes(bytes);
            Wait(writer.StoreAsync());
            ras.Seek(0);

            BitmapDecoder decoder = Wait(BitmapDecoder.CreateAsync(ras));
            SoftwareBitmap software = Wait(decoder.GetSoftwareBitmapAsync());
            OcrResult result = Wait(eng.RecognizeAsync(software));

            // Line by line, not result.Text: that runs every line together with
            // spaces, and chat is only readable as separate lines. Each keeps
            // where it was, so two readings of the picture can be matched up.
            List<TextLine> lines = new List<TextLine>();
            foreach (OcrLine line in result.Lines)
            {
                Rectangle box = Rectangle.Empty;
                foreach (OcrWord word in line.Words)
                {
                    Rectangle w = BoxOf(word);
                    box = box.IsEmpty ? w : Rectangle.Union(box, w);
                }
                lines.Add(new TextLine(box, line.Text));
            }
            return lines;
        }

        // Where a word is, read by reflection on purpose. The type it comes
        // back as, Windows.Foundation.Rect, is one .NET swaps for its own copy
        // in System.Runtime.WindowsRuntime.dll, and naming it in code means
        // referencing that assembly - which needs the Windows SDK to build.
        // At run time that copy ships with .NET Framework itself.
        static readonly PropertyInfo WordBox = typeof(OcrWord).GetProperty("BoundingRect");

        static Rectangle BoxOf(OcrWord word)
        {
            try
            {
                object r = WordBox.GetValue(word, null);
                Type t = r.GetType();
                double x = (double)t.GetProperty("X").GetValue(r, null);
                double y = (double)t.GetProperty("Y").GetValue(r, null);
                double w = (double)t.GetProperty("Width").GetValue(r, null);
                double h = (double)t.GetProperty("Height").GetValue(r, null);
                return new Rectangle((int)x, (int)y, (int)Math.Ceiling(w), (int)Math.Ceiling(h));
            }
            catch { return Rectangle.Empty; }
        }

        // Two readings of one picture put together, line by line; a line only
        // one of them found is kept as well. Out comes the screen's order: top
        // to bottom, and left to right along a row.
        //
        // Where both read a line, the colour reading is kept unless the other
        // found clearly more - a fifth more letters. Letters alone cannot tell
        // a garbled word from a right one: on a real spawn banner the reading
        // as it is turned red "Demons" into "venuons", one letter more than the
        // colour reading's correct line. Where the colour reading really does
        // lose a line - red or blue on white - it loses nearly all of it.
        public static List<TextLine> Merge(IList<TextLine> asItIs, IList<TextLine> inColour)
        {
            return Combine(asItIs, inColour, true);
        }

        // A reading that sees what the others cannot, and garbles what they
        // can. The dark-fill reading picks out outlined dark letters nothing
        // else reads, but it also reads white names off a dark panel, badly -
        // "VIBdDerKIng" for "Vlad DerKing", measured. So it adds lines nobody
        // else found, and replaces one only with strictly more letters.
        public static List<TextLine> MergeExtra(IList<TextLine> kept, IList<TextLine> extra)
        {
            return Combine(kept, extra, false);
        }

        static List<TextLine> Combine(IList<TextLine> first, IList<TextLine> other, bool trustOtherOnNearTie)
        {
            List<TextLine> kept = new List<TextLine>(first);
            foreach (TextLine line in other)
            {
                List<int> same = new List<int>();
                int theirs = 0;
                for (int i = 0; i < kept.Count; i++)
                    if (SameLine(kept[i].Box, line.Box)) { same.Add(i); theirs += Letters(kept[i].Text); }

                if (same.Count > 0)
                {
                    int mine = Letters(line.Text);
                    bool better = trustOtherOnNearTie ? mine * 5 >= theirs * 4 : mine > theirs;
                    if (!better) continue;
                }
                for (int i = same.Count - 1; i >= 0; i--) kept.RemoveAt(same[i]);
                kept.Add(line);
            }
            return InScreenOrder(kept);
        }

        // Two readings of one line overlap across, and down by at least half
        // the shorter one's height. One reading can split a line the other
        // reads whole, so a line can match several pieces.
        static bool SameLine(Rectangle a, Rectangle b)
        {
            Rectangle both = Rectangle.Intersect(a, b);
            if (both.Width <= 0 || both.Height <= 0) return false;
            return both.Height * 2 >= Math.Min(a.Height, b.Height);
        }

        static List<TextLine> InScreenOrder(List<TextLine> lines)
        {
            List<TextLine> down = new List<TextLine>(lines);
            down.Sort(delegate(TextLine x, TextLine y) { return Middle(x).CompareTo(Middle(y)); });

            List<TextLine> ordered = new List<TextLine>();
            int i = 0;
            while (i < down.Count)
            {
                // A row: every line whose middle is within half a line of the
                // first one's, then left to right along it.
                List<TextLine> row = new List<TextLine>();
                int top = Middle(down[i]);
                int half = Math.Max(1, down[i].Box.Height / 2);
                while (i < down.Count && Middle(down[i]) - top <= half) row.Add(down[i++]);
                row.Sort(delegate(TextLine x, TextLine y) { return x.Box.Left.CompareTo(y.Box.Left); });
                ordered.AddRange(row);
            }
            return ordered;
        }

        static int Middle(TextLine l) { return l.Box.Top + l.Box.Height / 2; }

        static bool Placed(List<TextLine> lines)
        {
            foreach (TextLine l in lines) if (l.Box.IsEmpty) return false;
            return true;
        }

        public static string Join(IList<TextLine> lines)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TextLine l in lines)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(l.Text);
            }
            return sb.ToString();
        }

        // Each pixel as grey at the brightness of its brightest part: dark red
        // counts as bright as its red, which is how it looks to a person.
        public static Pixels ColourAsBrightness(Pixels p)
        {
            Pixels o = new Pixels(p.Width, p.Height);
            for (int i = 0; i < p.Argb.Length; i++)
            {
                int c = p.Argb[i];
                int v = Math.Max((c >> 16) & 0xFF, Math.Max((c >> 8) & 0xFF, c & 0xFF));
                o.Argb[i] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
            }
            return o;
        }

        // Outlined dark lettering - a dark grey fill inside a black outline,
        // which is how this game draws a Secret egg's name - reads to the
        // recogniser as hollow letters: it takes the outline for the letter
        // and the fill for the gaps, and reads nothing (measured, every scale
        // and contrast tried). Picking out the fill alone, as solid black on
        // white, leaves ordinary letters, and those it reads. The fill measured
        // 46 with the outline at 0 and the scene behind well above 80, so the
        // band keeps the fill and nothing either side of it.
        const int FILL_FROM = 25, FILL_TO = 80;

        public static Pixels DarkFill(Pixels p)
        {
            Pixels o = new Pixels(p.Width, p.Height);
            int black = unchecked((int)0xFF000000), white = unchecked((int)0xFFFFFFFF);
            for (int i = 0; i < p.Argb.Length; i++)
            {
                int c = p.Argb[i];
                int v = Math.Max((c >> 16) & 0xFF, Math.Max((c >> 8) & 0xFF, c & 0xFF));
                o.Argb[i] = v >= FILL_FROM && v <= FILL_TO ? black : white;
            }
            return o;
        }

        static int Letters(string s)
        {
            int n = 0;
            if (s != null)
                foreach (char c in s)
                    if (char.IsLetterOrDigit(c)) n++;
            return n;
        }

        // Waiting on WinRT without the Task bridge.
        //
        // Blocking is right here: this is called from the watch thread, which
        // exists to do exactly this and nothing else, and reading a region
        // all three ways takes about ten to thirty milliseconds.
        static T Wait<T>(IAsyncOperation<T> op)
        {
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                op.Completed = delegate { done.Set(); };
                done.WaitOne();
                return op.GetResults();
            }
        }
    }
}
