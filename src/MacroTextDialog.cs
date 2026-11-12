using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A macro's steps as text, to write, change, copy out or paste in.
    //
    // The same steps as the list in the editor, the other way round: what is
    // typed here becomes steps there, and nothing is kept unless every line
    // is understood.
    class MacroTextDialog : Form
    {
        const int W = 620;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int TEXT_H = 400;
        // What is wrong with a line has a row to itself, the window's whole
        // width: it names the line, the word and what was expected.
        const int PROBLEM_Y = TOP + TEXT_H + 8;
        const int PROBLEM_H = 48;
        const int FOOT_Y = PROBLEM_Y + PROBLEM_H + 4;
        const int H = FOOT_Y + 32 + 12;

        readonly PictureShelf shelf;
        readonly Action<string> say;
        TextBox box;
        Label lblProblem;

        public List<MacroStep> Result { get; private set; }

        public MacroTextDialog(IList<MacroStep> steps, PictureShelf shelf, Action<string> say)
        {
            this.shelf = shelf ?? new PictureShelf();
            this.say = say;
            WatchUi.Frame(this, "Macro as text", W, H);
            Build();
            box.Text = MacroText.Write(steps, this.shelf);
            box.Select(box.TextLength, 0);
        }

        public string Written { get { return box.Text; } set { box.Text = value; } }
        public string ProblemText { get { return lblProblem.Text; } }
        public string Selected { get { return box.SelectedText; } }

        void Build()
        {
            Card card = WatchUi.CardAt(this, "ONE STEP ON EACH LINE", 12, TOP, W - 24, TEXT_H);
            int inner = W - 24 - Ui.PAD * 2;

            box = new TextBox();
            box.Multiline = true;
            box.AcceptsReturn = true;
            box.AcceptsTab = true;
            box.WordWrap = false;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Inset;
            box.ForeColor = Theme.Text;
            box.Font = Ui.F("Consolas", 10f);
            box.Location = new Point(Ui.PAD, 40);
            box.Size = new Size(inner, TEXT_H - 40 - 86);
            box.TextChanged += delegate { lblProblem.Text = ""; };
            card.Controls.Add(box);

            Label hint = Ui.MutedLabel(
                "press E   ·   hold W / release W   ·   wait 2   ·   click 50 50   ·   click picture \"Buy\" wait 3   ·   "
                + "say \"hello\"   ·   repeat forever ... end.   Places go from 0 to 100, across and down the window; "
                + "times are seconds. A picture is only named here, so a friend has to make their own.",
                Ui.PAD, TEXT_H - 74, 8.25f);
            hint.MaximumSize = new Size(inner - 130, 0);
            card.Controls.Add(hint);
            LinkLabel all = Ui.RowLink("All the words", W - 24 - Ui.PAD - 118, TEXT_H - 74, 20, 8.25f);
            all.Click += delegate { Guide.Open(say); };
            card.Controls.Add(all);

            lblProblem = Ui.MutedLabel("", 16, PROBLEM_Y, 8.25f);
            lblProblem.ForeColor = Theme.Amber;
            lblProblem.BackColor = Theme.Bg;
            lblProblem.AutoSize = false;
            lblProblem.MaximumSize = Size.Empty;
            lblProblem.Size = new Size(W - 32, PROBLEM_H);
            Controls.Add(lblProblem);

            Button copy = WatchUi.Secondary("Copy all", W - 12 - 116 - 8 - 96 - 8 - 96, FOOT_Y, 96, 32);
            copy.Click += delegate
            {
                try { if (box.TextLength > 0) Clipboard.SetText(box.Text); lblProblem.Text = ""; }
                catch { }
            };
            Controls.Add(copy);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 116 - 8 - 96, FOOT_Y, 96, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button use = WatchUi.Primary("Use these steps", W - 12 - 116, FOOT_Y, 116, 32);
            use.Click += delegate { if (TryUse()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(use);
            CancelButton = cancel;
        }

        // What the button does: the steps, or the line that is wrong.
        public bool TryUse()
        {
            List<MacroStep> steps = new List<MacroStep>();
            string problem = MacroText.Read(box.Text, shelf, steps);
            if (problem != null)
            {
                lblProblem.Text = problem;
                // The line it is about, picked out - in a long macro nobody
                // should have to count down to it.
                int line = MacroText.LineOf(problem) - 1;
                string[] lines = box.Lines;
                if (line >= 0 && line < lines.Length)
                {
                    string text = box.Text;
                    int start = 0;
                    for (int i = 0; i < line; i++) start = text.IndexOf('\n', start) + 1;
                    box.Select(start, lines[line].Length);
                    box.ScrollToCaret();
                    box.Focus();
                }
                return false;
            }
            Result = steps;
            return true;
        }
    }
}
