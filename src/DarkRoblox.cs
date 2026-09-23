using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    // Roblox's website in its own dark theme, inside the account browser.
    //
    // Measured on roblox.com: the site doesn't follow Windows' dark setting.
    // Its Theme.js takes, in order, a theme class the server put on <body>, a
    // RBXThemeOverride cookie, whatever the page last chose (localStorage),
    // and otherwise light - and it offers its own switch,
    // Roblox['core-scripts']['color-mode'].setMode. Using those gives Roblox's
    // real dark theme rather than a filter laid over a light one.
    //
    // Nothing is sent to Roblox's account settings. The account's own theme
    // choice stays as it is everywhere else; this is only this browser.
    static class DarkRoblox
    {
        public const string CookieName = "RBXThemeOverride";
        public const string CookieValue = "dark";
        public const string CookieDomain = ".roblox.com";

        // Runs at the start of every page. Only Roblox's pages are touched.
        // A server-set light class beats the cookie in Theme.js, so once the
        // page is ready its own switch is used, the classes are swapped if it
        // has none, and a light class put back later is swapped again.
        public const string Script = @"(function () {
  if (!/(^|\.)roblox\.com$/i.test(location.hostname)) return;
  function dark() {
    var b = document.body;
    if (!b) return;
    try {
      var cm = window.Roblox && window.Roblox['core-scripts'] && window.Roblox['core-scripts']['color-mode'];
      if (cm && cm.getMode && cm.getMode() !== 'dark') cm.setMode('dark');
    } catch (e) { }
    if (b.classList.contains('light-theme')) {
      b.classList.remove('light-theme');
      b.classList.add('dark-theme');
    }
  }
  function watch() {
    dark();
    if (!document.body || !window.MutationObserver) return;
    new MutationObserver(function () {
      if (document.body.classList.contains('light-theme')) dark();
    }).observe(document.body, { attributes: true, attributeFilter: ['class'] });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch);
  else watch();
  window.addEventListener('load', dark);
})();";
    }

    // One press of Play, one client.
    //
    // A page can start a client by navigating, by navigating a frame, or by
    // asking for an external protocol - and one press can raise more than one
    // of those. The same launch seen again within a few seconds is the same
    // press.
    class LaunchOnce
    {
        static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
        readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();

        public bool Take(string uri, DateTime now)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            DateTime at;
            if (seen.TryGetValue(uri, out at) && now - at < Window) return false;
            seen[uri] = now;
            return true;
        }
    }
}
