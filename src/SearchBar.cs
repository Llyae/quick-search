using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickSearch
{
    // The pop-up bar: a tag showing which site Enter goes to, then the text box.
    class SearchBar : Form
    {
        public Config Config;
        readonly HintTextBox input = new HintTextBox();
        readonly SiteTag tag = new SiteTag();
        readonly float scale;
        bool dark;

        public SearchBar(Config config)
        {
            Config = config;
            Text = "Quick Search";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            scale = DeviceDpi / 96f;
            ClientSize = new Size(S(640), S(56));

            tag.Font = PickFont("Segoe UI Variable Small Semibold", "Segoe UI Semibold", 9.5f);
            input.Font = PickFont("Segoe UI Variable Text", "Segoe UI", 14f);
            input.BorderStyle = BorderStyle.None;
            Controls.Add(tag);
            Controls.Add(input);

            input.TextChanged += delegate { UpdateTag(); };
            input.KeyDown += OnInputKey;
            tag.Click += delegate { input.Focus(); };
            Deactivate += delegate { Hide(); };
        }

        public void Toggle()
        {
            if (Visible) Hide(); else ShowBar();
        }

        public void ShowBar()
        {
            ApplyTheme(); // picks up a light/dark switch made while the bar was hidden
            UpdateTag();
            SetPlaceholder();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 4);
            Show();
            Activate();
            input.Focus();
            input.SelectAll(); // last search stays, ready to repeat or type over
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) Go();
            else if (e.KeyCode == Keys.Escape) Hide();
            else if (e.KeyData == (Keys.Control | Keys.Back)) DeleteWordBack(); // the classic text box types a box character instead
            else return;
            e.SuppressKeyPress = true;
        }

        void Go()
        {
            var url = Config.BuildUrl(input.Text);
            if (url == null) return;
            try
            {
                // Shell-opening a link uses the default browser, which adds a tab to its open window if it has one.
                using (Process.Start(url)) { }
                Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open your browser.\n\n" + ex.Message, "Quick Search", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void DeleteWordBack()
        {
            if (input.SelectionLength > 0) { input.SelectedText = ""; return; }
            var t = input.Text;
            int end = input.SelectionStart, start = end;
            while (start > 0 && t[start - 1] == ' ') start--;
            while (start > 0 && t[start - 1] != ' ') start--;
            input.Text = t.Remove(start, end - start);
            input.SelectionStart = start;
        }

        void UpdateTag()
        {
            string query;
            var site = Config.SiteName(Config.PickAddress(input.Text, out query));
            tag.Text = site ?? "Fix the search address in Settings";
            tag.Fill = site == null ? Color.FromArgb(0xC4, 0x31, 0x4B) : dark ? Color.FromArgb(0xF2, 0xB3, 0x3D) : Color.FromArgb(0xF5, 0xC4, 0x43);
            tag.ForeColor = site == null ? Color.White : Color.FromArgb(0x2E, 0x21, 0x00);
            int pad = S(14), h = S(26);
            tag.Bounds = new Rectangle(pad, (ClientSize.Height - h) / 2, TextRenderer.MeasureText(tag.Text, tag.Font).Width + S(18), h);
            input.Location = new Point(tag.Right + S(12), (ClientSize.Height - input.Height) / 2);
            input.Width = ClientSize.Width - input.Left - pad;
            tag.Invalidate();
        }

        void SetPlaceholder()
        {
            var words = new List<string>(Config.Shortcuts.Keys);
            var cue = "Search the web";
            if (words.Count == 1) cue += ", or start with " + words[0];
            if (words.Count > 1) cue += ", or start with " + string.Join(", ", words.GetRange(0, words.Count - 1)) + " or " + words[words.Count - 1];
            input.Hint = cue;
            input.Invalidate();
        }

        void ApplyTheme()
        {
            var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            dark = light is int && (int)light == 0;
            BackColor = dark ? Color.FromArgb(0x23, 0x26, 0x2E) : Color.FromArgb(0xFC, 0xFC, 0xFD);
            input.BackColor = tag.BackColor = BackColor;
            input.ForeColor = dark ? Color.FromArgb(0xEE, 0xF0, 0xF4) : Color.FromArgb(0x1C, 0x1F, 0x26);
            input.HintColor = dark ? Color.FromArgb(0x9A, 0xA0, 0xAC) : Color.FromArgb(0x6B, 0x72, 0x80);
            int border = dark ? 0x4B3F3A : 0xE3DCD9; // 0x00BBGGRR: #3A3F4B dark, #D9DCE3 light
            Native.DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref border, 4);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2, enabled = 2;
            Native.DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4); // Windows 11 only
            Native.DwmSetWindowAttribute(Handle, 2 /* DWMWA_NCRENDERING_POLICY */, ref enabled, 4); // lets a borderless window get a shadow
            var margins = new Native.Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            Native.DwmExtendFrameIntoClientArea(Handle, ref margins);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: keeps it out of Alt+Tab
                return cp;
            }
        }

        int S(int px) { return (int)(px * scale + 0.5f); }

        static Font PickFont(string family, string fallback, float size)
        {
            var f = new Font(family, size);
            if (f.Name == family) return f;
            f.Dispose();
            return new Font(fallback, size);
        }
    }

    // Text box that draws its own hint: the built-in one is a fixed grey that's too faint on a dark bar.
    class HintTextBox : TextBox
    {
        public string Hint = "";
        public Color HintColor;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F /* WM_PAINT */ || Text.Length > 0) return;
            int left = (int)Native.SendMessage(Handle, 0x00D4 /* EM_GETMARGINS */, IntPtr.Zero, IntPtr.Zero) & 0xFFFF;
            using (var g = Graphics.FromHwnd(Handle))
                TextRenderer.DrawText(g, Hint, Font, new Point(left, 0), HintColor, BackColor, TextFormatFlags.NoPadding);
        }
    }

    // Pill-shaped label with the destination site name.
    class SiteTag : Control
    {
        public Color Fill;

        public SiteTag()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Height - 1;
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(Fill))
            {
                path.AddArc(0, 0, d, d, 90, 180);
                path.AddArc(Width - 1 - d, 0, d, d, 270, 180);
                path.CloseFigure();
                e.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
