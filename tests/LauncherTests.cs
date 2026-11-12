using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The launcher's pages: reading what Roblox answers, laying cards out,
    // remembering what was played, and the shell that holds the pages. The
    // answers read here are real ones (RobloxAnswers); nothing is asked of
    // Roblox by a test.
    static class LauncherTests
    {
        // ---------- reading Roblox's answers ----------

        public static void TestJsonIsReadIntoPlainThings()
        {
            object o = Json.Parse("{\"a\":1,\"b\":\"two\",\"c\":[true,false,null],\"d\":{\"e\":-2.5e1}}");
            Assert.Equal(1L, Json.Num(o, "a"), "a number");
            Assert.Equal("two", Json.Str(o, "b"), "text");
            Assert.Equal(3, Json.List(o, "c").Count, "a list");
            Assert.Equal(true, Json.List(o, "c")[0], "true");
            Assert.Equal(null, Json.List(o, "c")[2], "null");
            Assert.Equal("-25", Json.Str(Json.Get(o, "d"), "e"), "a number inside something, as text");
        }

        // A game's name can hold anything: quotes, braces, another game's
        // field names. That is why these lists are read properly.
        public static void TestANameWithQuotesAndBracesIsOneName()
        {
            object o = Json.Parse("{\"name\":\"He said \\\"hi\\\" {\\\"universeId\\\":1}\\n\\u00e9\",\"universeId\":7}");
            Assert.Equal("He said \"hi\" {\"universeId\":1}\né", Json.Str(o, "name"), "the whole name");
            Assert.Equal("7", Json.Str(o, "universeId"), "and the real id, not the one in the name");
        }

        // An id is far bigger than fits in 32 bits, and must not come back
        // rounded or in scientific notation.
        public static void TestAnIdIsReadExactly()
        {
            Assert.Equal("120219252362013", Json.Str(Json.Parse("{\"id\":120219252362013}"), "id"), "every digit");
        }

        public static void TestAnswersThatAreNotJsonAreNothingNotAnError()
        {
            Assert.Equal(null, Json.Parse("<html>Bad gateway</html>"), "a web page");
            Assert.Equal(null, Json.Parse("{\"a\":"), "cut off");
            Assert.Equal(null, Json.Parse(""), "empty");
            Assert.Equal(null, Json.Parse("{} trailing"), "something after the end");
            Assert.Equal(0, Json.List(null, "x").Count, "and a list from nothing is empty");
            string deep = new string('[', 500) + new string(']', 500);
            Assert.Equal(null, Json.Parse(deep), "made to be absurdly deep");
        }

        public static void TestRobloxsListsOfGamesAreRead()
        {
            List<GameSort> sorts = RobloxWeb.ParseSorts(RobloxAnswers.Sorts);
            Assert.Equal(2, sorts.Count, "the two lists of games; the row of filters is not one");
            Assert.Equal("Top Trending", sorts[0].Name, "by Roblox's name for it");
            GameCard g = sorts[0].Games[0];
            Assert.Equal("Throw Food For Animals", g.Name, "a game's name");
            Assert.Equal("10766972131", g.UniverseId, "what Roblox's lists go by");
            Assert.Equal("120219252362013", g.PlaceId, "what starts it");
            Assert.Equal(211623L, g.Playing, "how many are playing");
            Assert.Equal(94, g.LikePercent, "and how many liked it");
            Assert.Equal("211.6K playing · 94%", g.Line(), "as a card says it");
        }

        // No adverts: a game that paid to be in a list is not shown in it.
        public static void TestGamesPaidToBeInAListAreLeftOut()
        {
            List<GameSort> sorts = RobloxWeb.ParseSorts(RobloxAnswers.Sorts);
            Assert.Equal(2, sorts[0].Games.Count, "three were sent, one of them sponsored");
            foreach (GameCard g in sorts[0].Games) Assert.False(g.Name.Contains("Forsaken"), "the sponsored one is gone");
        }

        public static void TestSearchResultsAreRead()
        {
            List<GameCard> games = RobloxWeb.ParseSearch(RobloxAnswers.Search);
            Assert.Equal(3, games.Count, "one from each group");
            Assert.Equal("Mega Obby Fun", games[0].Name, "in Roblox's order");
            Assert.Equal("16518256559", games[0].PlaceId, "with the place that starts it");
        }

        public static void TestGameDetailsAndFavouritesAreRead()
        {
            List<GameCard> games = RobloxWeb.ParseGames(RobloxAnswers.Games);
            Assert.Equal(2, games.Count, "both");
            Assert.Equal("4924922222", games[0].PlaceId, "details name the place one way");
            Assert.Equal(185842L, games[0].Playing, "and say how many are playing");

            List<GameCard> favs = RobloxWeb.ParseGames(RobloxAnswers.Favourites);
            Assert.Equal("The Hunt: Roblox 20", favs[0].Name, "a favourite");
            Assert.Equal("74205509034203", favs[0].PlaceId, "favourites name the place another way");
        }

        public static void TestLikesAreAddedToGamesThatCameWithout()
        {
            List<GameCard> games = RobloxWeb.ParseGames(RobloxAnswers.Games);
            Assert.Equal(-1, games[0].LikePercent, "details come with no votes");
            RobloxWeb.AddVotes(games, RobloxAnswers.Votes);
            Assert.Equal(86, games[0].LikePercent, "8.7 million up, 1.4 million down");
        }

        // A picture not made yet has no address, and only Roblox's own
        // picture servers are ever fetched from.
        public static void TestOnlyFinishedPicturesFromRobloxAreUsed()
        {
            Dictionary<string, string> icons = RobloxWeb.ParseImages(RobloxAnswers.Icons);
            Assert.Equal(2, icons.Count, "two finished, one still being made");
            Assert.True(icons["1686885941"].StartsWith("https://tr.rbxcdn.com/"), "from Roblox's picture server");

            Assert.True(Thumbs.IsRobloxPicture("https://tr.rbxcdn.com/abc/150/150/Image/Png/noFilter"), "Roblox's");
            Assert.False(Thumbs.IsRobloxPicture("https://evil.example/tr.rbxcdn.com/x.png"), "somewhere else with Roblox in the path");
            Assert.False(Thumbs.IsRobloxPicture("https://rbxcdn.com.evil.example/x.png"), "somewhere else with Roblox in the name");
            Assert.False(Thumbs.IsRobloxPicture("http://tr.rbxcdn.com/x.png"), "not over a secure connection");
            Assert.False(Thumbs.IsRobloxPicture(null), "nothing");
        }

        public static void TestAPicturesFileNameSaysNothingAboutItsAddress()
        {
            string name = Thumbs.FileFor("https://tr.rbxcdn.com/../../evil");
            Assert.Equal(44, name.Length, "a hash and .png");
            Assert.False(name.Contains("/") || name.Contains("\\") || name.Contains(".."), "nothing that could name a folder");
            Assert.Equal(name, Thumbs.FileFor("https://tr.rbxcdn.com/../../evil"), "the same every time");
        }

        public static void TestAPlaceLeadsToItsGame()
        {
            Assert.Equal("383310974", RobloxWeb.ParseUniverse(RobloxAnswers.Universe), "the game a place belongs to");
        }

        public static void TestWhereFriendsAreIsAddedToThem()
        {
            List<FriendCard> friends = new List<FriendCard>();
            FriendCard a = new FriendCard(); a.UserId = "1"; a.Name = "Roblox";
            FriendCard b = new FriendCard(); b.UserId = "156"; b.Name = "builderman";
            friends.Add(a); friends.Add(b);
            Assert.Equal("{\"userIds\":[1,156]}", RobloxWeb.PresenceBody(friends), "what is asked");

            RobloxWeb.AddPresence(friends, RobloxAnswers.Presence);
            Assert.False(a.Online, "offline, as Roblox said");
            Assert.Equal("Offline", a.Line(), "in words");
            Assert.False(a.Joinable, "and nowhere to join");

            RobloxWeb.AddPresence(friends, "{\"userPresences\":[{\"userPresenceType\":2,\"lastLocation\":\"Brookhaven\",\"placeId\":4924922222,"
                + "\"rootPlaceId\":4924922222,\"gameId\":\"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0\",\"universeId\":1686885941,\"userId\":156}]}");
            Assert.True(b.Joinable, "in a game, and Roblox said which server");
            Assert.Equal("Brookhaven", b.Line(), "where");
        }

        // In a game, but their privacy settings keep the server to themselves.
        public static void TestAFriendWhoseServerIsHiddenCantBeJoined()
        {
            List<FriendCard> friends = new List<FriendCard>();
            FriendCard f = new FriendCard(); f.UserId = "5";
            friends.Add(f);
            RobloxWeb.AddPresence(friends, "{\"userPresences\":[{\"userPresenceType\":2,\"lastLocation\":\"\",\"placeId\":null,\"rootPlaceId\":null,\"gameId\":null,\"universeId\":null,\"userId\":5}]}");
            Assert.True(f.Online, "online");
            Assert.False(f.Joinable, "but not joinable");
            Assert.Equal("In a game", f.Line(), "and it says only that");
        }

        public static void TestFriendsInAGameComeFirst()
        {
            List<FriendCard> friends = new List<FriendCard>();
            string[] names = { "zed", "amy", "bob", "cat" };
            int[] presence = { 0, 1, 2, 0 };
            for (int i = 0; i < names.Length; i++)
            {
                FriendCard f = new FriendCard(); f.Name = names[i]; f.Presence = presence[i];
                friends.Add(f);
            }
            RobloxWeb.SortFriends(friends);
            Assert.Equal("bob", friends[0].Name, "in a game");
            Assert.Equal("amy", friends[1].Name, "then on the website");
            Assert.Equal("cat", friends[2].Name, "then offline, by name");
        }

        // A sign-in is only ever sent to roblox.com.
        public static void TestOnlyRobloxIsEverAsked()
        {
            Assert.True(RobloxWeb.IsRoblox("https://games.roblox.com/v1/games"), "Roblox");
            Assert.False(RobloxWeb.IsRoblox("https://roblox.com.evil.example/v1"), "a lookalike");
            Assert.False(RobloxWeb.IsRoblox("https://evilroblox.com/"), "another lookalike");
            Assert.False(RobloxWeb.IsRoblox("http://games.roblox.com/v1/games"), "not secure");
            string error;
            Assert.Equal(null, RobloxWeb.Get("https://example.com/", "a-cookie", out error), "refused before anything is sent");
            Assert.Contains("Roblox", error, "and it says why");
        }

        public static void TestBigNumbersAreShortened()
        {
            Assert.Equal("897", GameCard.Short(897), "small");
            Assert.Equal("12.4K", GameCard.Short(12400), "thousands");
            Assert.Equal("1.5M", GameCard.Short(1500000), "millions");
        }

        // ---------- laying cards out ----------

        static CardSection Section(string title, int items)
        {
            CardSection s = new CardSection();
            s.Title = title;
            for (int i = 0; i < items; i++)
            {
                CardItem c = new CardItem();
                c.Title = title + " " + i;
                s.Items.Add(c);
            }
            return s;
        }

        public static void TestCardsWrapToAsManyAsFitAcross()
        {
            Assert.Equal(5, CardLayout.Across(902), "five across the page as it is");
            int height;
            List<CardLayout.Place> places = CardLayout.Lay(new CardSection[] { Section("Games", 7) }, 902, out height);
            List<CardLayout.Place> cards = places.FindAll(delegate(CardLayout.Place p) { return p.Item != null; });
            Assert.Equal(7, cards.Count, "every card");
            Assert.Equal(cards[0].Bounds.Top, cards[4].Bounds.Top, "five on the first row");
            Assert.True(cards[5].Bounds.Top > cards[0].Bounds.Bottom, "the sixth on the next");
            Assert.Equal(cards[0].Bounds.Left, cards[5].Bounds.Left, "back at the left");
            Assert.True(cards[4].Bounds.Right <= 902 - CardLayout.PAD, "and nothing past the right edge");
        }

        public static void TestASectionCanBeHeldToOneRow()
        {
            CardSection s = Section("Popular", 40);
            s.MaxRows = 1;
            int height;
            List<CardLayout.Place> places = CardLayout.Lay(new CardSection[] { s }, 902, out height);
            Assert.Equal(5, places.FindAll(delegate(CardLayout.Place p) { return p.Item != null; }).Count, "one row of five");
        }

        public static void TestAnEmptySectionSaysSoAndSectionsStack()
        {
            CardSection none = Section("Continue", 0);
            none.Link = "See all";
            int height;
            List<CardLayout.Place> places = CardLayout.Lay(new CardSection[] { none, Section("Popular", 2) }, 902, out height);
            Assert.True(places.Exists(delegate(CardLayout.Place p) { return p.IsEmpty; }), "a line where the cards would be");
            Assert.True(places.Exists(delegate(CardLayout.Place p) { return p.IsLink; }), "and its link");
            CardLayout.Place second = places.Find(delegate(CardLayout.Place p) { return p.Section.Title == "Popular" && p.Item == null; });
            CardLayout.Place firstEmpty = places.Find(delegate(CardLayout.Place p) { return p.IsEmpty; });
            Assert.True(second.Bounds.Top > firstEmpty.Bounds.Bottom, "the next section below it");
            Assert.True(height > second.Bounds.Bottom, "and the page as tall as all of it");
        }

        public static void TestFriendsAreRowsNotCards()
        {
            CardSection s = Section("Online", 3);
            s.Rows = true;
            int height;
            List<CardLayout.Place> rows = CardLayout.Lay(new CardSection[] { s }, 902, out height)
                .FindAll(delegate(CardLayout.Place p) { return p.Item != null; });
            Assert.Equal(902 - CardLayout.PAD * 2, rows[0].Bounds.Width, "the page's width");
            Assert.True(rows[1].Bounds.Top >= rows[0].Bounds.Bottom, "one under another");
        }

        public static void TestAPageScrollsNoFurtherThanItsEnd()
        {
            Assert.Equal(0, CardLayout.ClampScroll(-50, 2000, 600), "not above the top");
            Assert.Equal(1400, CardLayout.ClampScroll(5000, 2000, 600), "not below the bottom");
            Assert.Equal(0, CardLayout.ClampScroll(300, 400, 600), "and not at all when it all fits");
        }

        public static void TestAPictureFillsItsBoxWithoutBeingSquashed()
        {
            Rectangle r = CardCanvas.Cover(new Size(1920, 1080), new Rectangle(0, 0, 158, 158));
            Assert.Equal(158, r.Height, "as tall as the box");
            Assert.True(r.Width > 158 && r.X < 0, "wider, with the sides cropped evenly");
        }

        public static void TestACardCanvasShowsWhatItIsGivenAndReportsClicks()
        {
            using (CardCanvas c = new CardCanvas())
            {
                c.Size = new Size(902, 600);
                CardSection s = Section("Games", 30);
                c.Show(new CardSection[] { s });
                Assert.True(c.ContentHeight > 600, "more than a screen of it");
                c.ScrollTo(100000, false);
                Assert.Equal(c.ContentHeight - 600, c.ScrollTop, "scrolled to the end and no further");

                CardItem got = null;
                c.ItemClicked += delegate(CardItem item, Point at) { got = item; };
                c.ClickItem(s.Items[3]);
                Assert.Equal("Games 3", got.Title, "the one that was clicked");
            }
        }

        // ---------- played lately ----------

        public static void TestTheGameJustPlayedGoesFirst()
        {
            Recents r = new Recents(Path.Combine(Path.GetTempPath(), "rk-recent-" + Guid.NewGuid().ToString("N") + ".txt"));
            Assert.True(r.Add("111"), "the first");
            Assert.True(r.Add("222"), "another");
            Assert.False(r.Add("222"), "the same again changes nothing");
            Assert.True(r.Add("111"), "back to the first");
            Assert.Equal("111,222", string.Join(",", new List<string>(r.Places).ToArray()), "newest first, each once");
            Assert.False(r.Add("not a place"), "nonsense is not kept");
            Assert.False(r.Add(null), "nor nothing");
        }

        public static void TestPlayedLatelyIsKeptAndComesBackInOrder()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-recent-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                Recents r = new Recents(path);
                for (int i = 1; i <= Recents.KEEP + 5; i++) r.Add(i.ToString());
                Assert.Equal(Recents.KEEP, r.Places.Count, "only so many are kept");
                r.Save();

                Recents back = new Recents(path);
                back.Load();
                Assert.Equal((Recents.KEEP + 5).ToString(), back.Places[0], "the newest still first");
                Assert.Equal(Recents.KEEP, back.Places.Count, "and all of them");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestTheFirstListComesFromRobloxsOwnLogs()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-logs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "0.700.0_20261101T100000Z_Player_AAAAA_last.log"),
                    "2026-11-01T10:00:01.000Z,1.0,abc,6 [FLog::Output] ! Joining game '0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0' place 920587237 at 10.0.0.1\r\n");
                File.SetLastWriteTimeUtc(Path.Combine(dir, "0.700.0_20261101T100000Z_Player_AAAAA_last.log"), new DateTime(2026, 11, 1));
                File.WriteAllText(Path.Combine(dir, "0.700.0_20261102T100000Z_Player_BBBBB_last.log"),
                    "noise\r\n2026-11-02T10:00:01.000Z,1.0,abc,6 [FLog::Output] ! Joining game '1f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0' place 4924922222 at 10.0.0.2\r\n");
                File.SetLastWriteTimeUtc(Path.Combine(dir, "0.700.0_20261102T100000Z_Player_BBBBB_last.log"), new DateTime(2026, 11, 2));
                File.WriteAllText(Path.Combine(dir, "RobloxPlayerInstaller_123.log"), "! Joining game 'x' place 999 at\r\n");

                List<string> places = Recents.FromLogs(dir, 12);
                Assert.Equal("920587237,4924922222", string.Join(",", places.ToArray()), "what was joined, oldest first; the installer's log is not a client's");
                Assert.Equal(0, Recents.FromLogs(Path.Combine(dir, "missing"), 12).Count, "no logs folder is not an error");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- screenshots ----------

        public static void TestAScreenshotsNameSaysWhenAndWhose()
        {
            DateTime when = new DateTime(2026, 11, 11, 14, 3, 22);
            Assert.Equal("RobloxKeeper 2026-11-11 14.03.22 main_account.png", Shots.NameFor(when, "main_account"), "sorts by when, says whose");
            Assert.Equal("RobloxKeeper 2026-11-11 14.03.22 Client2.png", Shots.NameFor(when, "Client 2"), "a name made safe for a file");
            Assert.Equal("RobloxKeeper 2026-11-11 14.03.22.png", Shots.NameFor(when, "..\\..\\"), "nothing left of a name that was only a path");
        }

        public static void TestTheNewestScreenshotsComeFirst()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-shots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    string p = Path.Combine(dir, "shot" + i + ".png");
                    File.WriteAllText(p, "x");
                    File.SetLastWriteTimeUtc(p, new DateTime(2026, 11, 1 + i));
                }
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
                List<string> newest = Shots.Newest(dir, 3);
                Assert.Equal(3, newest.Count, "as many as were asked for");
                Assert.Equal("shot4.png", Path.GetFileName(newest[0]), "the newest first");
                Assert.Equal(0, Shots.Newest(Path.Combine(dir, "missing"), 3).Count, "no folder yet is not an error");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- the shell ----------

        public static void TestTheRailPutsPagesFromTheTopAndKeeperAtTheBottom()
        {
            using (Rail rail = new Rail())
            {
                rail.Size = new Size(Rail.WIDTH, 736);
                rail.Add(new RailItem("home", "Home", 'a', false));
                rail.Add(new RailItem("games", "Games", 'b', false));
                rail.Add(new RailItem("keeper", "Keeper", 'c', true));
                Assert.True(rail.BoundsOf(1).Top > rail.BoundsOf(0).Bottom, "one under another");
                Assert.True(rail.BoundsOf(2).Bottom > 680, "the last at the bottom");
                string chosen = null;
                rail.Chosen += delegate(string key) { chosen = key; };
                rail.Choose("games");
                Assert.Equal("games", chosen, "and it says which was chosen");
            }
        }

        // The window as it always was is one of the pages, moved whole.
        public static void TestTheOldWindowIsAPageOfTheNewOne()
        {
            // On a screen tall enough for all of it; a short one adds a scroll bar.
            MainForm.ScreenHeightForTests = 2000;
            try
            {
                using (MainForm f = new MainForm(false))
                {
                    Assert.Equal(MainForm.BASE_WIDTH + Rail.WIDTH, f.ClientSize.Width, "wider by the rail and nothing else");
                    Assert.Equal(MainForm.PAGE_KEEPER, f.CurrentPage, "a window only being drawn opens on the old page");
                    Assert.Equal(7, f.ShellRail.Items.Count, "seven pages");
                }
            }
            finally { MainForm.ScreenHeightForTests = 0; }
        }

        // A page costs nothing until someone opens it.
        public static void TestAPageIsMadeTheFirstTimeItIsOpened()
        {
            using (MainForm f = new MainForm(false))
            {
                Assert.Equal(null, f.PageNow, "nothing made to start with");
                f.ShowPage(MainForm.PAGE_AUTO);
                Assert.True(f.PageNow is AutomationPage, "made when asked for");
                Assert.False(f.PageNow.IsShowing, "and doing nothing while the window isn't on screen");
                ShellPage first = f.PageNow;
                f.ShowPage(MainForm.PAGE_KEEPER);
                f.ShowPage(MainForm.PAGE_AUTO);
                Assert.True(ReferenceEquals(first, f.PageNow), "the same one the second time");
            }
        }

        public static void TestEveryPageBuilds()
        {
            string[] keys = { MainForm.PAGE_HOME, MainForm.PAGE_GAMES, MainForm.PAGE_FRIENDS, MainForm.PAGE_AUTO,
                              MainForm.PAGE_LOOKS, MainForm.PAGE_SHOTS };
            using (MainForm f = new MainForm(false))
                foreach (string key in keys)
                {
                    f.ShowPage(key);
                    Assert.Equal(key, f.CurrentPage, key + " opens");
                    Assert.True(f.PageNow != null && f.PageNow.Width > 800, key + " is a page");
                }
        }

        // With no account saved, the friends page says what to do rather
        // than showing nothing.
        public static void TestFriendsWithNoAccountSaysWhatToDo()
        {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_FRIENDS);
                FriendsPage page = (FriendsPage)f.PageNow;
                page.Rebuild();
                Assert.Contains("Add a Roblox account", page.Canvas.Sections[0].Empty, "what to do");
                Assert.Equal("Add an account", page.Canvas.Sections[0].Link, "and a way to do it");
            }
        }

        public static void TestHomeWorksWithNothingOpenAndNoAccount()
        {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_HOME);
                HomePage home = (HomePage)f.PageNow;
                home.Rebuild();
                Assert.Equal("Start here", home.Canvas.Sections[0].Title, "someone new is told where to begin");
                Assert.True(home.Canvas.Sections[0].Items.Count >= 3, "with the jobs people come for, by name");
                Assert.Equal("Open now", home.Canvas.Sections[1].Title, "then your windows");
                Assert.Contains("No Roblox window is open", home.Canvas.Sections[1].Empty, "said plainly when there are none");
                Assert.Equal("Continue", home.Canvas.Sections[2].Title, "then what to go back to");

                f.TipsHidden = true;
                home.Rebuild();
                Assert.Equal("Open now", home.Canvas.Sections[0].Title, "and it can be put away");
            }
        }

        public static void TestThePageAndAccountAreRemembered()
        {
            AppSettings s = new AppSettings();
            Assert.Equal(MainForm.PAGE_HOME, s.Page, "Home the first time");
            s.Page = MainForm.PAGE_AUTO; s.PlayAs = "main"; s.ShotKey = 0x7B;
            AppSettings back = AppSettings.Parse(s.ToLines());
            Assert.Equal(MainForm.PAGE_AUTO, back.Page, "the page it was left on");
            Assert.Equal("main", back.PlayAs, "the account");
            Assert.Equal((byte)0x7B, back.ShotKey, "the screenshot key");
        }

        public static void TestAutomationSaysWhatEachToolIsDoing()
        {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_AUTO);
                AutomationPage page = (AutomationPage)f.PageNow;
                page.ShowStates();
                Assert.Equal("Not set up yet", page.StateText(0), "the autoclicker, before anyone has opened it");
                Assert.Contains("Nothing is playing", page.StateText(5), "and nothing to stop");
            }
        }

        // The activity list is on the Keeper page. What a page set going is
        // said on that page too.
        public static void TestWhatHappensIsSaidOnThePageInView()
        {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_AUTO);
                f.Say("Saved a picture to Pictures.");
                Assert.Contains("Saved a picture to Pictures.", f.PageNow.LastHeard, "at the foot of the page");
            }
        }

        public static void TestGreetingsFollowTheClock()
        {
            Assert.Equal("Good morning", Launcher.Greeting(9), "morning");
            Assert.Equal("Good afternoon", Launcher.Greeting(15), "afternoon");
            Assert.Equal("Good evening", Launcher.Greeting(21), "evening");
            Assert.Equal("1.5 GB", Launcher.Memory(1610612736L), "and memory in sizes people use");
            Assert.Equal("820 MB", Launcher.Memory(859832320L), "small ones in megabytes");
        }
    }
}
