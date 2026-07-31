using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Per-client resource settings. Opened from the "Tune" link on a client row.
    class ClientTuneDialog : Form
    {
        readonly int pid;
        readonly int clientIndex;
        readonly PerformanceManager perf;

        ComboBox cmbPriority, cmbCores;
        CheckBox chkEco;
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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(372, 262);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);

            Build(label);
        }

        void Build(string label)
        {
            Card card = new Card();
            card.Location = new Point(12, 12);
            card.Size = new Size(348, 196);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle(label.ToUpperInvariant()));

            lblRam = Ui.MutedLabel("", 20, 38, 8.25f);
            card.Controls.Add(lblRam);

            card.Controls.Add(Ui.MutedLabel("CPU priority", 20, 68, 9.75f));
            cmbPriority = Ui.DarkCombo(160, 65, 168);
            Ui.FillPriorityCombo(cmbPriority);
            cmbPriority.SelectedIndex = Result.Priority;
            card.Controls.Add(cmbPriority);

            card.Controls.Add(Ui.MutedLabel("Cores", 20, 102, 9.75f));
            cmbCores = Ui.DarkCombo(160, 99, 168);
            Ui.FillCoreCombo(cmbCores);
            Ui.SelectCoreCount(cmbCores, Result.Cores);
            card.Controls.Add(cmbCores);

            chkEco = Ui.DarkCheck("Efficiency mode (EcoQoS)", 20, 136, 9f);
            chkEco.ForeColor = Theme.Text;
            chkEco.Checked = Result.Eco;
            card.Controls.Add(chkEco);

            btnTrim = Ui.AccentButton("Trim memory now", 190, 158, 138, 28);
            btnTrim.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnTrim.Click += delegate { TrimNow(); };
            card.Controls.Add(btnTrim);

            card.Controls.Add(Ui.MutedLabel("Parks the client on efficiency cores.", 20, 160, 8.25f));

            Button ok = Ui.AccentButton("Apply", 244, 220, 116, 30);
            ok.Click += delegate { Commit(false); };
            Controls.Add(ok);

            Button reset = Ui.AccentButton("Use default", 124, 220, 112, 30);
            reset.BackColor = Theme.Card;
            reset.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            reset.FlatAppearance.MouseOverBackColor = Theme.Inset;
            reset.Click += delegate { Commit(true); };
            Controls.Add(reset);

            Button cancel = Ui.AccentButton("Cancel", 12, 220, 100, 30);
            cancel.BackColor = Theme.Card;
            cancel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            cancel.FlatAppearance.MouseOverBackColor = Theme.Inset;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            RefreshRam();
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
