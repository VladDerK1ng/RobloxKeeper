using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // A game's public servers, as Roblox lists them. The fixture is a real
    // answer from games.roblox.com for place 107778070777162, fetched on
    // 2026-09-22 and cut down to three servers.
    static class RobloxServersTests
    {
        const string RealPage =
            "{\"previousPageCursor\":null,\"nextPageCursor\":\"eyJzdGFydEluZGV4IjoxMCwiZGlzY3JpbWluYXRvciI6InBsYWNlSWQ6MTA3Nzc4MDcwNzc3MTYyc2VydmVyVHlwZTpQdWJsaWMiLCJjb3VudCI6MTB9CjIwZTQyODMzYzk4NWQ1NWU1MzcwZWNlZGEyOGQ2MWU4MzEyYTM3NTc0OGIxOTdlMDU5YzM3MWJlN2ZhYWMxM2M=\","
          + "\"data\":[{\"id\":\"a7c31f39-0d44-4d46-b900-7d94e7eb0aa5\",\"maxPlayers\":7,\"playing\":1,\"playerTokens\":[],\"players\":[],\"fps\":59.975727,\"ping\":65},"
          + "{\"id\":\"96447192-67fc-4bf9-99fa-8e144c3889e8\",\"maxPlayers\":7,\"playing\":6,\"playerTokens\":[],\"players\":[],\"fps\":59.9757,\"ping\":88},"
          + "{\"id\":\"c4fb8e4c-29ed-4e23-af35-507967d97eba\",\"maxPlayers\":7,\"playing\":7,\"playerTokens\":[],\"players\":[],\"fps\":59.966961,\"ping\":74}]}";

        public static void TestEveryServerOnAPageIsRead()
        {
            ServerPage p = RobloxServers.ParsePage(RealPage);
            Assert.Equal(3, p.Servers.Count, "three servers");
            Assert.Equal("a7c31f39-0d44-4d46-b900-7d94e7eb0aa5", p.Servers[0].Id, "its id");
            Assert.Equal(7, p.Servers[0].MaxPlayers, "room for seven");
            Assert.Equal(1, p.Servers[0].Playing, "one playing");
            Assert.Equal(65, p.Servers[0].Ping, "and its ping");
            Assert.True(p.Servers[1].HasRoom, "six of seven has room");
            Assert.False(p.Servers[2].HasRoom, "seven of seven is full");
        }

        public static void TestTheNextPageIsKnown()
        {
            Assert.True(RobloxServers.ParsePage(RealPage).NextCursor.StartsWith("eyJzdGFydEluZGV4"), "the cursor");
            Assert.Equal(null, RobloxServers.ParsePage("{\"previousPageCursor\":null,\"nextPageCursor\":null,\"data\":[]}").NextCursor,
                "the last page has none");
        }

        // Signed in, Roblox fills players with objects that have ids of their
        // own - numbers, not server ids, and they must not become servers.
        public static void TestPlayersInsideAServerAreNotServers()
        {
            string page = "{\"nextPageCursor\":null,\"data\":[{\"id\":\"a7c31f39-0d44-4d46-b900-7d94e7eb0aa5\",\"maxPlayers\":7,"
                        + "\"playing\":2,\"playerTokens\":[\"ABC\"],\"players\":[{\"playerToken\":\"ABC\",\"id\":1234,\"name\":\"x\",\"displayName\":\"x\"}],"
                        + "\"fps\":60,\"ping\":50}]}";
            ServerPage p = RobloxServers.ParsePage(page);
            Assert.Equal(1, p.Servers.Count, "one server");
            Assert.Equal(2, p.Servers[0].Playing, "its own count");
        }

        // Rate limited, or anything else that isn't a page: no servers, and
        // no exception.
        public static void TestAnAnswerThatIsNotAPageHasNoServers()
        {
            Assert.Equal(0, RobloxServers.ParsePage("{\"errors\":[{\"code\":0,\"message\":\"Too many requests\"}]}").Servers.Count, "rate limited");
            Assert.Equal(0, RobloxServers.ParsePage(null).Servers.Count, "nothing");
            Assert.Equal(0, RobloxServers.ParsePage("<html>").Servers.Count, "not json");
        }

        public static void TestThePageAddressAsksForServersWithRoom()
        {
            string first = RobloxServers.PageUrl("107778070777162", null);
            Assert.Contains("games.roblox.com/v1/games/107778070777162/servers/Public", first, "the game's public servers");
            Assert.Contains("excludeFullGames=true", first, "not full ones");
            Assert.Contains("limit=100", first, "a hundred at a time");
            Assert.False(first.Contains("cursor="), "the first page has no cursor");
            Assert.Contains("cursor=abc%2B%3D", RobloxServers.PageUrl("1", "abc+="), "a later page's cursor, escaped");
        }
    }
}
