using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Per-client resource settings. Opened from the "Tune" link on a client row.
    // Borderless like the main window - a system title bar here would be the one
    // piece of bright chrome left in the app.
    class ClientTuneDialog : Form
    {
        const int W = 372;
        const int H = 314;
        const int TITLEBAR_H = 40;

        readonly int pid;
        readonly int clientIndex;
        readonly PerformanceManager perf;

        ThemedPicker cmbPriority, cmbCores;
        ThemedCheckBox chkEco;
        Label lblRam;
        Button btnTrim;

        public ClientProfile Result { get; private set; }
        public bool ResetToDefault { get; private set; }

        public ClientTuneDialog(int pid, int clientIndex, string label, PerformanceManager perf)
        {
            this.pid = pid;
            this.clientIndex = clientIndex;
            this.perf = perf;
            Result = perf.ProfileFor(pid).Clone();

            Text = "Tune " + label;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, H);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);

            Build(label);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = Native.DWMWCP_ROUND;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(52, 52, 74), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        void Build(string label)
        {
            // --- Title bar ---
            Panel bar = new Panel();
            bar.Location = new Point(0, 0);
            bar.Size = new Size(W, TITLEBAR_H);
            bar.BackColor = Theme.Bg;
            Controls.Add(bar);

            Label title = Ui.RowLabel("Tune " + label, 16, 0, TITLEBAR_H, 240, 10f, Theme.Text);
            title.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            title.BackColor = Theme.Bg;
            bar.Controls.Add(title);

            WindowButton close = new WindowButton(true);
            close.Location = new Point(W - 44, 0);
            close.Size = new Size(44, 32);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.Add(close);

            bar.MouseDown += StartDrag;
            title.MouseDown += StartDrag;

            // --- Settings card ---
            Card card = new Card();
            card.Location = new Point(12, 46);
            card.Size = new Size(348, 212);
            Controls.Add(card);

            lblRam = Ui.MutedLabel("", Ui.PAD, 14, 8.25f);
            lblRam.MaximumSize = new Size(308, 0);
            card.Controls.Add(lblRam);

            const int row1 = 44, row2 = 78, rowEco = 112, ROW_H = 26;

            card.Controls.Add(Ui.RowLabel("CPU priority", Ui.PAD, row1, ROW_H, 120, 9.75f, Theme.Muted));
            cmbPriority = Ui.DarkCombo(150, row1, 178);
            Ui.FillPriorityCombo(cmbPriority);
            cmbPriority.SelectedIndex = Result.Priority;
            card.Controls.Add(cmbPriority);

            card.Controls.Add(Ui.RowLabel("Cores", Ui.PAD, row2, ROW_H, 120, 9.75f, Theme.Muted));
            cmbCores = Ui.DarkCombo(150, row2, 178);
            Ui.FillCoreCombo(cmbCores);
            Ui.SelectCoreCount(cmbCores, Result.Cores);
            card.Controls.Add(cmbCores);

            chkEco = Ui.DarkCheck("Efficiency mode (EcoQoS)", Ui.PAD, rowEco, 9f);
            chkEco.ForeColor = Theme.Text;
            chkEco.Checked = Result.Eco;
            Ui.CenterIn(chkEco, rowEco, ROW_H);
            card.Controls.Add(chkEco);

            // Indented to sit under the checkbox's text rather than its box.
            Label hint = Ui.MutedLabel("Parks the client on efficiency cores and caps its clock.", 45, 140, 8.25f);
            hint.MaximumSize = new Size(283, 0);
            card.Controls.Add(hint);

            btnTrim = Ui.AccentButton("Trim memory now", 188, 166, 140, 30);
            btnTrim.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnTrim.Click += delegate { TrimNow(); };
            card.Controls.Add(btnTrim);

            // --- Footer ---
            const int footer = 270;
            Button ok = Ui.AccentButton("Apply", 244, footer, 116, 32);
            ok.Click += delegate { Commit(false); };
            Controls.Add(ok);

            Button reset = Ui.AccentButton("Use default", 124, footer, 112, 32);
            reset.BackColor = Theme.Card;
            reset.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            reset.FlatAppearance.MouseOverBackColor = Theme.Inset;
            reset.Click += delegate { Commit(true); };
            Controls.Add(reset);

            Button cancel = Ui.AccentButton("Cancel", 12, footer, 100, 32);
            cancel.BackColor = Theme.Card;
            cancel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            cancel.FlatAppearance.MouseOverBackColor = Theme.Inset;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            RefreshRam();
        }

        void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
        }

        void RefreshRam()
        {
            long ws = 0;
            try { using (Process p = Process.GetProcessById(pid)) ws = p.WorkingSet64; }
            catch { }
            lblRam.Text = ws > 0
                ? "Using " + ClientTracker.FormatBytes(ws) + " of memory."
                : "This client is no longer running.";
        }

        void TrimNow()
        {
            long before = 0, after = 0;
            try { using (Process p = Process.GetProcessById(pid)) before = p.WorkingSet64; }
            catch { }

            if (!PerformanceManager.Trim(pid))
            {
                lblRam.Text = "Could not trim this client's memory.";
                return;
            }

            try { using (Process p = Process.GetProcessById(pid)) after = p.WorkingSet64; }
            catch { }

            long freed = before - after;
            lblRam.Text = freed > 0
                ? "Released " + ClientTracker.FormatBytes(freed) + " - now at " + ClientTracker.FormatBytes(after) + "."
                : "Now at " + ClientTracker.FormatBytes(after) + ".";
        }

        void Commit(bool useDefault)
        {
            ResetToDefault = useDefault;
            if (!useDefault)
            {
                Result.Priority = cmbPriority.SelectedIndex;
                Result.Cores = Ui.SelectedCoreCount(cmbCores);
                Result.Eco = chkEco.Checked;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
