using System;

namespace RobloxKeeper.Tests
{
    // Finding a player: their user id from their name, and where they are.
    // The shapes here are Roblox's real answers, measured on 2026-09-23.
    static class RobloxPlayersTests
    {
        public static void TestAUserIdFromAName()
        {
            string json = "{\"data\":[{\"requestedUsername\":\"Roblox\",\"hasVerifiedBadge\":true,\"id\":1,"
                        + "\"name\":\"Roblox\",\"displayName\":\"Roblox\"}]}";
            Assert.Equal("1", RobloxPlayers.UserIdFromLookup(json), "the id");
            Assert.Equal(null, RobloxPlayers.UserIdFromLookup("{\"data\":[]}"), "nobody by that name");
        }

        public static void TestSomeoneOnTheWebsite()
        {
            PlayerWhere w = RobloxPlayers.ParsePresence("{\"userPresences\":[{\"userPresenceType\":1,\"lastLocation\":\"Website\","
                + "\"placeId\":null,\"rootPlaceId\":null,\"gameId\":null,\"universeId\":null,\"userId\":156}]}");
            Assert.Equal(PlayerWhere.ONLINE, w.Type, "online");
            Assert.Equal(null, w.GameId, "in no server");
        }

        public static void TestSomeoneInAGameThisAccountMayJoin()
        {
            PlayerWhere w = RobloxPlayers.ParsePresence("{\"userPresences\":[{\"userPresenceType\":2,\"lastLocation\":\"Adopt Me!\","
                + "\"placeId\":920587237,\"rootPlaceId\":920587237,\"gameId\":\"eeeeeeee-0000-0000-0000-000000000000\","
                + "\"universeId\":383310974,\"userId\":555}]}");
            Assert.Equal(PlayerWhere.IN_GAME, w.Type, "in a game");
            Assert.Equal("920587237", w.PlaceId, "which");
            Assert.Equal("eeeeeeee-0000-0000-0000-000000000000", w.GameId, "and which server");
        }

        public static void TestNothingUnderstoodIsNotAPlace()
        {
            PlayerWhere w = RobloxPlayers.ParsePresence("{\"errors\":[{\"code\":0,\"message\":\"Unauthorized\"}]}");
            Assert.Equal(PlayerWhere.UNKNOWN, w.Type, "unknown");
        }
    }
}
