using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QuickSearch
{
    // Settings plus the logic that doesn't need a window (search addresses, hotkey text), so tests can cover it.
    public class Config
    {
        public static readonly string[][] Engines =
        {
            new[] { "Google", "https://www.google.com/search?q=%s" },
            new[] { "Bing", "https://www.bing.com/search?q=%s" },
            new[] { "DuckDuckGo", "https://duckduckgo.com/?q=%s" },
            new[] { "YouTube", "https://www.youtube.com/results?search_query=%s" },
        };

        public static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickSearch", "settings.txt");

        public string Hotkey = "Alt+Shift+Space";
        public string Engine = Engines[0][1];
        public SortedDictionary<string, string> Shortcuts = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "gh", "https://github.com/search?q=%s" },
            { "w", "https://en.wikipedia.org/w/index.php?search=%s" },
            { "yt", "https://www.youtube.com/results?search_query=%s" },
        };

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            c.Shortcuts.Clear(); // the file's list replaces the defaults, so deleted shortcuts stay deleted
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq < 1) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                if (key == "hotkey") c.Hotkey = val;
                else if (key == "engine") c.Engine = val;
                else if (key.StartsWith("shortcut.")) c.Shortcuts[key.Substring(9)] = val;
            }
            return c;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Quick Search settings. %s marks where your search text goes.");
            sb.AppendLine("hotkey=" + Hotkey);
            sb.AppendLine("engine=" + Engine);
            foreach (var s in Shortcuts) sb.AppendLine("shortcut." + s.Key + "=" + s.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }

        // Which search address the typed text goes to, and the text left after any shortcut word.
        public string PickAddress(string text, out string query)
        {
            query = (text ?? "").Trim();
            int space = query.IndexOf(' ');
            string shortcut;
            if (space > 0 && Shortcuts.TryGetValue(query.Substring(0, space), out shortcut))
            {
                query = query.Substring(space + 1).Trim();
                return shortcut;
            }
            return Engine;
        }

        // The address to open for what was typed, or null if there's nothing to search or the address isn't a web link.
        public string BuildUrl(string text)
        {
            string query;
            var template = PickAddress(text, out query);
            if (query.Length == 0 || !IsWebAddress(template)) return null;
            var q = Uri.EscapeDataString(query);
            return template.Contains("%s") ? template.Replace("%s", q) : template + q;
        }

        // Only web links, so a settings file can never make the bar launch a program.
        public static bool IsWebAddress(string t)
        {
            return t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        // "https://www.youtube.com/results?search_query=%s" -> "youtube.com", for the tag on the bar. Null if not a web address.
        public static string SiteName(string address)
        {
            Uri uri;
            if (!IsWebAddress(address) || !Uri.TryCreate(address.Replace("%s", ""), UriKind.Absolute, out uri)) return null;
            return uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
        }

        public string EngineName()
        {
            foreach (var e in Engines) if (e[1] == Engine) return e[0];
            return null;
        }

        public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, ModWin = 8;

        // "Ctrl+Alt+G" -> modifier flags + key. Needs Ctrl, Alt or Win so the hotkey can't swallow normal typing.
        public static bool TryParseHotkey(string text, out uint mods, out Keys key)
        {
            mods = 0;
            key = Keys.None;
            var parts = (text ?? "").Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModCtrl; break;
                    case "alt": mods |= ModAlt; break;
                    case "shift": mods |= ModShift; break;
                    case "win": mods |= ModWin; break;
                    default: return false;
                }
            }
            if (!Enum.TryParse(parts[parts.Length - 1].Trim(), true, out key)) return false;
            var code = key & Keys.KeyCode;
            bool isModifierKey = code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu
                || code == Keys.LWin || code == Keys.RWin;
            return (mods & ~ModShift) != 0 && key == code && code != Keys.None && !isModifierKey;
        }

        // Key press from a KeyDown event -> "Ctrl+Alt+Shift+Key" text that TryParseHotkey reads back.
        public static string FormatHotkey(Keys keyData)
        {
            var s = "";
            if ((keyData & Keys.Control) != 0) s += "Ctrl+";
            if ((keyData & Keys.Alt) != 0) s += "Alt+";
            if ((keyData & Keys.Shift) != 0) s += "Shift+";
            return s + (keyData & Keys.KeyCode);
        }
    }
}
