using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;

namespace RobloxKeeper
{
    class WebhookRequest
    {
        public string Url;
        public string Boundary;
        public string ContentType;
        public byte[] Body;
    }

    // Turning a detection into the request Discord expects.
    //
    // Building it and sending it are separate on purpose. Everything that can
    // go wrong is in the building - a quote in a chat line typed by a
    // stranger, a missing reference to the attachment, a URL that is not a
    // webhook at all - and none of that should need a network to test. Send
    // does nothing but post what it is handed.
    static class WebhookPost
    {
        // What Send says when it fails. Named, because whether to try again
        // depends on which one it was.
        public const string Unreachable = "couldn't reach Discord";
        public const string RateLimited = "Discord is rate-limiting us";
        public const string Gone = "that webhook no longer exists";

        public static string PayloadJson(DetectionEvent d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"embeds\":[{");
            sb.Append("\"title\":\"").Append(Escape(d.WatcherName)).Append(" found\",");
            sb.Append("\"color\":3066993,");
            sb.Append("\"timestamp\":\"")
              .Append(d.When.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))
              .Append("\",");

            // What was actually seen. For a chat watcher that is the whole
            // line; for the others, the word or template that hit.
            string said = string.IsNullOrEmpty(d.Line) ? d.Matched : d.Line;
            sb.Append("\"description\":\"");
            if (!string.IsNullOrEmpty(said))
                sb.Append("```\\n").Append(Escape(said)).Append("\\n```");
            string link = d.JoinLink;
            if (link != null)
                sb.Append("\\n[Join this server](").Append(Escape(link)).Append(")");
            sb.Append("\",");

            sb.Append("\"fields\":[");
            List<string> fields = new List<string>();
            fields.Add(Field("Client", d.ClientLabel));
            if (!string.IsNullOrEmpty(d.AccountName)) fields.Add(Field("Account", d.AccountName));
            if (!string.IsNullOrEmpty(d.PlaceId)) fields.Add(Field("Place", d.PlaceId));
            sb.Append(string.Join(",", fields.ToArray()));
            sb.Append("],");

            // Always present. An alert you cannot check is worse than none, so
            // the embed always points at the picture - and when there is no
            // picture Discord simply shows nothing, which is honest.
            sb.Append("\"image\":{\"url\":\"attachment://crop.png\"}");
            sb.Append("}]}");
            return sb.ToString();
        }

        static string Field(string name, string value)
        {
            return "{\"name\":\"" + Escape(name) + "\",\"value\":\""
                 + Escape(value) + "\",\"inline\":true}";
        }

        public static WebhookRequest Build(string url, DetectionEvent d, byte[] pngBytes)
        {
            WebhookRequest r = new WebhookRequest();
            r.Url = url;
            r.Boundary = "RobloxKeeper" + DateTime.UtcNow.Ticks.ToString("x");
            r.ContentType = "multipart/form-data; boundary=" + r.Boundary;

            MemoryStream body = new MemoryStream();
            Write(body, "--" + r.Boundary + "\r\n");
            Write(body, "Content-Disposition: form-data; name=\"payload_json\"\r\n");
            Write(body, "Content-Type: application/json\r\n\r\n");
            Write(body, PayloadJson(d));
            Write(body, "\r\n");

            if (pngBytes != null && pngBytes.Length > 0)
            {
                Write(body, "--" + r.Boundary + "\r\n");
                Write(body, "Content-Disposition: form-data; name=\"files[0]\"; filename=\"crop.png\"\r\n");
                Write(body, "Content-Type: image/png\r\n\r\n");
                body.Write(pngBytes, 0, pngBytes.Length);
                Write(body, "\r\n");
            }

            // Without the trailing dashes the body is malformed and the whole
            // request is rejected.
            Write(body, "--" + r.Boundary + "--\r\n");

            r.Body = body.ToArray();
            return r;
        }

        static void Write(Stream s, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        // JSON string escaping, in full.
        //
        // Chat lines are typed by strangers and go straight in here. One
        // unescaped character and every webhook after it fails with a parse
        // error nobody ever sees.
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // Checked where it is typed, so a mistake shows up then rather than as
        // silence three hours later.
        //
        // https only: the token in that URL is enough for anyone who sees it
        // to post into the channel.
        public static bool LooksLikeDiscordWebhook(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;

            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;

            // Matched exactly, or as a subdomain with the dot included -
            // "notdiscord.com" and "discord.com.evil.net" are somebody else.
            string host = u.Host.ToLowerInvariant();
            bool discord = host == "discord.com" || host == "discordapp.com"
                        || host.EndsWith(".discord.com") || host.EndsWith(".discordapp.com");

            return discord && u.AbsolutePath.StartsWith("/api/webhooks/",
                                                        StringComparison.OrdinalIgnoreCase);
        }

        // Null on success, a short reason on failure. Never throws: a webhook
        // that cannot be delivered is a line in the activity log, not a dialog
        // in front of a game.
        public static string Send(WebhookRequest r)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(r.Url);
                req.Method = "POST";
                req.ContentType = r.ContentType;
                req.ContentLength = r.Body.Length;
                req.Timeout = 15000;
                using (Stream s = req.GetRequestStream()) s.Write(r.Body, 0, r.Body.Length);
                using (WebResponse resp = req.GetResponse()) { resp.Close(); }
                return null;
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp == null) return Unreachable;
                if ((int)resp.StatusCode == 429) return RateLimited;
                if ((int)resp.StatusCode == 404) return Gone;
                return "Discord refused it (" + (int)resp.StatusCode + ")";
            }
            catch { return "couldn't send it"; }
        }

        // How long to wait before each retry: soon, then later, then a good
        // while - about forty seconds all told.
        public static readonly int[] RetryDelaysMs = { 2000, 10000, 30000 };

        // A server that could not be reached, that asked us to slow down, or
        // that had a moment of trouble may well work shortly. A webhook that
        // has been deleted, or a request Discord refused, never will.
        public static bool WorthRetrying(string reason)
        {
            if (reason == null) return false;
            return reason == Unreachable || reason == RateLimited
                || reason.StartsWith("Discord refused it (5", StringComparison.Ordinal);
        }

        // Sends, and tries again after each delay while it is worth trying.
        // Null on success, otherwise the last reason. The attempt and the wait
        // are passed in, so the schedule is tested without a network or a clock.
        public static string SendWithRetry(Func<string> attempt, int[] delaysMs, Action<int> wait)
        {
            string reason = attempt();
            foreach (int ms in delaysMs)
            {
                if (!WorthRetrying(reason)) return reason;
                wait(ms);
                reason = attempt();
            }
            return reason;
        }

        // The picture as the PNG the embed points at, or null for none.
        public static byte[] Png(Pixels p)
        {
            if (p == null || p.IsEmpty) return null;
            using (Bitmap b = p.ToBitmap())
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
