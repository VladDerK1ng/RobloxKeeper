using System;
using System.Text;

namespace RobloxKeeper.Tests
{
    // Building the Discord request, without ever sending one.
    //
    // A chat line is arbitrary text typed by a stranger. It reaches this code
    // and goes into JSON, so quoting it correctly is not a nicety - one
    // unescaped character and every webhook after it silently fails with a
    // parse error nobody ever sees.
    static class WebhookPostTests
    {
        const string Url = "https://discord.com/api/webhooks/1/abc";

        static DetectionEvent Egg()
        {
            DetectionEvent d = new DetectionEvent();
            d.WatcherName = "Secret Egg";
            d.ClientLabel = "Client 3";
            d.AccountName = "VladDerKing";
            d.PlaceId = "142823291";
            d.JobId = "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0";
            d.Matched = "secret";
            d.Line = "[SERVER] Restock: Rainbow Egg x3";
            d.When = new DateTime(2026, 9, 21, 14, 30, 0, DateTimeKind.Utc);
            return d;
        }

        public static void TestThePayloadNamesTheWatcherTheClientAndTheAccount()
        {
            string json = WebhookPost.PayloadJson(Egg());
            Assert.Contains("Secret Egg", json, "what was found");
            Assert.Contains("Client 3", json, "which window");
            Assert.Contains("VladDerKing", json, "whose account");
        }

        // For a chat watcher the useful part is the whole line, not the word
        // that caught it.
        public static void TestThePayloadCarriesTheWholeChatLine()
        {
            Assert.Contains("Restock: Rainbow Egg x3", WebhookPost.PayloadJson(Egg()),
                "the entire message");
        }

        public static void TestThePayloadLinksBackToTheServer()
        {
            Assert.Contains("gameInstanceId=55cf1f30-d19e-4a37-84aa-609ff3c1d3a0",
                WebhookPost.PayloadJson(Egg()), "the exact server");
        }

        // The picture is not optional. An alert you cannot check is worse than
        // no alert, so the embed always points at the attachment.
        public static void TestThePayloadReferencesTheAttachedPicture()
        {
            Assert.Contains("attachment://crop.png", WebhookPost.PayloadJson(Egg()),
                "the embed shows the crop");
        }

        // A stranger typed this.
        public static void TestQuotesAndBackslashesInAChatLineAreEscaped()
        {
            DetectionEvent d = Egg();
            d.Line = "he said \"hi\" and left C:\\temp";
            string json = WebhookPost.PayloadJson(d);
            Assert.Contains("\\\"hi\\\"", json, "quotes escaped");
            Assert.Contains("C:\\\\temp", json, "backslashes escaped");
        }

        public static void TestNewlinesAndControlCharactersAreEscaped()
        {
            DetectionEvent d = Egg();
            d.Line = "first\nsecond\ttabbed";
            string json = WebhookPost.PayloadJson(d);
            Assert.Contains("first\\nsecond", json, "newline escaped");
            Assert.Contains("\\t", json, "tab escaped");
            Assert.False(json.Contains("first\nsecond"), "no raw newline survived");
        }

        // A character below space would be rejected outright by a JSON parser.
        public static void TestControlCharactersBecomeEscapes()
        {
            DetectionEvent d = Egg();
            d.Line = "bell\u0007here";
            Assert.Contains("\\u0007", WebhookPost.PayloadJson(d), "escaped as a code point");
        }

        public static void TestADetectionWithNoServerHasNoJoinLink()
        {
            DetectionEvent d = Egg();
            d.JobId = null;
            Assert.False(WebhookPost.PayloadJson(d).Contains("gameInstanceId"),
                "no server, no link, rather than a link to nowhere");
        }

        // A text or image watcher has no chat line, and the message still has
        // to say what it found.
        public static void TestAWatcherWithNoChatLineStillSaysWhatItMatched()
        {
            DetectionEvent d = Egg();
            d.Line = null;
            Assert.Contains("secret", WebhookPost.PayloadJson(d), "the word that hit");
        }

        public static void TestTheRequestIsMultipartWithBothParts()
        {
            WebhookRequest r = WebhookPost.Build(Url, Egg(), new byte[] { 1, 2, 3, 4 });
            string body = Encoding.UTF8.GetString(r.Body);
            Assert.Contains("multipart/form-data", r.ContentType, "multipart");
            Assert.Contains(r.Boundary, r.ContentType, "the boundary is declared");
            Assert.Contains("name=\"payload_json\"", body, "the message");
            Assert.Contains("name=\"files[0]\"; filename=\"crop.png\"", body, "the picture");
            Assert.Contains("image/png", body, "declared as a png");
        }

        public static void TestTheBodyEndsWithTheClosingBoundary()
        {
            WebhookRequest r = WebhookPost.Build(Url, Egg(), new byte[] { 1 });
            string body = Encoding.UTF8.GetString(r.Body);
            Assert.True(body.TrimEnd().EndsWith("--" + r.Boundary + "--"),
                "an unterminated multipart body is rejected as malformed");
        }

        // Some detections have nothing to show - a crop that failed, or a
        // watcher with no region. The message still goes out.
        public static void TestARequestWithNoPictureIsStillValid()
        {
            WebhookRequest r = WebhookPost.Build(Url, Egg(), null);
            string body = Encoding.UTF8.GetString(r.Body);
            Assert.Contains("name=\"payload_json\"", body, "still has the message");
            Assert.False(body.Contains("files[0]"), "but no empty attachment");
        }

        // Catching a mistyped URL where it is entered, rather than as silence
        // three hours later.
        public static void TestItRecognisesADiscordWebhookUrl()
        {
            Assert.True(WebhookPost.LooksLikeDiscordWebhook(
                "https://discord.com/api/webhooks/123/abcDEF"), "the normal form");
            Assert.True(WebhookPost.LooksLikeDiscordWebhook(
                "https://discordapp.com/api/webhooks/123/abcDEF"), "the old host");
            Assert.False(WebhookPost.LooksLikeDiscordWebhook("https://example.com/hook"), "not discord");
            Assert.False(WebhookPost.LooksLikeDiscordWebhook("not a url at all"), "not a url");
            Assert.False(WebhookPost.LooksLikeDiscordWebhook(null), "nothing");
            Assert.False(WebhookPost.LooksLikeDiscordWebhook(""), "blank");
        }

        // http would put the webhook token on the wire in clear text, and that
        // token is enough for anyone who sees it to post into the channel.
        public static void TestAPlainHttpWebhookIsRejected()
        {
            Assert.False(WebhookPost.LooksLikeDiscordWebhook(
                "http://discord.com/api/webhooks/123/abc"), "must be https");
        }

        // A host that merely ends in something discord-ish is not Discord.
        public static void TestALookalikeHostIsRejected()
        {
            Assert.False(WebhookPost.LooksLikeDiscordWebhook(
                "https://notdiscord.com/api/webhooks/123/abc"), "different site");
            Assert.False(WebhookPost.LooksLikeDiscordWebhook(
                "https://discord.com.evil.net/api/webhooks/123/abc"), "a subdomain of somewhere else");
        }
    }
}
