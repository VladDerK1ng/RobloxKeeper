using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    enum MatchMode { Any, All }

    // Whether a screenful of text counts as a hit.
    //
    // Deliberately forgiving to start with. OCR misreads small dim text, so a
    // strict rule quietly never fires - and a watcher that never fires looks
    // exactly like nothing having happened, which the user has no way to tell
    // apart. A loose rule that fires too often is at least visible, and the
    // two checkboxes here are how it gets tightened afterwards.
    class MatchRule
    {
        public string[] Words;
        public MatchMode Mode = MatchMode.Any;
        public bool IgnoreCase = true;
        public bool WholeWordsOnly;

        public bool Matches(string text)
        {
            return FirstHit(text) != null;
        }

        // The word that matched, for the message that gets sent. Null when
        // nothing did.
        public string FirstHit(string text)
        {
            if (text == null) return null;
            IList<string> words = Real();
            if (words.Count == 0) return null;

            if (Mode == MatchMode.All)
                return AllPresent(text, words) ? words[0] : null;

            foreach (string w in words)
                if (Contains(text, w)) return w;
            return null;
        }

        bool AllPresent(string text, IList<string> words)
        {
            foreach (string w in words)
                if (!Contains(text, w)) return false;
            return true;
        }

        // Blank entries are the user pressing Enter in the words box, not a
        // rule that matches everything.
        IList<string> Real()
        {
            List<string> list = new List<string>();
            if (Words == null) return list;
            foreach (string w in Words)
                if (!string.IsNullOrEmpty(w) && w.Trim().Length > 0) list.Add(w.Trim());
            return list;
        }

        bool Contains(string text, string word)
        {
            StringComparison how = IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!WholeWordsOnly) return text.IndexOf(word, how) >= 0;

            int from = 0;
            while (from <= text.Length - word.Length)
            {
                int at = text.IndexOf(word, from, how);
                if (at < 0) return false;
                if (IsEdge(text, at - 1) && IsEdge(text, at + word.Length)) return true;
                from = at + 1;
            }
            return false;
        }

        // Off the end of the string counts as an edge, so a word can be the
        // whole line.
        static bool IsEdge(string text, int index)
        {
            if (index < 0 || index >= text.Length) return true;
            return !char.IsLetterOrDigit(text[index]);
        }
    }
}
