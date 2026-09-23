using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // Every window the app opens looks like the app's.
    //
    // The Accounts window showed WinForms' default icon in the taskbar, beside
    // the app's own on the main window and the account browser - the owner's
    // screenshot. Its name prompt did the same, and had a taskbar button of
    // its own besides.
    static class WindowIconTests
    {
        static Icon DefaultIcon()
        {
            using (Form f = new Form()) return f.Icon;
        }

        static WatchKit Kit()
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-icon-" + Guid.NewGuid().ToString("N") + ".dat"));
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(); };
            k.Log = delegate(string s) { };
            return k;
        }

        public static void TestTheAppHasAnIconToGive()
        {
            Assert.True(Ui.AppIcon != null, "the exe's own icon");
            Assert.True(ReferenceEquals(Ui.AppIcon, Ui.AppIcon), "taken out of the exe once and shared");
        }

        public static void TestTheAccountsWindowHasTheAppsIcon()
        {
            AccountStore store = new AccountStore(Path.Combine(Path.GetTempPath(), "rk-none-" + Guid.NewGuid().ToString("N")));
            using (AccountsDialog d = new AccountsDialog(store, delegate(string s) { }, null))
            {
                Assert.False(ReferenceEquals(d.Icon, DefaultIcon()), "not WinForms' default");
                Assert.True(ReferenceEquals(d.Icon, Ui.AppIcon), "the app's");
            }
        }

        public static void TestTheAccountPromptStaysOutOfTheTaskbar()
        {
            TextBox box;
            using (Form f = AccountsDialog.MakePrompt("What is it for?", "farming", out box))
            {
                Assert.False(f.ShowInTaskbar, "no taskbar button of its own");
                Assert.True(ReferenceEquals(f.Icon, Ui.AppIcon), "and the app's icon for Alt+Tab");
                Assert.Equal("farming", box.Text, "what was there before");
            }
        }

        public static void TestEveryFramedWindowHasTheAppsIcon()
        {
            using (MacrosDialog d = new MacrosDialog(Kit()))
                Assert.True(ReferenceEquals(d.Icon, Ui.AppIcon), "the macro list, and everything else WatchUi.Frame builds");
        }
    }
}
