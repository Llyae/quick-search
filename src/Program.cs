using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickSearch
{
    static class Program
    {
        // Sent by a second copy of the app so the running one shows its bar instead.
        public static readonly int ShowBarMessage = Native.RegisterWindowMessage("QuickSearch.ShowBar");

        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, "QuickSearch.SingleInstance", out first))
            {
                if (!first)
                {
                    Native.AllowSetForegroundWindow(-1); // let the running copy bring its bar to the front
                    Native.PostMessage((IntPtr)0xFFFF /* all top-level windows */, ShowBarMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
        }

        public static Icon AppIcon(Size size)
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("icon.ico")) return new Icon(s, size);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // True only if the startup entry points at this exe, so a moved exe shows as off until saved again.
        public static bool StartsWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && (k.GetValue("QuickSearch") as string) == "\"" + Application.ExecutablePath + "\"";
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("QuickSearch", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("QuickSearch", false);
                }
            }
        }
    }

    // Runs in the background: tray icon, global hotkey, and the bar.
    class App : ApplicationContext
    {
        const int HotkeyId = 1, WM_HOTKEY = 0x0312;
        Config config;
        readonly SearchBar bar;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Listener listener;

        public App()
        {
            bool firstRun = !File.Exists(Config.DefaultPath);
            string loadError = null;
            try { config = Config.Load(Config.DefaultPath); }
            catch (Exception ex) { config = new Config(); loadError = ex.Message; } // file is left alone until Settings saves

            bar = new SearchBar(config);
            listener = new Listener(this);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Search", null, delegate { bar.ShowBar(); }).Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add("Quit", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.Icon = Program.AppIcon(SystemInformation.SmallIconSize);
            tray.Text = "Quick Search";
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) bar.ShowBar(); };
            tray.Visible = true;

            if (loadError != null)
                tray.ShowBalloonTip(8000, "Couldn't read your settings", "Using the defaults for now. " + loadError, ToolTipIcon.Warning);
            else if (!RegisterHotkey(config.Hotkey))
                tray.ShowBalloonTip(8000, "Quick Search can't use " + config.Hotkey, "Another app already uses it. Pick a different hotkey in Settings.", ToolTipIcon.Warning);
            else if (firstRun)
            {
                tray.ShowBalloonTip(8000, "Quick Search is running", "Press " + config.Hotkey + " to search the web.", ToolTipIcon.None);
                try { config.Save(Config.DefaultPath); } catch (Exception) { } // only stops this tip from repeating
            }
        }

        bool RegisterHotkey(string text)
        {
            Native.UnregisterHotKey(listener.Handle, HotkeyId);
            uint mods;
            Keys key;
            return Config.TryParseHotkey(text, out mods, out key)
                && Native.RegisterHotKey(listener.Handle, HotkeyId, mods | 0x4000 /* MOD_NOREPEAT */, (uint)key);
        }

        void Quit()
        {
            tray.Visible = false;
            listener.DestroyHandle();
            ExitThread();
        }

        // Hidden window that receives the hotkey and the "show the bar" message from a second copy.
        class Listener : NativeWindow
        {
            readonly App app;

            public Listener(App app)
            {
                this.app = app;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY) app.bar.Toggle();
                else if (m.Msg == Program.ShowBarMessage) app.bar.ShowBar();
                base.WndProc(ref m);
            }
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    }
}
