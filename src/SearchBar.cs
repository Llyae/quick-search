using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickSearch
{
    // The pop-up bar: bookmarks row on top, then the search row (destination tag + text box), then suggestions.
    class SearchBar : Form
    {
        public Config Config;
        readonly HintTextBox input = new HintTextBox();
        readonly SiteTag tag = new SiteTag();
        readonly FlowLayoutPanel bookmarks = new FlowLayoutPanel();
        readonly ListBox list = new ListBox(); // suggestions; a real list box so screen readers can read the rows
        readonly ToolTip tip = new ToolTip();
        readonly Timer debounce = new Timer { Interval = 150 }; // ask Google once typing pauses, not on every key
        readonly Font bookmarkFont;
        readonly float scale;
        List<string> web = new List<string>(); // Google's latest suggestions
        WebClient fetch; // the request in flight, if any
        string fetched; // the text Google was last asked about
        Config built; // the settings the bookmarks row was last built from
        int bandTop, bandBottom; // the search row, between bookmarks and suggestions
        Color line, rowFill, muted;
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
            bookmarkFont = PickFont("Segoe UI Variable Small", "Segoe UI", 9.5f);
            bookmarks.AutoSize = true;
            bookmarks.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bookmarks.MaximumSize = new Size(ClientSize.Width, 0); // wraps onto more lines instead of growing wider
            bookmarks.Padding = new Padding(S(12), S(10), S(12), S(8));
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = S(36);
            list.BorderStyle = BorderStyle.None;
            list.IntegralHeight = false;
            list.TabStop = false;
            list.Visible = false;
            list.Font = PickFont("Segoe UI Variable Text", "Segoe UI", 10.5f);
            list.DrawItem += DrawRow;
            list.MouseMove += (s, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i; };
            list.MouseClick += delegate { Pick(); };
            list.SelectedIndexChanged += delegate { UpdateTag(); }; // the tag follows the highlighted row
            Controls.Add(bookmarks);
            Controls.Add(tag);
            Controls.Add(input);
            Controls.Add(list);

            input.TextChanged += delegate { ShowSuggestions(); debounce.Stop(); debounce.Start(); };
            debounce.Tick += delegate { debounce.Stop(); FetchSuggestions(); };
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
            if (built != Config) BuildBookmarks();
            ApplyTheme(); // picks up a light/dark switch made while the bar was hidden
            list.Items.Clear(); // suggestions come back once you type
            UpdateTag();
            SetPlaceholder();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 4);
            Show();
            Activate();
            input.Focus();
            input.SelectAll(); // last search stays, ready to repeat or type over
        }

        void BuildBookmarks()
        {
            built = Config;
            while (bookmarks.Controls.Count > 0) bookmarks.Controls[0].Dispose();
            for (int i = 0; i < Config.Bookmarks.Count; i++)
            {
                var link = Config.Bookmarks[i][1];
                var button = new BookmarkButton { Text = Config.Bookmarks[i][0], Font = bookmarkFont, AccessibleDescription = link };
                button.Margin = new Padding(S(2));
                button.Size = new Size(Math.Min(S(190), S(42) + TextRenderer.MeasureText(button.Text, bookmarkFont).Width), S(30));
                tip.SetToolTip(button, i < 9 ? link + "  (Ctrl+" + (i + 1) + ")" : link);
                button.Click += delegate { Open(link); };
                SiteIcons.Get(link, img => { button.Image = img; button.Invalidate(); });
                bookmarks.Controls.Add(button);
            }
            bookmarks.Visible = Config.Bookmarks.Count > 0;
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            int n = e.KeyCode - Keys.D1;
            if (e.KeyCode == Keys.Enter) Pick();
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int i = list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                if (i >= -1 && i < list.Items.Count) list.SelectedIndex = i; // -1 = back to what you typed
            }
            else if (e.KeyCode == Keys.Escape) Hide();
            else if (e.KeyData == (Keys.Control | Keys.Back)) DeleteWordBack(); // the classic text box types a box character instead
            else if (e.Modifiers == Keys.Control && n >= 0 && n < 9 && n < Config.Bookmarks.Count) Open(Config.Bookmarks[n][1]);
            else return;
            e.SuppressKeyPress = true;
        }

        // Single-line text boxes never see Tab (it moves focus), so it's caught here: Tab copies a suggestion into the box.
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) != Keys.Tab) return base.ProcessDialogKey(keyData);
            var row = (list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null)) as Suggestion;
            if (row != null)
            {
                input.Text = row.Text;
                input.SelectionStart = input.Text.Length;
            }
            return true; // focus stays in the box either way
        }

        // Enter or click: the highlighted suggestion if there is one, otherwise what's typed.
        void Pick()
        {
            var row = list.SelectedItem as Suggestion;
            Open(Config.BuildUrl(row != null ? row.Text : input.Text));
        }

        void ShowSuggestions()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var row in Config.Suggest(input.Text, Config.WebSuggestions ? web : new List<string>())) list.Items.Add(row);
            list.EndUpdate();
            UpdateTag();
        }

        void FetchSuggestions()
        {
            string query;
            Config.PickAddress(input.Text, out query);
            if (!Config.WebSuggestions || query.Length == 0 || query == fetched) return;
            fetched = query;
            if (fetch != null) fetch.CancelAsync();
            var client = fetch = new WebClient { Encoding = Encoding.UTF8 };
            client.DownloadStringCompleted += (s, e) => // raised back on the UI thread
            {
                client.Dispose();
                if (e.Cancelled || e.Error != null || client != fetch) return; // offline or superseded: keep what's shown
                fetch = null;
                web = Config.ParseSuggestions(e.Result);
                ShowSuggestions();
            };
            client.DownloadStringAsync(new Uri("https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&hl="
                + CultureInfo.CurrentUICulture.TwoLetterISOLanguageName + "&q=" + Uri.EscapeDataString(query)));
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var row = (Suggestion)list.Items[e.Index];
            var g = e.Graphics;
            var b = e.Bounds;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, b);
            if ((e.State & DrawItemState.Selected) != 0)
                using (var fill = new SolidBrush(rowFill))
                using (var path = Shapes.Round(new Rectangle(b.X, b.Y + S(2), b.Width - 1, b.Height - S(4) - 1), S(16)))
                    g.FillPath(fill, path);
            int icon = S(16);
            var box = new Rectangle(b.X + S(10), b.Y + (b.Height - icon) / 2, icon, icon);
            var img = row.Link != null ? SiteIcons.Cached(row.Link) : null;
            if (img != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, box);
            }
            else if (row.Link == null) // magnifier for searches
                using (var pen = new Pen(muted, S(2) * 0.8f))
                {
                    int d = icon * 5 / 8;
                    g.DrawEllipse(pen, box.X + 1, box.Y + 1, d, d);
                    g.DrawLine(pen, box.X + 1 + d * 0.85f, box.Y + 1 + d * 0.85f, box.Right - 1, box.Bottom - 1);
                }
            int x = box.Right + S(12);
            int detail = row.Detail == null ? 0 : TextRenderer.MeasureText(row.Detail, list.Font).Width;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, row.Label, list.Font, new Rectangle(x, b.Y, b.Right - x - detail - S(24), b.Height), input.ForeColor, flags | TextFormatFlags.EndEllipsis);
            if (detail > 0) TextRenderer.DrawText(g, row.Detail, list.Font, new Rectangle(b.Right - detail - S(12), b.Y, detail, b.Height), muted, flags);
        }

        void Open(string url)
        {
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
            var row = list.SelectedItem as Suggestion;
            var site = Config.Destination(row != null ? row.Text : input.Text);
            tag.Text = site ?? "Fix the search address in Settings";
            tag.Fill = site == null ? Color.FromArgb(0xC4, 0x31, 0x4B) : dark ? Color.FromArgb(0xF2, 0xB3, 0x3D) : Color.FromArgb(0xF5, 0xC4, 0x43);
            tag.ForeColor = site == null ? Color.White : Color.FromArgb(0x2E, 0x21, 0x00);
            int pad = S(14), h = S(26), band = S(56);
            bandTop = bookmarks.Visible ? bookmarks.Bottom : 0;
            tag.Bounds = new Rectangle(pad, bandTop + (band - h) / 2, TextRenderer.MeasureText(tag.Text, tag.Font).Width + S(18), h);
            input.Location = new Point(tag.Right + S(12), bandTop + (band - input.Height) / 2);
            input.Width = ClientSize.Width - input.Left - pad;
            bandBottom = bandTop + band;
            list.Visible = list.Items.Count > 0;
            list.Bounds = new Rectangle(S(6), bandBottom + S(5), ClientSize.Width - S(12), list.Items.Count * list.ItemHeight);
            ClientSize = new Size(ClientSize.Width, list.Visible ? list.Bottom + S(5) : bandBottom);
            tag.Invalidate();
            Invalidate(); // divider lines
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(line))
            {
                if (bandTop > 0) e.Graphics.DrawLine(pen, 0, bandTop, ClientSize.Width, bandTop);
                if (list.Visible) e.Graphics.DrawLine(pen, 0, bandBottom, ClientSize.Width, bandBottom);
            }
        }

        void SetPlaceholder()
        {
            var words = new List<string>(Config.Shortcuts.Keys);
            var last = words.Count > 0 ? words[words.Count - 1] : "";
            var list = words.Count > 1 ? string.Join(", ", words.GetRange(0, words.Count - 1)) + " or " + last : last;
            input.Hint = words.Count == 0 ? "Search or type a link" : "Search, type a link, or start with " + list;
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
            line = dark ? Color.FromArgb(0x34, 0x38, 0x42) : Color.FromArgb(0xE6, 0xE8, 0xEC);
            rowFill = dark ? Color.FromArgb(0x30, 0x35, 0x40) : Color.FromArgb(0xEE, 0xF0, 0xF3);
            muted = input.HintColor;
            bookmarks.BackColor = list.BackColor = BackColor;
            foreach (BookmarkButton b in bookmarks.Controls)
            {
                b.ForeColor = input.ForeColor;
                b.Fill = dark ? Color.FromArgb(0x2C, 0x30, 0x39) : Color.FromArgb(0xF0, 0xF1, 0xF4);
                b.HoverFill = dark ? Color.FromArgb(0x38, 0x3D, 0x48) : Color.FromArgb(0xE3, 0xE5, 0xEA);
            }
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

    static class Shapes
    {
        // Rectangle with rounded corners; d is the corner diameter (the rectangle's height gives a pill).
        public static GraphicsPath Round(Rectangle r, int d)
        {
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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
            using (var path = Shapes.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height - 1))
            using (var brush = new SolidBrush(Fill))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
