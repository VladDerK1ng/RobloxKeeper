using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RobloxKeeper
{
    enum LuaKind { Keyword, String, Comment, Number, Function }

    class LuaSpan
    {
        public int Start, Length;
        public LuaKind Kind;
        public int End { get { return Start + Length; } }
    }

    // Which parts of a Lua script are a word of the language, text in
    // quotes, a comment, a number or a call of one of the app's own
    // functions. Pure: text in, ranges out, so what gets coloured can be
    // tested without a window. The whole text is read from the top each
    // time - a long string or a block comment can begin far above what is on
    // screen - but only as far as asked, so a script's end costs nothing
    // while its start is being looked at.
    static class LuaSyntax
    {
        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
        };

        public static bool IsKeyword(string word) { return Keywords.Contains(word); }

        // Every span that starts before upTo. functions: the names the app
        // adds; one is coloured only where it is called, so a variable that
        // happens to be named "count" is left alone.
        public static List<LuaSpan> Spans(string text, int upTo, ICollection<string> functions)
        {
            List<LuaSpan> found = new List<LuaSpan>();
            if (string.IsNullOrEmpty(text)) return found;
            int n = text.Length;
            int limit = Math.Min(upTo, n);
            int i = 0;
            while (i < limit)
            {
                char c = text[i];
                if (c == '-' && i + 1 < n && text[i + 1] == '-')
                {
                    int start = i;
                    i += 2;
                    int level;
                    if (i < n && text[i] == '[' && LongOpen(text, i, out level)) i = LongEnd(text, i, level);
                    else while (i < n && text[i] != '\n') i++;
                    Add(found, start, i, LuaKind.Comment);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int start = i;
                    i++;
                    while (i < n && text[i] != c && text[i] != '\n')
                    {
                        if (text[i] == '\\' && i + 1 < n && text[i + 1] != '\n') i++;
                        i++;
                    }
                    // A quote that closes on its line is part of the string;
                    // one that never does runs to the end of the line.
                    if (i < n && text[i] == c) i++;
                    Add(found, start, i, LuaKind.String);
                    continue;
                }
                if (c == '[')
                {
                    int level;
                    if (LongOpen(text, i, out level))
                    {
                        int start = i;
                        i = LongEnd(text, i, level);
                        Add(found, start, i, LuaKind.String);
                        continue;
                    }
                    i++;
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(text[i + 1])))
                {
                    int start = i;
                    i = NumberEnd(text, i);
                    Add(found, start, i, LuaKind.Number);
                    continue;
                }
                if (IsWordStart(c))
                {
                    int start = i;
                    while (i < n && IsWordPart(text[i])) i++;
                    string word = text.Substring(start, i - start);
                    if (Keywords.Contains(word)) Add(found, start, i, LuaKind.Keyword);
                    else if (functions != null && functions.Contains(word) && IsCalled(text, start, i))
                        Add(found, start, i, LuaKind.Function);
                    continue;
                }
                i++;
            }
            return found;
        }

        static void Add(List<LuaSpan> list, int start, int end, LuaKind kind)
        {
            if (end <= start) return;
            LuaSpan s = new LuaSpan();
            s.Start = start; s.Length = end - start; s.Kind = kind;
            list.Add(s);
        }

        static bool IsWordStart(char c) { return c == '_' || (c < 128 && char.IsLetter(c)); }
        static bool IsWordPart(char c) { return c == '_' || (c < 128 && char.IsLetterOrDigit(c)); }

        // name( - and not t.name( or t:name(, which is something else's.
        static bool IsCalled(string text, int start, int end)
        {
            if (start > 0 && (text[start - 1] == '.' || text[start - 1] == ':')) return false;
            int j = end;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
            return j < text.Length && text[j] == '(';
        }

        // "[[" or "[==[" at i: how many = signs it has.
        static bool LongOpen(string s, int i, out int level)
        {
            level = 0;
            if (i >= s.Length || s[i] != '[') return false;
            int j = i + 1;
            while (j < s.Length && s[j] == '=') { level++; j++; }
            return j < s.Length && s[j] == '[';
        }

        // Just past the "]]" that closes the one opening at i, or the end
        // of the text when it never closes.
        static int LongEnd(string s, int i, int level)
        {
            string close = "]" + new string('=', level) + "]";
            int at = s.IndexOf(close, i + level + 2, StringComparison.Ordinal);
            return at < 0 ? s.Length : at + close.Length;
        }

        static int NumberEnd(string s, int i)
        {
            int n = s.Length;
            if (s[i] == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X'))
            {
                i += 2;
                while (i < n && (Uri.IsHexDigit(s[i]) || s[i] == '.')) i++;
                return i;
            }
            while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                int j = i + 1;
                if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                if (j < n && char.IsDigit(s[j]))
                {
                    while (j < n && char.IsDigit(s[j])) j++;
                    i = j;
                }
            }
            return i;
        }
    }

    // What Enter and Tab do in the editor, as plain functions.
    static class LuaEdit
    {
        public const string TAB = "    ";

        static readonly Regex Opens = new Regex(@"(\b(then|do|else|repeat)|\bfunction\b.*\)|\{)\s*$", RegexOptions.Compiled);

        // The spaces a new line starts with: what the line above starts
        // with, and one step more after a line that opens a block.
        public static string IndentAfter(string line)
        {
            line = line ?? "";
            int n = 0;
            while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
            string indent = line.Substring(0, n);
            string rest = line.Substring(n).TrimEnd();
            if (rest.StartsWith("--", StringComparison.Ordinal)) return indent;
            // A comment on the end of the line doesn't change what it opens.
            int cut = rest.IndexOf("--", StringComparison.Ordinal);
            if (cut > 0 && !rest.Substring(0, cut).Contains("\"") && !rest.Substring(0, cut).Contains("'")) rest = rest.Substring(0, cut).TrimEnd();
            return Opens.IsMatch(rest) ? indent + TAB : indent;
        }
    }

    // What was typed, kept so Ctrl+Z can take it back. The editor's own undo
    // would undo the colouring as well - colours are text formatting to it -
    // so the editor keeps its own: a copy of the text each time typing
    // pauses, and at each change that isn't typing.
    class EditHistory
    {
        public class Snapshot
        {
            public string Text;
            public int Caret;
        }

        public const int KEEP = 200;

        readonly List<Snapshot> undo = new List<Snapshot>();
        readonly List<Snapshot> redo = new List<Snapshot>();
        Snapshot now;

        public EditHistory(string text) { now = Make(text, 0); }

        static Snapshot Make(string text, int caret)
        {
            Snapshot s = new Snapshot();
            s.Text = text ?? "";
            s.Caret = caret;
            return s;
        }

        public string Latest { get { return now.Text; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        // The text is now this. A change makes a step to go back to, and
        // forgets what was undone: it is a different future now.
        public void Record(string text, int caret)
        {
            text = text ?? "";
            if (text == now.Text) { now.Caret = caret; return; }
            undo.Add(now);
            if (undo.Count > KEEP) undo.RemoveAt(0);
            now = Make(text, caret);
            redo.Clear();
        }

        // The text to go back to, or null when there is none.
        public Snapshot Undo(string text, int caret)
        {
            Record(text, caret);
            if (undo.Count == 0) return null;
            redo.Add(now);
            now = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            return now;
        }

        public Snapshot Redo(string text, int caret)
        {
            Record(text, caret);
            if (redo.Count == 0) return null;
            undo.Add(now);
            now = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            return now;
        }
    }

    // A RichTextBox that says when it scrolled, and gives its own keys first
    // call. It is made quiet by what it leaves out: its own undo, its own
    // bold and italic, and pasted formatting.
    class QuietRich : RichTextBox
    {
        const int WM_VSCROLL = 0x115, WM_HSCROLL = 0x114, WM_MOUSEWHEEL = 0x20A;

        public event EventHandler Scrolled;

        // Asked of the editor before the box acts on a key; true when taken.
        public Func<Keys, bool> KeyFirst;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (KeyFirst != null && KeyFirst(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if ((m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL || m.Msg == WM_MOUSEWHEEL) && Scrolled != null)
                Scrolled(this, EventArgs.Empty);
        }
    }

    // The numbers down the left of the editor, with a mark on the line a
    // script is on.
    class LuaGutter : Control
    {
        public const int WIDTH = 46;

        readonly RichTextBox box;
        public int RunLine;             // 1-based; 0 for none

        public LuaGutter(RichTextBox box)
        {
            this.box = box;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Width = WIDTH;
            Font = box.Font;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int lines = Math.Max(1, box.GetLineFromCharIndex(Math.Max(0, box.TextLength)) + 1);
            int first;
            try { first = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(0, 0))); }
            catch { first = 0; }
            for (int line = first; line < lines; line++)
            {
                int at = box.GetFirstCharIndexFromLine(line);
                if (at < 0) break;
                Point p = box.GetPositionFromCharIndex(at);
                if (p.Y >= Height) break;
                int h = LineHeight;
                bool on = line + 1 == RunLine;
                if (on)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(70, Theme.Accent)))
                        g.FillRectangle(b, 0, p.Y, Width, h);
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, 0, p.Y, 3, h);
                }
                TextRenderer.DrawText(g, (line + 1).ToString(), Font, new Rectangle(0, p.Y, Width - 8, h),
                                      on ? Theme.Text : Theme.Muted,
                                      TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            using (Pen p = new Pen(Color.FromArgb(52, 52, 74)))
                g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }

        public int LineHeight { get { return Math.Max(8, TextRenderer.MeasureText("Ag", Font, Size.Empty, TextFormatFlags.NoPadding).Height); } }
    }

    // The place scripts are written: Consolas, line numbers, Lua words in
    // colour, Tab for four spaces, and the line a running script is on.
    //
    // Colouring is the part that could make typing lag, so it is kept small:
    // it waits for a pause in typing, reads the script from the top but
    // colours only the lines on screen and a few around them, and does that
    // again when scrolling brings new lines into view.
    class LuaEditor : Panel
    {
        // Lines coloured above and below the ones in view, so a short scroll
        // shows colour at once.
        public const int MARGIN_LINES = 12;
        const int COLOUR_DELAY_MS = 150;
        const int SCROLL_DELAY_MS = 40;
        const int HISTORY_DELAY_MS = 500;

        public static readonly Color KeywordColour = Color.FromArgb(203, 166, 247);
        public static readonly Color StringColour = Color.FromArgb(166, 227, 161);
        public static readonly Color CommentColour = Color.FromArgb(108, 112, 134);
        public static readonly Color NumberColour = Color.FromArgb(250, 179, 135);
        public static readonly Color FunctionColour = Color.FromArgb(137, 180, 250);

        readonly QuietRich box;
        readonly LuaGutter gutter;
        readonly Timer colourTimer = new Timer();
        readonly Timer historyTimer = new Timer();
        readonly ICollection<string> functions;
        EditHistory history = new EditHistory("");
        string saved = "";                  // the text as it was last saved, with \n for line ends
        bool busy;                          // our own changes to the box: not typing
        bool dirty;
        int colouredFrom = -1, colouredTo = -1;
        int runLine;
        int litFrom = -1, litLength;        // where the running line's background is now

        public event EventHandler Changed;          // the text is not what it was
        public event EventHandler DirtyChanged;     // saved or not saved
        public event EventHandler SaveRequested;    // Ctrl+S

        public LuaEditor(ICollection<string> functions)
        {
            this.functions = functions;
            BackColor = Theme.Inset;

            box = new QuietRich();
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Inset;
            box.ForeColor = Theme.Text;
            box.Font = Ui.F("Consolas", 9.75f);
            box.WordWrap = false;
            box.AcceptsTab = true;
            box.DetectUrls = false;
            box.HideSelection = false;
            box.ShortcutsEnabled = false;
            box.EnableAutoDragDrop = false;
            box.ScrollBars = RichTextBoxScrollBars.ForcedBoth;
            box.Location = new Point(LuaGutter.WIDTH, 0);
            box.HandleCreated += delegate { DarkScrollbars.Apply(box); };
            box.TextChanged += delegate { OnBoxChanged(); };
            box.KeyDown += BoxKeyDown;
            box.KeyFirst = KeyFirst;
            box.Scrolled += delegate { gutter.Invalidate(); ColourSoon(SCROLL_DELAY_MS); };
            box.ContextMenuStrip = MakeMenu();

            gutter = new LuaGutter(box);
            Controls.Add(box);
            Controls.Add(gutter);

            colourTimer.Tick += delegate
            {
                colourTimer.Stop();
                // Not while a button is down - a selection is being dragged - and
                // not while nobody can see it: coloured when it is shown.
                if (!IsHandleCreated || !Visible) return;
                if (Control.MouseButtons != MouseButtons.None) ColourSoon(COLOUR_DELAY_MS);
                else ColourNow();
            };
            VisibleChanged += delegate { if (Visible) ColourSoon(SCROLL_DELAY_MS); };
            historyTimer.Interval = HISTORY_DELAY_MS;
            historyTimer.Tick += delegate { historyTimer.Stop(); Remember(); };
            Resize += delegate { Place(); };
            Place();
        }

        void Place()
        {
            gutter.SetBounds(0, 0, LuaGutter.WIDTH, Height);
            box.SetBounds(LuaGutter.WIDTH, 0, Math.Max(10, Width - LuaGutter.WIDTH), Height);
            ColourSoon(SCROLL_DELAY_MS);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { colourTimer.Dispose(); historyTimer.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------- the text ----------

        // Line ends in the box are \n; in files they are \r\n.
        public static string Plain(string s) { return (s ?? "").Replace("\r\n", "\n"); }

        // The script, as it would be written to its file.
        public string Code { get { return box.Text.Replace("\n", "\r\n"); } }

        // The text as the box holds it.
        public string PlainText { get { return box.Text; } }

        public bool Dirty { get { return dirty; } }
        public int CaretAt { get { return box.SelectionStart; } }
        public int SelectedLength { get { return box.SelectionLength; } }
        public bool IsReadOnly { get { return box.ReadOnly; } set { box.ReadOnly = value; } }
        public RichTextBox Box { get { return box; } }

        // A script has been loaded: it is as saved, and nothing can be
        // undone back past it.
        public void Load(string code)
        {
            string text = Plain(code);
            busy = true;
            try
            {
                box.Text = text;
                box.Select(0, 0);
                ResetFormat();
            }
            finally { busy = false; }
            saved = box.Text;
            history = new EditHistory(box.Text);
            colouredFrom = colouredTo = -1;
            litFrom = -1;
            SetDirty(false);
            ColourNow();
            gutter.Invalidate();
        }

        // It has been written to its file.
        public void MarkSaved()
        {
            saved = box.Text;
            SetDirty(false);
        }

        void SetDirty(bool now)
        {
            if (dirty == now) return;
            dirty = now;
            if (DirtyChanged != null) DirtyChanged(this, EventArgs.Empty);
        }

        void OnBoxChanged()
        {
            // Colouring and the running line are changes to the box too, and
            // are not typing.
            if (busy) return;
            colouredFrom = colouredTo = -1;
            litFrom = -1;
            SetDirty(box.Text != saved);
            historyTimer.Stop();
            historyTimer.Start();
            ColourSoon(COLOUR_DELAY_MS);
            gutter.Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        // Typing is kept as a step each time it pauses; the step is cut at
        // once for anything else, before it is done.
        void Remember()
        {
            history.Record(box.Text, box.SelectionStart);
        }

        public void Insert(string text)
        {
            if (box.ReadOnly || string.IsNullOrEmpty(text)) return;
            Remember();
            box.SelectedText = text;
            Remember();
            box.Focus();
        }

        // Where the caret is, after the line it is in: for a picture's name
        // or a function's call.
        public void InsertAtCursor(string text) { Insert(text); }

        public void InsertTab() { Insert(LuaEdit.TAB); }

        // Enter: the new line starts under the one above.
        public void InsertNewLine()
        {
            if (box.ReadOnly) return;
            int line = box.GetLineFromCharIndex(box.SelectionStart);
            int start = box.GetFirstCharIndexFromLine(line);
            string before = start < 0 ? "" : box.Text.Substring(start, Math.Max(0, box.SelectionStart - start));
            Insert("\n" + LuaEdit.IndentAfter(before));
        }

        public void SelectEverything() { box.SelectAll(); }

        public bool Undo()
        {
            EditHistory.Snapshot s = history.Undo(box.Text, box.SelectionStart);
            if (s == null) return false;
            Put(s);
            return true;
        }

        public bool Redo()
        {
            EditHistory.Snapshot s = history.Redo(box.Text, box.SelectionStart);
            if (s == null) return false;
            Put(s);
            return true;
        }

        void Put(EditHistory.Snapshot s)
        {
            busy = true;
            try
            {
                box.Text = s.Text;
                box.Select(Math.Min(s.Caret, box.TextLength), 0);
                ResetFormat();
            }
            finally { busy = false; }
            colouredFrom = colouredTo = -1;
            litFrom = -1;
            SetDirty(box.Text != saved);
            ColourNow();
            gutter.Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        void ResetFormat()
        {
            box.SelectAll();
            box.SelectionColor = Theme.Text;
            box.SelectionBackColor = box.BackColor;
            box.Select(0, 0);
        }

        // ---------- keys ----------

        bool KeyFirst(Keys key)
        {
            if (key == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (key == (Keys.Control | Keys.Y) || key == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            if (key == (Keys.Control | Keys.S)) { if (SaveRequested != null) SaveRequested(this, EventArgs.Empty); return true; }
            if (key == (Keys.Control | Keys.A)) { box.SelectAll(); return true; }
            if (key == (Keys.Control | Keys.C) || key == (Keys.Control | Keys.Insert)) { CopySelection(); return true; }
            if (key == (Keys.Control | Keys.X) || key == (Keys.Shift | Keys.Delete)) { CutSelection(); return true; }
            if (key == (Keys.Control | Keys.V) || key == (Keys.Shift | Keys.Insert)) { PasteText(); return true; }
            // Bold, italic, alignment: a script has no use for them.
            if (key == (Keys.Control | Keys.B) || key == (Keys.Control | Keys.I) || key == (Keys.Control | Keys.U) ||
                key == (Keys.Control | Keys.E) || key == (Keys.Control | Keys.L) || key == (Keys.Control | Keys.R) ||
                key == (Keys.Control | Keys.J)) return true;
            return false;
        }

        void BoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab && !e.Control && !e.Alt)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                InsertTab();
            }
            else if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                InsertNewLine();
            }
        }

        void CopySelection()
        {
            if (box.SelectionLength == 0) return;
            try { Clipboard.SetText(box.SelectedText.Replace("\n", "\r\n")); } catch { }
        }

        void CutSelection()
        {
            if (box.SelectionLength == 0 || box.ReadOnly) return;
            CopySelection();
            Remember();
            box.SelectedText = "";
            Remember();
        }

        void PasteText()
        {
            if (box.ReadOnly) return;
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { text = null; }
            if (string.IsNullOrEmpty(text)) return;
            Insert(Plain(text));
        }

        ContextMenuStrip MakeMenu()
        {
            ContextMenuStrip m = DarkMenu.Make();
            m.Items.Add("Undo", null, delegate { Undo(); });
            m.Items.Add("Redo", null, delegate { Redo(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Cut", null, delegate { CutSelection(); });
            m.Items.Add("Copy", null, delegate { CopySelection(); });
            m.Items.Add("Paste", null, delegate { PasteText(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Select all", null, delegate { box.SelectAll(); });
            return m;
        }

        // ---------- colour ----------

        public static Color ColourOf(LuaKind kind)
        {
            switch (kind)
            {
                case LuaKind.Keyword: return KeywordColour;
                case LuaKind.String: return StringColour;
                case LuaKind.Comment: return CommentColour;
                case LuaKind.Number: return NumberColour;
                default: return FunctionColour;
            }
        }

        void ColourSoon(int ms)
        {
            colourTimer.Stop();
            colourTimer.Interval = ms;
            colourTimer.Start();
        }

        // The lines in view and MARGIN_LINES around them, as a range of
        // characters.
        public void VisibleRange(out int from, out int to)
        {
            int length = box.TextLength;
            int first, last;
            try
            {
                first = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(0, 0)));
                last = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(0, Math.Max(0, box.ClientSize.Height - 1))));
            }
            catch { first = 0; last = 0; }
            int top = Math.Max(0, first - MARGIN_LINES);
            int bottom = last + MARGIN_LINES + 1;
            from = Math.Max(0, box.GetFirstCharIndexFromLine(top));
            int end = box.GetFirstCharIndexFromLine(bottom);
            to = end < 0 ? length : end;
        }

        // Colours what is in view that is not coloured yet. Nothing is done
        // when the lines in view are already coloured.
        public void ColourNow()
        {
            colourTimer.Stop();
            int from, to;
            VisibleRange(out from, out to);
            if (colouredFrom >= 0 && from >= colouredFrom && to <= colouredTo) return;
            PaintRange(from, to);
        }

        // For a measurement or a test: colours the lines in view whatever
        // was coloured before.
        public void Recolour()
        {
            colouredFrom = colouredTo = -1;
            ColourNow();
        }

        public int ColouredFrom { get { return colouredFrom; } }
        public int ColouredTo { get { return colouredTo; } }

        void PaintRange(int from, int to)
        {
            if (to <= from) { colouredFrom = from; colouredTo = to; return; }
            string text = box.Text;
            List<LuaSpan> spans = LuaSyntax.Spans(text, to, functions);

            KeptView kept = KeepView();
            busy = true;
            Redraw(false);
            try
            {
                box.Select(from, to - from);
                box.SelectionColor = Theme.Text;
                box.SelectionBackColor = box.BackColor;
                foreach (LuaSpan s in spans)
                {
                    if (s.End <= from) continue;
                    int a = Math.Max(s.Start, from), b = Math.Min(s.End, to);
                    if (b <= a) continue;
                    box.Select(a, b - a);
                    box.SelectionColor = ColourOf(s.Kind);
                }
                LightRunLine();
                PutBack(kept);
            }
            finally
            {
                Redraw(true);
                busy = false;
            }
            colouredFrom = from;
            colouredTo = to;
            box.Invalidate();
            gutter.Invalidate();
        }

        // What you had before the box's selection was borrowed: the
        // selection, which end of it the cursor is at, and the line at the
        // top of the view.
        struct KeptView
        {
            public int Start, Length, Top;
            public bool Backward;
        }

        KeptView KeepView()
        {
            KeptView k;
            k.Start = box.SelectionStart;
            k.Length = box.SelectionLength;
            k.Backward = SelectionBackward;
            k.Top = FirstVisibleLine();
            return k;
        }

        // Puts it back. Selecting scrolls the box to the cursor, so the view
        // is scrolled back to where it was after; and a selection made
        // upwards is put back upwards (EM_SETSEL with the start after the
        // end), or Select would turn it round and the next Shift+arrow grow
        // it from the wrong end.
        void PutBack(KeptView k)
        {
            int start = Math.Min(k.Start, box.TextLength);
            int length = Math.Min(k.Length, Math.Max(0, box.TextLength - start));
            if (k.Backward && length > 0) Native.SendMessage(box.Handle, 0x00B1 /* EM_SETSEL */, (IntPtr)(start + length), (IntPtr)start);
            else box.Select(start, length);
            int moved = FirstVisibleLine() - k.Top;
            if (moved != 0) Native.SendMessage(box.Handle, 0x00B6 /* EM_LINESCROLL */, IntPtr.Zero, (IntPtr)(-moved));
        }

        // The cursor is at the start of the selection: it was made upwards
        // or leftwards. SelectionStart and SelectionLength can't say, so the
        // box's own text object (TOM) is asked; if it won't answer, the
        // selection is taken as made forwards, as Select would leave it.
        public bool SelectionBackward
        {
            get
            {
                if (!box.IsHandleCreated || box.SelectionLength == 0) return false;
                IntPtr slot = Marshal.AllocHGlobal(IntPtr.Size);
                object doc = null, sel = null;
                try
                {
                    Marshal.WriteIntPtr(slot, IntPtr.Zero);
                    Native.SendMessage(box.Handle, 0x043C /* EM_GETOLEINTERFACE */, IntPtr.Zero, slot);
                    IntPtr unk = Marshal.ReadIntPtr(slot);
                    if (unk == IntPtr.Zero) return false;
                    try { doc = Marshal.GetObjectForIUnknown(unk); }
                    finally { Marshal.Release(unk); }
                    sel = doc.GetType().InvokeMember("Selection", BindingFlags.GetProperty, null, doc, null);
                    if (sel == null) return false;
                    int flags = Convert.ToInt32(sel.GetType().InvokeMember("Flags", BindingFlags.GetProperty, null, sel, null));
                    return (flags & 1 /* tomSelStartActive */) != 0;
                }
                catch { return false; }
                finally
                {
                    Marshal.FreeHGlobal(slot);
                    if (sel != null && Marshal.IsComObject(sel)) Marshal.ReleaseComObject(sel);
                    if (doc != null && Marshal.IsComObject(doc)) Marshal.ReleaseComObject(doc);
                }
            }
        }

        int FirstVisibleLine()
        {
            return (int)Native.SendMessage(box.Handle, 0x00CE /* EM_GETFIRSTVISIBLELINE */, IntPtr.Zero, IntPtr.Zero);
        }

        void Redraw(bool on)
        {
            Native.SendMessage(box.Handle, 0x000B /* WM_SETREDRAW */, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
        }

        // ---------- the line a script is on ----------

        // 1-based; 0 for none. The gutter marks it, and so does a band of
        // colour behind its text.
        public int RunLine
        {
            get { return runLine; }
            set
            {
                if (value < 0) value = 0;
                if (value == runLine) return;
                runLine = value;
                gutter.RunLine = value;
                gutter.Invalidate();
                if (!box.IsHandleCreated) return;
                // Kept and put back as PaintRange does: the band is put on by
                // selecting the line, which would scroll the view to it and
                // turn an upwards selection round.
                KeptView kept = KeepView();
                busy = true;
                Redraw(false);
                try
                {
                    LightRunLine();
                    PutBack(kept);
                }
                finally { Redraw(true); busy = false; }
                box.Invalidate();
            }
        }

        // Puts the band on the running line and takes it off the one it was
        // on. With the box's selection free to move: the caller puts it back.
        void LightRunLine()
        {
            if (litFrom >= 0 && litFrom <= box.TextLength)
            {
                box.Select(litFrom, Math.Min(litLength, box.TextLength - litFrom));
                box.SelectionBackColor = box.BackColor;
            }
            litFrom = -1;
            if (runLine <= 0) return;
            int start = box.GetFirstCharIndexFromLine(runLine - 1);
            if (start < 0) return;
            int next = box.GetFirstCharIndexFromLine(runLine);
            int end = next < 0 ? box.TextLength : next - 1;
            if (end <= start) return;
            box.Select(start, end - start);
            box.SelectionBackColor = Color.FromArgb(52, 48, 96);
            litFrom = start;
            litLength = end - start;
        }

        public void ScrollToLine(int line)
        {
            int at = box.GetFirstCharIndexFromLine(Math.Max(0, line - 1));
            if (at < 0) return;
            box.Select(at, 0);
            box.ScrollToCaret();
        }
    }
}
