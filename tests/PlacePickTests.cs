using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Choosing a place by clicking it on a picture of the game, and the
    // autoclicker's window showing only the rows its answers call for - so
    // nobody has to know how many percent across a button is.
    static class PlacePickTests
    {
        // A maximized client as Windows reports one: the frame hangs nine
        // pixels off every edge of the screen, and the title bar is on top.
        static readonly Rectangle Window = new Rectangle(-9, -9, 1938, 1158);
        static readonly Rectangle Client = new Rectangle(0, 29, 1920, 1111);
        static readonly Size Picture = new Size(1938, 1158);

        public static void TestAClickOnThePictureIsAPlaceInTheGame()
        {
            double fx, fy;
            // The very middle of the game: 960 across the screen, 29 + 555 down.
            Assert.True(PlacePick.FromPicture(Picture, Window, Client, new Point(969, 593), out fx, out fy), "inside the game");
            Assert.True(Math.Abs(fx - 0.5) < 0.002, "half way across: " + fx);
            Assert.True(Math.Abs(fy - 0.5) < 0.002, "half way down: " + fy);
        }

        // The picture has the title bar in it. A click step doesn't count it.
        public static void TestTheTitleBarIsNotPartOfTheGame()
        {
            double fx, fy;
            Assert.False(PlacePick.FromPicture(Picture, Window, Client, new Point(969, 20), out fx, out fy), "on the title bar");
            Assert.Equal(0.0, fy, "held to the game's top edge all the same");
            Assert.True(PlacePick.FromPicture(Picture, Window, Client, new Point(969, 40), out fx, out fy), "just under it is the game");
        }

        // On a scaled display the picture is not the size the window is on
        // the screen. The same spot on it is still the same place.
        public static void TestAPictureOfAnotherSizeGivesTheSamePlace()
        {
            double fx, fy, hx, hy;
            PlacePick.FromPicture(Picture, Window, Client, new Point(1300, 540), out fx, out fy);
            PlacePick.FromPicture(new Size(969, 579), Window, Client, new Point(650, 270), out hx, out hy);
            Assert.True(Math.Abs(fx - hx) < 0.002, "across: " + fx + " and " + hx);
            Assert.True(Math.Abs(fy - hy) < 0.002, "down: " + fy + " and " + hy);
        }

        public static void TestAPlaceCanBeMarkedBackOnThePicture()
        {
            double fx, fy;
            PlacePick.FromPicture(Picture, Window, Client, new Point(1304, 540), out fx, out fy);
            Point back = PlacePick.ToPicture(Picture, Window, Client, fx, fy);
            Assert.True(Math.Abs(back.X - 1304) <= 1 && Math.Abs(back.Y - 540) <= 1, "where it was clicked: " + back);
        }

        // A click made from a picked place lands where it was picked: the
        // picture and the player agree about the window.
        public static void TestAPickedPlaceIsClickedWhereItWasPicked()
        {
            double fx, fy;
            PlacePick.FromPicture(Picture, Window, Client, new Point(1304, 540), out fx, out fy);
            Point screen = MacroPlan.PointIn(Client, fx, fy);
            // Picture pixel 1304,540 is screen pixel 1295,531: the window starts at -9,-9.
            Assert.True(Math.Abs(screen.X - 1295) <= 1 && Math.Abs(screen.Y - 531) <= 1, "on the screen: " + screen);
        }

        static Pixels Still()
        {
            Pixels p = new Pixels(Picture.Width, Picture.Height);
            p.Fill(unchecked((int)0xFF204060));
            return p;
        }

        public static void TestOneClickChoosesAPlace()
        {
            using (PlacePickerForm f = new PlacePickerForm(Still(), Window, Client, "Client 1", false, null))
            {
                Assert.False(f.Accept(), "nothing chosen yet");
                f.ClickAt(new Point(f.ViewSize.Width / 2, f.ViewSize.Height / 2));
                Assert.True(f.Accept(), "chosen");
                Assert.Equal(2, f.Picked.Length, "across and down");
                Assert.True(Math.Abs(f.Picked[0] - 0.5) < 0.01, "about the middle: " + f.Picked[0]);
                Assert.Contains("% across", f.Says, "and it says where");
            }
        }

        // The place a step already has is marked, but opening the window was
        // to choose again - it is not handed back as if it had been clicked.
        public static void TestThePlaceItAlreadyHadIsShownNotChosen()
        {
            using (PlacePickerForm f = new PlacePickerForm(Still(), Window, Client, "Client 1", false, new double[] { 0.2, 0.3 }))
            {
                Assert.Equal(1, f.PlacesChosen, "marked");
                Assert.False(f.Accept(), "but not chosen until something is clicked");
            }
        }

        public static void TestADragIsTwoClicks()
        {
            using (PlacePickerForm f = new PlacePickerForm(Still(), Window, Client, "Client 1", true, null))
            {
                int w = f.ViewSize.Width, h = f.ViewSize.Height;
                f.ClickAt(new Point(w / 4, h / 2));
                Assert.False(f.Accept(), "one end is not a drag");
                Assert.Contains("ends", f.Says, "it asks for the other");
                f.ClickAt(new Point(3 * w / 4, h / 2));
                Assert.True(f.Accept(), "both ends");
                Assert.Equal(4, f.Picked.Length, "two places");
                Assert.True(f.Picked[2] > f.Picked[0], "from left to right");

                f.ClickAt(new Point(w / 2, h / 2));
                Assert.Equal(1, f.PlacesChosen, "a third click starts over");
            }
        }

        public static void TestAClickOnTheFrameIsRefusedWithAReason()
        {
            using (PlacePickerForm f = new PlacePickerForm(Still(), Window, Client, "Client 1", false, null))
            {
                f.ClickAt(new Point(f.ViewSize.Width / 2, 1));
                Assert.False(f.Accept(), "not a place");
                Assert.Contains("frame", f.Says, "and it says why");
            }
        }

        // ---------- the autoclicker's window ----------

        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        class Host : IMacroHost
        {
            readonly AutoClick clicker = new AutoClick();
            byte stopKey = 0x79;
            public AutoClick Clicker { get { return clicker; } }
            public void SaveClicker() { }
            public bool ClickerOn { get { return false; } }
            public string ToggleClicker() { return null; }
            public byte StopKey { get { return stopKey; } set { stopKey = value; } }
            public string StopKeyProblem(byte vk) { return null; }
            public void StopEverything() { }
            public int SessionsRunning { get { return 0; } }
            public string KeyUseProblem(byte vk, string usedFor, string editing) { return null; }
            public void MacrosSaved() { }
            public string PlayAnywhere(Macro m) { return null; }
            public bool IsPlaying(string tag) { return false; }
            public bool StopTag(string tag) { return false; }
            public Macro QuickRecording { get; set; }
        }

        static WatchKit Kit(Host host)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-pick-" + Guid.NewGuid().ToString("N") + ".dat"));
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(new string[] { "main", "alt1" }); };
            k.Log = delegate(string s) { };
            k.Macros = host;
            return k;
        }

        // Someone who only wants "click where my mouse is" sees four rows,
        // not twelve.
        public static void TestTheAutoclickerOnlyShowsWhatTheAnswersNeed()
        {
            using (AutoClickDialog d = new AutoClickDialog(Kit(new Host())))
            {
                Assert.False(d.ShowsPlace || d.ShowsPicture || d.ShowsTurns, "at the mouse, in front: nothing extra");
                int small = d.ClientSize.Height;

                d.ChooseWhere(ClickWhere.Place);
                Assert.True(d.ShowsPlace, "one spot: the spot");
                Assert.False(d.ShowsPicture, "and no picture");
                d.ChooseWhere(ClickWhere.Picture);
                Assert.True(d.ShowsPicture && !d.ShowsPlace, "a picture: the picture, and no spot");
                d.ChooseOn(ClickOn.TakeTurns);
                Assert.True(d.ShowsTurns, "several windows: which, and for how long");
                Assert.True(d.ClientSize.Height > small, "and the window grows to fit");

                d.ChooseWhere(ClickWhere.AtPointer);
                d.ChooseOn(ClickOn.InFront);
                Assert.Equal(small, d.ClientSize.Height, "and shrinks back");
            }
        }

        public static void TestSpeedIsChosenByNameNotBySeconds()
        {
            Host host = new Host();
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
            {
                Assert.False(d.ShowsOwnSpeed, "the seconds box is out of the way: 0.1s is \"Fast\"");
                d.ChooseSpeed(0);
                Assert.Equal(1000, d.Collect().EveryMs, "slow is one a second");
                d.ChooseSpeed(3);
                Assert.Equal(50, d.Collect().EveryMs, "very fast is twenty a second");
                d.ChooseSpeed(d.SpeedChoices - 1);
                Assert.True(d.ShowsOwnSpeed, "my own speed brings the seconds box back");
            }
        }

        public static void TestASpeedThatIsNotOnTheListIsKeptAsMyOwn()
        {
            Host host = new Host();
            host.Clicker.EveryMs = 250;
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
            {
                Assert.True(d.ShowsOwnSpeed, "shown as typed");
                Assert.Equal(250, d.Collect().EveryMs, "and kept");
            }
        }

        // The boxes show whole percents; a picked spot is kept to the pixel.
        public static void TestAPickedSpotIsKeptExactly()
        {
            Host host = new Host();
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
            {
                d.ChooseWhere(ClickWhere.Place);
                d.ChoosePlace(0.6748, 0.4523);
                AutoClick c = d.Collect();
                Assert.Equal(0.6748, c.X, "across, as picked");
                Assert.Equal(0.4523, c.Y, "down, as picked");
            }
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Click), Kit(host)))
            {
                d.PickPlace(0.6748, 0.4523);
                Assert.True(d.TryAccept(), "saves");
                Assert.Equal(0.6748, d.Result.X, "a click step keeps it too");
            }
        }
    }
}
