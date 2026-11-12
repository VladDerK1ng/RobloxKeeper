using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The rows that choose a picture for a step to go by: one a watcher
    // already looks for, or a new one cut out of a client. Shared by the step
    // window and the autoclicker's.
    class PictureRows
    {
        public const int HEIGHT = 128;
        public const int HEIGHT_SCREEN = 66;

        // Where what Look and the rest say goes, when there is no room for a line of its own.
        public Action<string> Said;

        public static int HeightFor(bool onScreen) { return onScreen ? HEIGHT_SCREEN : HEIGHT; }

        public string Picture, Mask, Box;
        public double Tolerance;

        readonly Form owner;
        readonly WatchKit kit;
        readonly bool onScreen;     // for a macro that plays on the screen, not on a Roblox window
        readonly List<string> created = new List<string>();
        readonly Label lblName, lblSays;
        readonly ThemedNumeric numAlike;
        readonly LinkLabel lnkPaint, lnkLook;
        readonly ThemedPicker cmbBox;
        readonly List<string> boxes = new List<string>();

        public PictureRows(Form owner, WatchKit kit, Control card, int y, int fieldX, int inner,
                           string picture, string mask, double tolerance, string box)
            : this(owner, kit, card, y, fieldX, inner, picture, mask, tolerance, box, false) { }

        // onScreen: the picture is looked for on the whole screen, so there
        // is no box to look in and nothing to look on a Roblox window for.
        public PictureRows(Form owner, WatchKit kit, Control card, int y, int fieldX, int inner,
                           string picture, string mask, double tolerance, string box, bool onScreen)
        {
            this.owner = owner;
            this.kit = kit;
            this.onScreen = onScreen;
            Picture = picture; Mask = mask; Box = box;
            Tolerance = tolerance <= 0 ? MacroStep.DEFAULT_TOLERANCE : tolerance;

            card.Controls.Add(Ui.RowLabel("Picture", Ui.PAD, y, 28, 90, 9f, Theme.Muted));
            Button choose = WatchUi.Secondary("Choose", fieldX, y, 90, 28);
            choose.Enabled = kit != null;
            choose.Click += delegate { Choose(choose); };
            card.Controls.Add(choose);
            lblName = Ui.RowLabel("", fieldX + 98, y, 28, inner - fieldX - 98 + Ui.PAD, 9f, Theme.Text);
            card.Controls.Add(lblName);

            card.Controls.Add(Ui.RowLabel("How close", Ui.PAD, y + 34, 26, 90, 9f, Theme.Muted));
            numAlike = Ui.DarkNumeric(fieldX, y + 34, 64, 50, 100, (int)Math.Round(Tolerance * 100));
            card.Controls.Add(numAlike);
            card.Controls.Add(Ui.RowLabel("%", fieldX + 70, y + 34, 26, 20, 9f, Theme.Muted));
            lnkPaint = Ui.RowLink("Erase background", fieldX + 98, y + 34, 26, 8.25f);
            lnkPaint.Click += delegate { Paint(); };
            card.Controls.Add(lnkPaint);

            // Where to look: the whole window, or a box already drawn for a
            // game in the Watchers window - a quarter of the window is found
            // in a quarter of the time.
            if (kit != null)
                foreach (WatchRegion r in kit.Store.Regions)
                    if (r != null && !string.IsNullOrEmpty(r.Name) && !boxes.Contains(r.Name)) boxes.Add(r.Name);
            if (!string.IsNullOrEmpty(Box) && !boxes.Contains(Box)) boxes.Add(Box);
            Label lookIn = Ui.RowLabel("Look in", Ui.PAD, y + 66, 26, 90, 9f, Theme.Muted);
            lookIn.Visible = !onScreen;
            card.Controls.Add(lookIn);
            cmbBox = Ui.DarkCombo(fieldX, y + 66, Math.Min(230, inner - fieldX + Ui.PAD));
            cmbBox.Visible = !onScreen;
            cmbBox.Items.Add("The whole window");
            foreach (string name in boxes) cmbBox.Items.Add("The box \"" + name + "\"");
            cmbBox.SelectedIndex = string.IsNullOrEmpty(Box) ? 0 : boxes.IndexOf(Box) + 1;
            cmbBox.SelectedIndexChanged += delegate
            {
                Box = cmbBox.SelectedIndex <= 0 ? null : boxes[cmbBox.SelectedIndex - 1];
            };
            card.Controls.Add(cmbBox);

            int sayY = onScreen ? y + 66 : y + 98;
            lnkLook = Ui.RowLink("Look for it now", Ui.PAD, sayY, 24, 8.25f);
            lnkLook.Click += delegate { Look(); };
            card.Controls.Add(lnkLook);
            lblSays = Ui.RowLabel("", onScreen ? Ui.PAD : Ui.PAD + 100, sayY, 24, onScreen ? inner : inner - 100, 8.25f, Theme.Muted);
            card.Controls.Add(lblSays);

            lblSays.Visible = !onScreen;
            Show();
        }

        public double ChosenTolerance { get { return numAlike.Value / 100.0; } }
        public string Says { get { return lblSays.Text; } }

        void Show()
        {
            bool has = !string.IsNullOrEmpty(Picture);
            lblName.Text = has ? MacroStep.PictureLabel(Picture) + (Mask != null ? " - background erased" : "")
                               : "None yet";
            lblName.ForeColor = has ? Theme.Text : Theme.Muted;
            lnkPaint.Visible = has && kit != null;
            lnkLook.Visible = has && kit != null && !onScreen;
        }

        // The box to look in, shown in the list when there is one.
        public void ChooseBox(string name)
        {
            Box = string.IsNullOrEmpty(name) ? null : name;
            if (Box != null && !boxes.Contains(Box))
            {
                // One the list has not got yet is added rather than dropped.
                boxes.Add(Box);
                cmbBox.Items.Add("The box \"" + Box + "\"");
            }
            cmbBox.SelectedIndex = Box == null ? 0 : boxes.IndexOf(Box) + 1;
        }

        public int BoxChoices { get { return cmbBox.Items.Count; } }

        void Say(string text, bool warn)
        {
            if (onScreen && Said != null) { Said(text); return; }
            lblSays.Text = text;
            lblSays.ForeColor = warn ? Theme.Amber : Theme.Muted;
        }

        // The pictures there already are, then every client one could be cut
        // out of.
        void Choose(Control under)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            int n = 0;
            foreach (Watcher w in kit.Store.Watchers)
            {
                if (w == null || w.Kind != WatchKind.ImageFound || string.IsNullOrEmpty(w.TemplateFile)) continue;
                Watcher from = w;
                menu.Items.Add("The picture " + w.Name + " looks for", null, delegate { Take(from); });
                n++;
            }
            if (n > 0) menu.Items.Add(new ToolStripSeparator());

            WatchedClient[] clients = kit.RunningClients();
            foreach (WatchedClient c in clients)
            {
                WatchedClient client = c;
                menu.Items.Add("Take a new picture from " + WatchUi.ClientName(c), null, delegate { Cut(client); });
            }
            if (clients.Length == 0)
            {
                ToolStripItem none = menu.Items.Add("Open a Roblox window to take a picture from");
                none.Enabled = false;
            }
            menu.Show(under, new Point(0, under.Height));
        }

        // A watcher's picture, with its painted-out copy, how alike it has to
        // be and the box it is looked for in.
        void Take(Watcher w)
        {
            Picture = w.TemplateFile;
            Mask = w.MaskFile;
            ChooseBox(w.RegionName);
            numAlike.Value = (int)Math.Round(w.Tolerance * 100);
            Say("", false);
            Show();
        }

        void Cut(WatchedClient c)
        {
            Pixels still = WindowCapture.Grab(WindowCapture.GameWindow(c.Pid));
            if (still == null)
            {
                Say("Couldn't see that Roblox window. Is it minimized?", true);
                return;
            }
            using (RegionPickerForm f = new RegionPickerForm(still, WatchUi.ClientName(c),
                       RegionPickMode.Picture, c.PlaceId, RegionScaling.Uniform, null))
            {
                if (f.ShowDialog(owner) != DialogResult.OK || f.PickedPicture == null) return;
                // Steps are read by their picture's name - "Click Buy button
                // where it is" - so it gets one.
                string called;
                using (PictureNameDialog n = new PictureNameDialog())
                {
                    if (n.ShowDialog(owner) != DialogResult.OK) return;
                    called = n.Chosen;
                }
                string file = WatcherForm.TemplateName(called, DateTime.Now);
                try { WatcherForm.SavePicture(f.PickedPicture, file); }
                catch (Exception ex) { Say("Couldn't save the picture: " + ex.Message, true); return; }
                created.Add(file);
                Picture = file;
                Mask = null;
                ChooseBox(null);
                Say("Picture saved. Erase its background if it sits over scenery.", false);
                Show();
            }
        }

        void Paint()
        {
            Pixels t = WatchEngine.LoadPicture(Picture);
            if (t == null) { Say("Couldn't open the picture. Choose it again.", true); return; }
            Pixels existing = Mask == null ? null : WatchEngine.LoadPicture(Mask);
            using (MaskPainterForm f = new MaskPainterForm(t, existing))
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return;
                string file = WatcherForm.MaskNameFor(Picture, DateTime.Now);
                try { WatcherForm.SavePicture(f.Mask, file); }
                catch (Exception ex) { Say("Couldn't save it: " + ex.Message, true); return; }
                created.Add(file);
                Mask = file;
                Show();
            }
        }

        // On every client that is open: where it is, or how near it came.
        void Look()
        {
            Pixels needle = WatchEngine.LoadPicture(Picture);
            if (needle == null) { Say("Couldn't open the picture. Choose it again.", true); return; }
            bool[] mask = null;
            if (Mask != null)
            {
                Pixels m = WatchEngine.LoadPicture(Mask);
                if (m != null && m.Width == needle.Width && m.Height == needle.Height) mask = WatchEngine.MaskFrom(m);
            }

            WatchedClient[] clients = kit.RunningClients();
            if (clients.Length == 0) { Say("Open a Roblox window to look in.", true); return; }
            List<string> said = new List<string>();
            bool any = false;
            foreach (WatchedClient c in clients)
            {
                Pixels shot = WindowCapture.Grab(WindowCapture.GameWindow(c.Pid));
                if (shot == null) { said.Add(c.Label + ": can't see this window"); continue; }
                double fx, fy, score;
                bool found = PictureSearch.Find(shot, needle, mask, ChosenTolerance, Rectangle.Empty, 0, 0, out fx, out fy, out score);
                any |= found;
                said.Add(c.Label + ": " + (found ? "found, " : "not there, ") + Percent(score) + " match");
            }
            Say(string.Join(" · ", said.ToArray()), !any);
        }

        static string Percent(double f)
        {
            return ((int)Math.Round(f * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // What is chosen now has been saved somewhere that names it: it is
        // no longer this window's to tidy away, whatever is chosen after.
        public void Keep()
        {
            created.Remove(Picture);
            created.Remove(Mask);
        }

        // Files made here that the saved step does not use are removed, so
        // cancelling leaves nothing behind. A watcher's own are never touched.
        public void Tidy(bool saved)
        {
            foreach (string file in created)
            {
                if (saved && (file == Picture || file == Mask)) continue;
                try { File.Delete(WatchStore.TemplatePath(file)); } catch { }
            }
            created.Clear();
        }
    }

    // What to call a picture that has just been cut out.
    class PictureNameDialog : Form
    {
        const int W = 380;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        readonly TextBox box;
        readonly Label says;

        public string Chosen;

        public PictureNameDialog()
        {
            int footY = TOP + 100 + 12;
            WatchUi.Frame(this, "Name the picture", W, footY + 32 + 12);
            Card card = WatchUi.CardAt(this, "NAME", 12, TOP, W - 24, 100);
            box = WatchUi.Input(Ui.PAD, 40, W - 24 - Ui.PAD * 2);
            box.MaxLength = 40;
            card.Controls.Add(box);
            says = Ui.MutedLabel("Give it a short name, like \"Buy button\" or \"Egg\".", Ui.PAD, 68, 8.25f);
            says.MaximumSize = new Size(W - 24 - Ui.PAD * 2, 0);
            card.Controls.Add(says);

            Button save = WatchUi.Primary("Save", W - 12 - 100, footY, 100, 32);
            save.Click += delegate { if (Accept(box.Text)) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 208, footY, 100, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Shown += delegate { box.Focus(); };
        }

        // A name with a letter or number in it: it becomes part of a file's.
        public bool Accept(string typed)
        {
            string name = (typed ?? "").Trim();
            bool any = false;
            foreach (char c in name) if (c < 128 && char.IsLetterOrDigit(c)) any = true;
            if (!any)
            {
                says.Text = "Use at least one letter or number in the name.";
                says.ForeColor = Theme.Amber;
                return false;
            }
            Chosen = name;
            return true;
        }
    }

    // One step, typed in.
    class MacroStepDialog : Form
    {
        const int W = 440;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int FIELD_X = 120;
        const int INNER = W - 24 - Ui.PAD * 2;

        readonly MacroStep step;
        readonly WatchKit kit;
        readonly bool anywhere;
        byte vk;
        Button btnKey;
        TextBox secondsBox, textBox;
        ThemedNumeric numAcross, numDown, numAcross2, numDown2, numCount;
        ThemedCheckBox chkRight, chkChat, chkPointer, chkStop, chkGone;
        ThemedPicker cmbMode, cmbWay;
        PictureRows picture;
        Label lblProblem;
        int footY;

        public MacroStep Result { get; private set; }

        public MacroStepDialog(MacroStep s) : this(s, null) { }

        public MacroStepDialog(MacroStep s, WatchKit kit) : this(s, kit, false) { }

        // anywhere: the macro plays on the screen, where there is no Roblox
        // window to pick a spot on.
        public MacroStepDialog(MacroStep s, WatchKit kit, bool anywhere)
        {
            step = s.Copy();
            this.kit = kit;
            this.anywhere = anywhere;
            vk = s.Vk;
            int cardH = HeightFor(s.Kind);
            footY = TOP + cardH + 12;
            WatchUi.Frame(this, Title(s.Kind), W, footY + 32 + 12);
            Build(cardH);
            FormClosed += delegate { if (picture != null) picture.Tidy(Result != null); };
        }

        static string Title(MacroStepKind k)
        {
            switch (k)
            {
                case MacroStepKind.Click: return "Click";
                case MacroStepKind.Wait: return "Wait";
                case MacroStepKind.Type: return "Type";
                case MacroStepKind.KeyDown: return "Hold a key";
                case MacroStepKind.KeyUp: return "Release a key";
                case MacroStepKind.ClickPicture: return "Click a picture";
                case MacroStepKind.WaitPicture: return "Wait for a picture";
                case MacroStepKind.Move: return "Move the pointer";
                case MacroStepKind.Drag: return "Drag";
                case MacroStepKind.Scroll: return "Scroll";
                case MacroStepKind.Repeat: return "Repeat";
                default: return "Press a key";
            }
        }

        int HeightFor(MacroStepKind k)
        {
            switch (k)
            {
                case MacroStepKind.Click: return 232;
                case MacroStepKind.ClickPicture: return 40 + PictureRows.HeightFor(anywhere) + 32 * 3 + 76;
                case MacroStepKind.WaitPicture: return 40 + PictureRows.HeightFor(anywhere) + 32 * 3 + 60;
                case MacroStepKind.Drag: return 284;
                case MacroStepKind.Scroll: return 250;
                case MacroStepKind.Repeat: return 40 + 64 + PictureRows.HeightFor(anywhere) + 84;
                default: return 176;
            }
        }

        // A spot can be picked on a picture of a Roblox window; for the
        // screen there is none, and the numbers are all there is.
        bool CanPick { get { return kit != null && !anywhere; } }

        // ---------- layout ----------

        void Build(int cardH)
        {
            Card card = WatchUi.CardAt(this, "STEP", 12, TOP, W - 24, cardH);
            switch (step.Kind)
            {
                case MacroStepKind.Click: BuildClick(card); break;
                case MacroStepKind.Wait:
                    secondsBox = Seconds(card, "Seconds", 40, step.Ms);
                    Hint(card, "Gives the game time to catch up, like a shop opening.", 76);
                    break;
                case MacroStepKind.Type: BuildType(card); break;
                case MacroStepKind.ClickPicture: BuildClickPicture(card); break;
                case MacroStepKind.WaitPicture: BuildWaitPicture(card); break;
                case MacroStepKind.Move:
                    Place(card, 40, step.X, step.Y, out numAcross, out numDown);
                    Hint(card, "Moves the pointer there without clicking. Use it to open something that shows "
                             + "when you point at it.", 110);
                    break;
                case MacroStepKind.Drag: BuildDrag(card); break;
                case MacroStepKind.Scroll: BuildScroll(card); break;
                case MacroStepKind.Repeat: BuildRepeat(card); break;
                default: BuildKey(card); break;
            }

            lblProblem = Ui.RowLabel("", 16, footY, 32, W - 16 - 208, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);
            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 90 - 8 - 90, footY, 90, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            Button save = WatchUi.Primary("Save", W - 12 - 90, footY, 90, 32);
            save.Click += delegate { if (Accept()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
        }

        void BuildClick(Card card)
        {
            Place(card, 40, step.X, step.Y, out numAcross, out numDown);
            chkRight = Ui.DarkCheck("Right mouse button", Ui.PAD, 104, 9f);
            chkRight.Checked = step.RightButton;
            card.Controls.Add(chkRight);
            chkPointer = Ui.DarkCheck("Click wherever my pointer is", Ui.PAD, 130, 9f);
            chkPointer.AutoSize = true;
            chkPointer.Checked = step.AtPointer;
            card.Controls.Add(chkPointer);
            Hint(card, anywhere ? "Across and down say where on your screen to click. "
                                  + "Or press Record and click it yourself."
                                : "Press \"Pick it on the game\" and click the spot. "
                                  + "Or press Record and click in the game yourself.", 160);
        }

        void BuildType(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Text", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
            textBox = WatchUi.Input(FIELD_X, 42, INNER - FIELD_X + Ui.PAD);
            textBox.Text = step.Text ?? "";
            card.Controls.Add(textBox);
            chkChat = Ui.DarkCheck("Send it in chat", Ui.PAD, 76, 9f);
            chkChat.AutoSize = true;
            chkChat.Checked = step.InChat;
            card.Controls.Add(chkChat);
            Hint(card, "Ticked, it presses / to open the chat, types this, then presses Enter. "
                     + "Unticked, it types into a box that's already open.", 104);
        }

        void BuildKey(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Key", Ui.PAD, 40, 26, 80, 9f, Theme.Muted));
            btnKey = WatchUi.Secondary(vk == 0 ? "Choose a key" : MacroKeys.Name(vk), FIELD_X, 40, 140, 28);
            btnKey.Click += delegate { ChooseKey(); };
            card.Controls.Add(btnKey);
            if (step.Kind != MacroStepKind.Key) return;
            secondsBox = Seconds(card, "Hold for", 76, step.HoldMs);
            Hint(card, "0.05 is a quick tap. Hold longer to walk or charge something up.", 110);
        }

        void BuildClickPicture(Card card)
        {
            picture = new PictureRows(this, kit, card, 40, FIELD_X, INNER, step.Picture, step.Mask, step.Tolerance, step.Box, anywhere);
            picture.Said = SaidInFooter;
            int y = 40 + PictureRows.HeightFor(anywhere);
            secondsBox = Seconds(card, "Wait up to", y, step.TimeoutMs);
            chkRight = Ui.DarkCheck("Right mouse button", Ui.PAD, y + 34, 9f);
            chkRight.Checked = step.RightButton;
            card.Controls.Add(chkRight);
            chkStop = Ui.DarkCheck("Stop the macro if it isn't there in time", Ui.PAD, y + 60, 9f);
            chkStop.AutoSize = true;
            chkStop.Checked = step.StopIfMissing;
            card.Controls.Add(chkStop);
            Hint(card, (anywhere ? "Looks at your screen when this step plays and clicks the picture wherever it is. "
                                 : "Looks at the Roblox window when this step plays and clicks the picture wherever it is. ")
                     + "0 seconds looks once. Unticked, a missing picture is skipped.", y + 90);
        }

        void BuildWaitPicture(Card card)
        {
            picture = new PictureRows(this, kit, card, 40, FIELD_X, INNER, step.Picture, step.Mask, step.Tolerance, step.Box, anywhere);
            picture.Said = SaidInFooter;
            int y = 40 + PictureRows.HeightFor(anywhere);
            chkGone = Ui.DarkCheck("Wait for it to disappear instead of appear", Ui.PAD, y, 9f);
            chkGone.AutoSize = true;
            chkGone.Checked = step.UntilGone;
            card.Controls.Add(chkGone);
            secondsBox = Seconds(card, "For up to", y + 30, step.TimeoutMs);
            chkStop = Ui.DarkCheck("Stop the macro if that never happens", Ui.PAD, y + 64, 9f);
            chkStop.AutoSize = true;
            chkStop.Checked = step.StopIfMissing;
            card.Controls.Add(chkStop);
            Hint(card, "Does nothing itself. The macro waits here, for a shop to open or a loading screen to go away.", y + 94);
        }

        void BuildDrag(Card card)
        {
            card.Controls.Add(Ui.RowLabel("From", Ui.PAD, 40, 26, 90, 9f, Theme.Muted));
            Pair(card, 40, step.X, step.Y, out numAcross, out numDown);
            card.Controls.Add(Ui.RowLabel("To", Ui.PAD, 72, 26, 90, 9f, Theme.Muted));
            Pair(card, 72, step.X2, step.Y2, out numAcross2, out numDown2);
            secondsBox = Seconds(card, "Over", 104, step.Ms);
            if (CanPick)
            {
                // A row of its own: beside "seconds" it ran past the edge of the card.
                Button pick = WatchUi.Primary("Pick both on the game", FIELD_X, 136, 170, 28);
                pick.Click += delegate
                {
                    PlacePick.Ask(this, kit, pick, true,
                                  new double[] { numAcross.Value / 100.0, numDown.Value / 100.0,
                                                 numAcross2.Value / 100.0, numDown2.Value / 100.0 },
                                  delegate(double[] at)
                                  {
                                      Picked(numAcross, numDown, at[0], at[1], 0);
                                      Picked(numAcross2, numDown2, at[2], at[3], 1);
                                  },
                                  delegate(string why) { lblProblem.Text = why; });
                };
                card.Controls.Add(pick);
            }
            chkRight = Ui.DarkCheck("Right mouse button (turns the camera)", Ui.PAD, CanPick ? 172 : 138, 9f);
            chkRight.AutoSize = true;
            chkRight.Checked = step.RightButton;
            card.Controls.Add(chkRight);
            Hint(card, CanPick ? "Press \"Pick both on the game\", then click where the drag starts and where it ends. "
                               + "It presses at the start, moves to the end, then lets go."
                               : "It presses at the first spot, moves to the second, then lets go."
                               + (anywhere ? " Spots are across and down your screen." : ""),
                 CanPick ? 204 : 170);
        }

        void BuildScroll(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Which way", Ui.PAD, 40, 26, 90, 9f, Theme.Muted));
            cmbWay = Ui.DarkCombo(FIELD_X, 40, 110);
            cmbWay.Items.Add("Up");
            cmbWay.Items.Add("Down");
            cmbWay.SelectedIndex = step.Notches < 0 ? 1 : 0;
            card.Controls.Add(cmbWay);
            card.Controls.Add(Ui.RowLabel("Wheel clicks", Ui.PAD, 72, 26, 90, 9f, Theme.Muted));
            numCount = Ui.DarkNumeric(FIELD_X, 72, 64, 1, 50, Math.Max(1, Math.Abs(step.Notches)));
            card.Controls.Add(numCount);
            Place(card, 104, step.X, step.Y, out numAcross, out numDown);
            chkPointer = Ui.DarkCheck("Wherever the pointer already is", Ui.PAD, 170, 9f);
            chkPointer.AutoSize = true;
            chkPointer.Checked = step.AtPointer;
            card.Controls.Add(chkPointer);
            Hint(card, "Scrolling up zooms the camera in. In a list, it moves up.", 200);
        }

        void BuildRepeat(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Keep going", Ui.PAD, 40, 26, 90, 9f, Theme.Muted));
            cmbMode = Ui.DarkCombo(FIELD_X, 40, 230);
            cmbMode.Items.Add("A number of times");
            cmbMode.Items.Add("Until I stop it");
            cmbMode.Items.Add("Until a picture appears");
            cmbMode.Items.Add("Until a picture disappears");
            cmbMode.SelectedIndex = (int)step.Mode;
            card.Controls.Add(cmbMode);

            card.Controls.Add(Ui.RowLabel("Times", Ui.PAD, 72, 26, 90, 9f, Theme.Muted));
            numCount = Ui.DarkNumeric(FIELD_X, 72, 72, 2, 9999, Math.Max(2, step.Count));
            card.Controls.Add(numCount);

            picture = new PictureRows(this, kit, card, 104, FIELD_X, INNER, step.Picture, step.Mask, step.Tolerance, step.Box, anywhere);
            picture.Said = SaidInFooter;
            Hint(card, "Repeats the steps down to \"End of repeat\". Times is for a set number; "
                     + "the picture is for the last two. \"Until I stop it\" ends with the stop key, "
                     + "or when you switch to another window.", 104 + PictureRows.HeightFor(anywhere));
        }

        // "Across" and "Down" on a row each.
        void Place(Card card, int y, double x, double yy, out ThemedNumeric across, out ThemedNumeric down)
        {
            card.Controls.Add(Ui.RowLabel("Across", Ui.PAD, y, 26, 80, 9f, Theme.Muted));
            across = Ui.DarkNumeric(FIELD_X, y, 64, 0, 100, (int)Math.Round(x * 100));
            card.Controls.Add(across);
            card.Controls.Add(Ui.RowLabel("%", FIELD_X + 70, y, 26, 30, 9f, Theme.Muted));
            card.Controls.Add(Ui.RowLabel("Down", Ui.PAD, y + 32, 26, 80, 9f, Theme.Muted));
            down = Ui.DarkNumeric(FIELD_X, y + 32, 64, 0, 100, (int)Math.Round(yy * 100));
            card.Controls.Add(down);
            card.Controls.Add(Ui.RowLabel("%", FIELD_X + 70, y + 32, 26, 30, 9f, Theme.Muted));

            // Nobody knows how many percent across a button is. Clicking it
            // on a picture of the game fills both boxes in.
            if (!CanPick) return;
            ThemedNumeric a = across, d = down;
            Button pick = WatchUi.Primary("Pick it on the game", FIELD_X + 110, y, 170, 28);
            pick.Click += delegate
            {
                PlacePick.Ask(this, kit, pick, false, new double[] { a.Value / 100.0, d.Value / 100.0 },
                              delegate(double[] at) { Picked(a, d, at[0], at[1], 0); },
                              delegate(string why) { lblProblem.Text = why; });
            };
            card.Controls.Add(pick);
            card.Controls.Add(Ui.RowLabel("or type it", FIELD_X + 114, y + 32, 26, 120, 8.25f, Theme.Muted));
        }

        // A picked place is kept exactly; the boxes show it to the nearest
        // percent, and a box changed afterwards takes over again.
        readonly double[] exact = new double[4];
        readonly bool[] isExact = new bool[2];

        void Picked(ThemedNumeric across, ThemedNumeric down, double fx, double fy, int which)
        {
            across.Value = (int)Math.Round(fx * 100);
            down.Value = (int)Math.Round(fy * 100);
            exact[which * 2] = fx;
            exact[which * 2 + 1] = fy;
            isExact[which] = true;
            lblProblem.Text = "";
        }

        double Exact(ThemedNumeric box, int index)
        {
            double typed = box.Value / 100.0;
            return isExact[index / 2] && (int)Math.Round(exact[index] * 100) == box.Value ? exact[index] : typed;
        }

        // For tests: as if the place had been clicked on the picture.
        public void PickPlace(double fx, double fy) { Picked(numAcross, numDown, fx, fy, 0); }

        // Across and down side by side, for a row that has both ends to fit.
        void Pair(Card card, int y, double x, double yy, out ThemedNumeric across, out ThemedNumeric down)
        {
            across = Ui.DarkNumeric(FIELD_X, y, 64, 0, 100, (int)Math.Round(x * 100));
            card.Controls.Add(across);
            card.Controls.Add(Ui.RowLabel("% across", FIELD_X + 70, y, 26, 70, 9f, Theme.Muted));
            down = Ui.DarkNumeric(FIELD_X + 144, y, 64, 0, 100, (int)Math.Round(yy * 100));
            card.Controls.Add(down);
            card.Controls.Add(Ui.RowLabel("% down", FIELD_X + 214, y, 26, 70, 9f, Theme.Muted));
        }

        TextBox Seconds(Card card, string label, int y, int ms)
        {
            card.Controls.Add(Ui.RowLabel(label, Ui.PAD, y, 26, 90, 9f, Theme.Muted));
            TextBox box = WatchUi.Input(FIELD_X, y + 2, 80);
            box.Text = MacroStep.Seconds(ms).TrimEnd('s');
            card.Controls.Add(box);
            card.Controls.Add(Ui.RowLabel("seconds", FIELD_X + 88, y, 26, 80, 9f, Theme.Muted));
            return box;
        }

        void SaidInFooter(string text) { lblProblem.Text = text; }

        void Hint(Card card, string text, int y)
        {
            Label l = Ui.MutedLabel(text, Ui.PAD, y, 8.25f);
            l.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(l);
        }

        void ChooseKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;
                vk = d.Captured;
                btnKey.Text = MacroKeys.Name(vk);
            }
        }

        // ---------- saving ----------

        // A wait for a picture may be nothing at all - look once - which a
        // plain wait may not.
        static string ParseTimeout(string text, out int ms)
        {
            ms = 0;
            string t = (text ?? "").Trim().ToLowerInvariant().TrimEnd('s').Trim().Replace(',', '.');
            double v;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v == 0) return null;
            return MacroForm.ParseSeconds(text, out ms);
        }

        void TakePicture(MacroStep s)
        {
            s.Picture = picture.Picture;
            s.Mask = picture.Mask;
            s.Box = picture.Box;
            s.Tolerance = picture.ChosenTolerance;
        }

        bool Accept()
        {
            MacroStep s = step.Copy();
            string problem = null;
            int ms;
            switch (s.Kind)
            {
                case MacroStepKind.Click:
                    s.X = Exact(numAcross, 0);
                    s.Y = Exact(numDown, 1);
                    s.RightButton = chkRight.Checked;
                    s.AtPointer = chkPointer.Checked;
                    break;
                case MacroStepKind.Wait:
                    problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                    s.Ms = ms;
                    break;
                case MacroStepKind.Type:
                    s.Text = textBox.Text;
                    s.InChat = chkChat.Checked;
                    if (s.Text.Length == 0) problem = "Type something.";
                    break;
                case MacroStepKind.ClickPicture:
                    TakePicture(s);
                    s.RightButton = chkRight.Checked;
                    s.StopIfMissing = chkStop.Checked;
                    problem = ParseTimeout(secondsBox.Text, out ms);
                    s.TimeoutMs = ms;
                    if (problem == null && string.IsNullOrEmpty(s.Picture)) problem = "Choose the picture to click.";
                    break;
                case MacroStepKind.WaitPicture:
                    TakePicture(s);
                    s.UntilGone = chkGone.Checked;
                    s.StopIfMissing = chkStop.Checked;
                    problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                    s.TimeoutMs = ms;
                    if (problem == null && string.IsNullOrEmpty(s.Picture)) problem = "Choose the picture to wait for.";
                    break;
                case MacroStepKind.Move:
                    s.X = Exact(numAcross, 0);
                    s.Y = Exact(numDown, 1);
                    break;
                case MacroStepKind.Drag:
                    s.X = Exact(numAcross, 0);
                    s.Y = Exact(numDown, 1);
                    s.X2 = Exact(numAcross2, 2);
                    s.Y2 = Exact(numDown2, 3);
                    s.RightButton = chkRight.Checked;
                    problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                    s.Ms = ms;
                    if (problem == null && s.X == s.X2 && s.Y == s.Y2) problem = "It starts and ends in the same spot. Change one.";
                    break;
                case MacroStepKind.Scroll:
                    s.Notches = cmbWay.SelectedIndex == 1 ? -numCount.Value : numCount.Value;
                    s.X = Exact(numAcross, 0);
                    s.Y = Exact(numDown, 1);
                    s.AtPointer = chkPointer.Checked;
                    break;
                case MacroStepKind.Repeat:
                    s.Mode = (RepeatMode)Math.Max(0, cmbMode.SelectedIndex);
                    s.Count = numCount.Value;
                    if (s.UsesPicture)
                    {
                        TakePicture(s);
                        if (string.IsNullOrEmpty(s.Picture)) problem = "Choose a picture to look for.";
                    }
                    break;
                default:
                    s.Vk = vk;
                    if (vk == 0) problem = "Choose a key.";
                    else if (s.Kind == MacroStepKind.Key)
                    {
                        problem = MacroForm.ParseSeconds(secondsBox.Text, out ms);
                        s.HoldMs = ms;
                    }
                    break;
            }
            if (problem != null) { lblProblem.Text = problem; return false; }
            Result = s;
            return true;
        }

        // For tests: what Save would make of the form as it stands.
        public bool TryAccept() { return Accept(); }
        public string ProblemText { get { return lblProblem.Text; } }
    }
}
