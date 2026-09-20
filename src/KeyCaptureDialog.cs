using System;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // "Press the key you want to send." Captures one keypress and refuses the
    // ones that would cause trouble in a game - chat, menus, focus changes and
    // modifiers - rather than letting the user arm something that will fire
    // unattended every fifteen minutes.
    class KeyCaptureDialog : Form
    {
        readonly Label prompt;
        public byte Captured;

        public KeyCaptureDialog()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 120);
            BackColor = Theme.Card;
            KeyPreview = true;
            ShowInTaskbar = false;

            Label title = new Label();
            title.Text = "PRESS A KEY";
            title.AutoSize = false;
            title.Location = new Point(0, 22);
            title.Size = new Size(320, 16);
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            title.ForeColor = Theme.Muted;
            title.BackColor = Theme.Card;
            Controls.Add(title);

            prompt = new Label();
            prompt.Text = "Waiting...";
            prompt.AutoSize = false;
            prompt.Location = new Point(10, 44);
            prompt.Size = new Size(300, 44);
            prompt.TextAlign = ContentAlignment.MiddleCenter;
            prompt.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            prompt.ForeColor = Theme.Text;
            prompt.BackColor = Theme.Card;
            Controls.Add(prompt);

            Label hint = new Label();
            hint.Text = "Esc to cancel";
            hint.AutoSize = false;
            hint.Location = new Point(0, 92);
            hint.Size = new Size(320, 14);
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.Font = new Font("Segoe UI", 7.5f);
            hint.ForeColor = Theme.Muted;
            hint.BackColor = Theme.Card;
            Controls.Add(hint);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Accent, 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            byte vk = (byte)e.KeyCode;
            if (!NudgeKeys.IsSafe(vk))
            {
                // Named rather than just refused, so it is obvious this is a
                // rule and not a dead dialog.
                prompt.Text = KeyName(e.KeyCode) + " isn't safe";
                prompt.ForeColor = Theme.Amber;
                return;
            }

            Captured = vk;
            DialogResult = DialogResult.OK;
            Close();
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Return: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.ShiftKey: return "Shift";
                case Keys.ControlKey: return "Ctrl";
                case Keys.Menu: return "Alt";
                default: return k.ToString();
            }
        }
    }
}
