using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // Putting lists in the order you want - asked for by the owner for
    // accounts, "and the same for macros etc". Every list uses the same grip:
    // drag it, or right-click it to move a row to the top, up, down or to the
    // bottom, or to make a copy.
    static class RowOrderTests
    {
        static List<string> L(params string[] s) { return new List<string>(s); }
        static string J(IList<string> l) { return string.Join(",", new List<string>(l).ToArray()); }

        // Dropping between rows: the gap nearest the pointer.
        public static void TestTheGapUnderThePointer()
        {
            Assert.Equal(0, RowOrder.SlotAt(-50, 30, 4), "above everything - the top");
            Assert.Equal(0, RowOrder.SlotAt(10, 30, 4), "the top half of the first row");
            Assert.Equal(1, RowOrder.SlotAt(20, 30, 4), "its bottom half - under it");
            Assert.Equal(4, RowOrder.SlotAt(500, 30, 4), "below everything - the bottom");
        }

        // A row dropped in a gap below where it was lands one higher, because
        // it has left its old place.
        public static void TestWhereARowLands()
        {
            Assert.Equal(2, RowOrder.TargetIndex(0, 3), "moving down");
            Assert.Equal(1, RowOrder.TargetIndex(3, 1), "moving up");
            Assert.Equal(2, RowOrder.TargetIndex(2, 3), "just under itself - nowhere");
            Assert.Equal(2, RowOrder.TargetIndex(2, 2), "just above itself - nowhere");
        }

        public static void TestMovingARow()
        {
            List<string> l = L("a", "b", "c", "d");
            Assert.True(RowOrder.Move(l, 0, 2), "moved");
            Assert.Equal("b,c,a,d", J(l), "a is third");
            Assert.True(RowOrder.Move(l, 3, 0), "moved");
            Assert.Equal("d,b,c,a", J(l), "d is first");
            Assert.False(RowOrder.Move(l, 1, 1), "nowhere to go");
            Assert.False(RowOrder.Move(l, 5, 0), "not a row");
            Assert.Equal("d,b,c,a", J(l), "and nothing changed");
        }

        public static void TestTheMenuMoves()
        {
            Assert.Equal(0, RowOrder.MenuTarget(2, 4, RowMove.Top), "to the top");
            Assert.Equal(1, RowOrder.MenuTarget(2, 4, RowMove.Up), "up one");
            Assert.Equal(3, RowOrder.MenuTarget(2, 4, RowMove.Down), "down one");
            Assert.Equal(3, RowOrder.MenuTarget(2, 4, RowMove.Bottom), "to the bottom");
            Assert.Equal(-1, RowOrder.MenuTarget(0, 4, RowMove.Up), "the first can't go up");
            Assert.Equal(-1, RowOrder.MenuTarget(3, 4, RowMove.Bottom), "the last is already at the bottom");
        }

        // A copy needs a name nothing else has - macros and watchers are
        // picked by name.
        public static void TestACopyGetsANameOfItsOwn()
        {
            List<string> taken = L("Buy egg", "Buy egg copy");
            Func<string, bool> isTaken = delegate(string n) { return taken.Contains(n); };
            Assert.Equal("Sell egg copy", RowOrder.CopyName("Sell egg", isTaken), "the first copy");
            Assert.Equal("Buy egg copy 2", RowOrder.CopyName("Buy egg", isTaken), "the next free one");
        }

        // ---------- the grip ----------

        static void Mouse(RowGrip g, string what, MouseButtons button, int y)
        {
            typeof(RowGrip).GetMethod(what, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(g, new object[] { new MouseEventArgs(button, 1, 5, y, 0) });
        }

        static RowGrip[] List(ScrollPanel list, int rows, int rowHeight, Action<int, int> moved)
        {
            RowGrip[] grips = new RowGrip[rows];
            for (int i = 0; i < rows; i++)
            {
                grips[i] = RowGrip.For(i, rows, i * rowHeight, rowHeight, 0, moved, null);
                list.Controls.Add(grips[i]);
            }
            return grips;
        }

        public static void TestDraggingTheGripMovesTheRow()
        {
            using (ScrollPanel list = new ScrollPanel())
            {
                list.Size = new System.Drawing.Size(400, 300);
                string moved = null;
                RowGrip[] g = List(list, 4, 30, delegate(int from, int to) { moved = from + ">" + to; });

                Mouse(g[0], "OnMouseDown", MouseButtons.Left, 10);
                Mouse(g[0], "OnMouseMove", MouseButtons.Left, 10 + 75);     // into the third row's lower half
                Mouse(g[0], "OnMouseUp", MouseButtons.Left, 10 + 75);
                Assert.Equal("0>2", moved, "the first row is now third");
                Assert.Equal(4, list.Controls.Count, "and the line showing where it would land is gone");
            }
        }

        // A press that barely moves is a click, not a drag.
        public static void TestATwitchIsNotADrag()
        {
            using (ScrollPanel list = new ScrollPanel())
            {
                list.Size = new System.Drawing.Size(400, 300);
                string moved = null;
                RowGrip[] g = List(list, 4, 30, delegate(int from, int to) { moved = from + ">" + to; });
                Mouse(g[1], "OnMouseDown", MouseButtons.Left, 10);
                Mouse(g[1], "OnMouseMove", MouseButtons.Left, 12);
                Mouse(g[1], "OnMouseUp", MouseButtons.Left, 12);
                Assert.Equal(null, moved, "nothing moved");
            }
        }

        // ---------- accounts ----------

        static AccountStore Store(params string[] names)
        {
            AccountStore s = new AccountStore(Path.Combine(Path.GetTempPath(), "rk-order-" + Guid.NewGuid().ToString("N")));
            foreach (string n in names)
            {
                RobloxAccount a = new RobloxAccount();
                a.Name = n;
                a.Cookie = "c";
                s.Add(a);
            }
            return s;
        }

        static string Names(AccountStore s)
        {
            List<string> n = new List<string>();
            foreach (RobloxAccount a in s.Accounts) n.Add(a.Name);
            return string.Join(",", n.ToArray());
        }

        public static void TestAccountsKeepTheOrderTheyArePutIn()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-order-" + Guid.NewGuid().ToString("N"));
            try
            {
                AccountStore s = new AccountStore(path);
                foreach (string n in new[] { "main", "alt1", "alt2" })
                {
                    RobloxAccount a = new RobloxAccount(); a.Name = n; a.Cookie = "c"; s.Add(a);
                }
                Assert.True(s.Move(2, 0), "moved");
                s.Save();
                AccountStore back = new AccountStore(path);
                back.Load();
                Assert.Equal("alt2,main,alt1", Names(back), "saved in that order");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // Signing an account in again - its cookie expired - used to send it
        // to the bottom of the list.
        public static void TestSigningInAgainKeepsItsPlace()
        {
            AccountStore s = Store("main", "alt1", "alt2");
            RobloxAccount again = new RobloxAccount();
            again.Name = "alt1";
            again.Cookie = "new";
            s.Add(again);
            Assert.Equal("main,alt1,alt2", Names(s), "still second");
            Assert.Equal("new", s.Find("alt1").Cookie, "with its new sign-in");
        }
    }
}
