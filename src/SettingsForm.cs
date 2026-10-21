using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickSearch
{
    // Settings window. `apply` returns a problem to show, or null once the new settings are live and saved.
    class SettingsForm : Form
    {
        readonly Func<Config, bool, string> apply;
        readonly TextBox hotkey = new TextBox();
        readonly ComboBox engine = new ComboBox();
        readonly TextBox address = new TextBox();
        readonly TextBox shortcuts = new TextBox();
        readonly CheckBox startup = new CheckBox();
        readonly Label error = new Label();
        readonly TableLayoutPanel layout = new TableLayoutPanel();
        readonly float scale;
        bool syncing;

        public SettingsForm(Config config, Func<Config, bool, string> apply, Action<bool> pauseHotkey)
        {
            this.apply = apply;
            Text = "Quick Search settings";
            Icon = Program.AppIcon(SystemInformation.IconSize);
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            scale = DeviceDpi / 96f;
            Padding = new Padding(S(20), S(14), S(20), S(16));
            int width = S(460);

            hotkey.ReadOnly = true;
            hotkey.BackColor = SystemColors.Window;
            hotkey.ShortcutsEnabled = false;
            hotkey.Text = config.Hotkey;
            hotkey.KeyDown += OnHotkeyKey;
            // The app's own hotkey would fire instead of reaching this box, so it's off while you pick one.
            hotkey.Enter += delegate { pauseHotkey(true); };
            hotkey.Leave += delegate { pauseHotkey(false); };
            FormClosed += delegate { pauseHotkey(false); };

            engine.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var e in Config.Engines) engine.Items.Add(e[0]);
            engine.Items.Add("Custom");
            engine.SelectedIndexChanged += delegate { PickEngine(); };
            address.Text = config.Engine;
            address.TextChanged += delegate { SyncEngine(); };
            SyncEngine();

            shortcuts.Multiline = true;
            shortcuts.AcceptsReturn = true;
            shortcuts.WordWrap = false;
            shortcuts.ScrollBars = ScrollBars.Both;
            shortcuts.Height = S(110);
            shortcuts.Text = Config.ShortcutsText(config.Shortcuts);

            startup.Text = "Start with Windows";
            startup.AutoSize = true;
            startup.Checked = Program.StartsWithWindows;
            startup.Margin = new Padding(0, S(14), 0, 0);

            error.AutoSize = true;
            error.MaximumSize = new Size(width, 0);
            error.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
            error.Margin = new Padding(0, S(10), 0, 0);

            var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(S(80), 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(S(80), 0) };
            save.Click += delegate { Save(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = save;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(12), 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);

            layout.ColumnCount = 1;
            layout.AutoSize = true;
            layout.Dock = DockStyle.Fill;
            Add(Heading("Hotkey", 0), hotkey, Help("Click the box, then press the keys you want, like Ctrl+Alt+G."));
            Add(Heading("Search engine", S(14)), engine, address, Help("Use %s where your search text goes."));
            Add(Heading("Shortcut words", S(14)), shortcuts,
                Help("One per line: a word, a space, then its search address. Type the word before your search to use it, like \"yt cats\"."));
            Add(startup, error, buttons);
            foreach (Control c in new Control[] { hotkey, engine, address, shortcuts }) { c.Width = width; c.Margin = new Padding(0, S(4), 0, 0); }
            Controls.Add(layout);
        }

        void Add(params Control[] controls)
        {
            layout.Controls.AddRange(controls);
        }

        Label Heading(string text, int top)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, top, 0, 0) };
        }

        Label Help(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(S(460), 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, S(4), 0, 0) };
        }

        void OnHotkeyKey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return; // wait for the main key
            var text = Config.FormatHotkey(e.KeyData);
            uint mods;
            Keys key;
            if (Config.TryParseHotkey(text, out mods, out key)) { hotkey.Text = text; error.Text = ""; }
            else error.Text = "Hold Ctrl or Alt (Shift is optional), then press a letter, number or F-key.";
        }

        void PickEngine()
        {
            if (syncing) return;
            if (engine.SelectedIndex < Config.Engines.Length)
            {
                syncing = true;
                address.Text = Config.Engines[engine.SelectedIndex][1];
                syncing = false;
            }
            else
            {
                address.Focus();
                address.SelectAll();
            }
        }

        void SyncEngine()
        {
            if (syncing) return;
            syncing = true;
            int i = Array.FindIndex(Config.Engines, e => e[1] == address.Text.Trim());
            engine.SelectedIndex = i >= 0 ? i : Config.Engines.Length;
            syncing = false;
        }

        void Save()
        {
            var next = new Config();
            next.Hotkey = hotkey.Text;
            next.Engine = address.Text.Trim();
            var problem = Config.IsWebAddress(next.Engine) ? null : "The search address must start with https://";
            if (problem == null) problem = Config.ParseShortcuts(shortcuts.Text, next.Shortcuts);
            if (problem == null) problem = apply(next, startup.Checked);
            if (problem == null) Close();
            else error.Text = problem;
        }

        int S(int px) { return (int)(px * scale + 0.5f); }
    }
}
