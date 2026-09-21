using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RobloxKeeper
{
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
                using (Bitmap bmp = image.ToBitmap())
                    return Read(bmp, eng);
            }
            catch { return ""; }
        }

        static string Read(Bitmap bmp, OcrEngine eng)
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
            // spaces, and chat is only readable as separate lines.
            StringBuilder sb = new StringBuilder();
            foreach (OcrLine line in result.Lines)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line.Text);
            }
            return sb.ToString();
        }

        // Waiting on WinRT without the Task bridge.
        //
        // Blocking is right here: this is called from the watch thread, which
        // exists to do exactly this and nothing else, and a read takes about
        // ten milliseconds on a region.
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
