using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
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
        public bool WebSuggestions = true;
        public SortedDictionary<string, string> Shortcuts = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "gh", "https://github.com/search?q=%s" },
            { "w", "https://en.wikipedia.org/w/index.php?search=%s" },
            { "yt", "https://www.youtube.com/results?search_query=%s" },
        };

        // The row above the bar, in the user's order: { name, link }.
        public List<string[]> Bookmarks = new List<string[]>
        {
            new[] { "YouTube", "https://www.youtube.com/" },
            new[] { "Gmail", "https://mail.google.com/" },
            new[] { "GitHub", "https://github.com/" },
            new[] { "Reddit", "https://www.reddit.com/" },
        };

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            // The file's lists replace the defaults, so deleted shortcuts and bookmarks stay deleted.
            c.Shortcuts.Clear();
            c.Bookmarks.Clear();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq < 1) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                if (key == "hotkey") c.Hotkey = val;
                else if (key == "engine") c.Engine = val;
                else if (key == "suggestions") c.WebSuggestions = val != "off";
                else if (key.StartsWith("shortcut.")) c.Shortcuts[key.Substring(9)] = val;
                else if (key == "bookmark") { var b = ParseBookmark(val); if (b != null) c.Bookmarks.Add(b); }
            }
            return c;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Quick Search settings. %s marks where your search text goes.");
            sb.AppendLine("hotkey=" + Hotkey);
            sb.AppendLine("engine=" + Engine);
            sb.AppendLine("suggestions=" + (WebSuggestions ? "on" : "off"));
            foreach (var s in Shortcuts) sb.AppendLine("shortcut." + s.Key + "=" + s.Value);
            foreach (var b in Bookmarks) sb.AppendLine("bookmark=" + b[0] + " " + b[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }

        // Which search address the typed text goes to, and the text left after any shortcut word.
        public string PickAddress(string text, out string query)
        {
            query = (text ?? "").Trim();
            int space = query.IndexOf(' ');
            string shortcut;
            if (Shortcuts.TryGetValue(space > 0 ? query.Substring(0, space) : query, out shortcut))
            {
                query = space > 0 ? query.Substring(space + 1).Trim() : "";
                return shortcut;
            }
            return Engine;
        }

        // The address to open for what was typed: a typed link, a shortcut word's home page, or a search.
        // Null if there's nothing to do or the address isn't a web link.
        public string BuildUrl(string text)
        {
            var link = AsLink(text);
            if (link != null) return link;
            string query;
            var template = PickAddress(text, out query);
            if (!IsWebAddress(template)) return null;
            if (query.Length == 0) return (text ?? "").Trim().Length == 0 ? null : HomePage(template); // a shortcut word on its own
            var q = Uri.EscapeDataString(query);
            return template.Contains("%s") ? template.Replace("%s", q) : template + q;
        }

        // Site name for the tag on the bar: where Enter will go.
        public string Destination(string text)
        {
            string query;
            return SiteName(AsLink(text) ?? PickAddress(text, out query));
        }

        // Rows for the dropdown: bookmarks whose name or site matches, then Google's suggestions, up to 6 in all.
        // shortcut: web suggestions must start with the typed text, which drops replies to older keystrokes
        // (and Google's rare spelling fixes); track which text each reply was for if those are missed.
        public List<Suggestion> Suggest(string text, List<string> web)
        {
            var rows = new List<Suggestion>();
            var t = (text ?? "").Trim();
            if (t.Length == 0) return rows;
            foreach (var b in Bookmarks)
            {
                var site = SiteName(b[1]) ?? "";
                if (rows.Count < 3 && (b[0].IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || site.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    rows.Add(new Suggestion { Label = b[0], Detail = site, Text = b[1], Link = b[1] });
            }
            string query;
            PickAddress(t, out query);
            var prefix = t.Substring(0, t.Length - query.Length); // "yt " when a shortcut word is in front
            foreach (var s in web)
                if (rows.Count < 6 && s.StartsWith(query, StringComparison.OrdinalIgnoreCase) && !s.Equals(query, StringComparison.OrdinalIgnoreCase))
                    rows.Add(new Suggestion { Label = s, Text = prefix + s });
            return rows;
        }

        // Google's reply: ["cats", ["cats movie", "cats musical", ...]] -> the suggestions. Empty for anything else.
        public static List<string> ParseSuggestions(string json)
        {
            var list = new List<string>();
            try
            {
                var reply = new JavaScriptSerializer().DeserializeObject(json) as object[];
                var items = reply != null && reply.Length > 1 ? reply[1] as object[] : null;
                if (items != null) foreach (var s in items) if (s is string) list.Add((string)s);
            }
            catch (Exception) { } // it came off the internet: a bad reply just means no suggestions
            return list;
        }

        // shortcut: only these endings count as typed addresses (so "readme.md" is searched); switch to the full IANA list if people hit misses.
        static readonly string[] LinkEndings =
        {
            "com", "org", "net", "edu", "gov", "io", "dev", "app", "ai", "co", "me", "tv", "gg", "xyz", "info",
            "uk", "us", "ca", "au", "nz", "ie", "de", "fr", "nl", "eu", "jp", "ly", "so", "sh", "to", "fm", "page", "site", "blog",
        };

        // "reddit.com", "github.com/x", "www.x.y" or a full http(s) link -> the address to open. Null means search it instead.
        public static string AsLink(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0 || t.Contains(" ")) return null;
            bool full = IsWebAddress(t);
            if (!full && t.Contains("@")) return null; // an email address, not a site
            Uri uri;
            if (!Uri.TryCreate(full ? t : "https://" + t, UriKind.Absolute, out uri)) return null;
            int dot = uri.Host.LastIndexOf('.');
            bool known = dot > 0 && Array.IndexOf(LinkEndings, uri.Host.Substring(dot + 1)) >= 0;
            return full || known || uri.Host.StartsWith("www.") ? uri.AbsoluteUri : null;
        }

        static string HomePage(string address)
        {
            return new Uri(address.Replace("%s", "")).GetLeftPart(UriPartial.Authority) + "/";
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

        // Settings box text: one "word address" per line.
        public static string ShortcutsText(SortedDictionary<string, string> shortcuts)
        {
            var sb = new StringBuilder();
            foreach (var s in shortcuts) sb.Append(s.Key + " " + s.Value + "\r\n");
            return sb.ToString();
        }

        // Reads the settings box back. Returns a problem to show the user, or null when every line was fine.
        public static string ParseShortcuts(string text, SortedDictionary<string, string> into)
        {
            into.Clear();
            var lines = (text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var parts = line.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0].Contains("=") || !IsWebAddress(parts[1].Trim()))
                    return "Shortcut line " + (i + 1) + " needs a word, a space, then an address starting with https://";
                into[parts[0]] = parts[1].Trim();
            }
            return null;
        }

        // "Google Maps https://maps.google.com" -> { name, link }. The link is the last word, so names can have spaces.
        // No name means the site name is used. Null if the link isn't a web address.
        static string[] ParseBookmark(string line)
        {
            line = line.Trim();
            int space = line.LastIndexOfAny(new[] { ' ', '\t' });
            var link = AsLink(line.Substring(space + 1));
            if (link == null) return null;
            var name = space > 0 ? line.Substring(0, space).Trim() : "";
            return new[] { name.Length > 0 ? name : SiteName(link), link };
        }

        public static string BookmarksText(List<string[]> bookmarks)
        {
            var sb = new StringBuilder();
            foreach (var b in bookmarks) sb.Append(b[0] + " " + b[1] + "\r\n");
            return sb.ToString();
        }

        // Reads the settings box back. Returns a problem to show the user, or null when every line was fine.
        public static string ParseBookmarks(string text, List<string[]> into)
        {
            into.Clear();
            var lines = (text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                var b = ParseBookmark(lines[i]);
                if (b == null) return "Bookmark line " + (i + 1) + " needs a name, a space, then a link like https://example.com";
                into.Add(b);
            }
            return null;
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

    // One dropdown row. Text is what goes in the box (and through BuildUrl); Link is set for bookmarks, for their icon.
    public class Suggestion
    {
        public string Label, Detail, Text, Link;

        public override string ToString() { return Detail == null ? Label : Label + ", " + Detail; } // what screen readers say
    }
}
