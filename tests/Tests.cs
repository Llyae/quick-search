using System;
using System.IO;
using System.Windows.Forms;

namespace QuickSearch
{
    // Plain asserts, no test framework: build.bat compiles and runs this, exit code 1 means a check failed.
    static class Tests
    {
        static int failures;

        static void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + name);
            if (!ok) failures++;
        }

        static void Same(string expected, string actual, string name)
        {
            Check(expected == actual, name + (expected == actual ? "" : "  (expected " + (expected ?? "null") + ", got " + (actual ?? "null") + ")"));
        }

        static int Main()
        {
            var c = new Config();
            Same("https://www.google.com/search?q=hello%20world", c.BuildUrl("  hello world "), "plain search");
            Same(null, c.BuildUrl("   "), "blank does nothing");
            Same("https://www.google.com/search?q=a%26b%3Dc%3F", c.BuildUrl("a&b=c?"), "special characters escaped");
            Same("https://www.google.com/search?q=%F0%9F%90%B1", c.BuildUrl("\U0001F431"), "emoji escaped");
            Same("https://www.youtube.com/results?search_query=cats%20dogs", c.BuildUrl("yt cats dogs"), "shortcut word");
            Same("https://www.youtube.com/results?search_query=cats", c.BuildUrl("YT   cats"), "shortcut ignores case and extra spaces");
            Same("https://www.google.com/search?q=yt", c.BuildUrl("yt"), "shortcut word alone is a normal search");
            Same("https://www.google.com/search?q=ytx%20cats", c.BuildUrl("ytx cats"), "only whole words are shortcuts");

            c.Engine = "https://example.com/find/";
            Same("https://example.com/find/x%20y", c.BuildUrl("x y"), "address without %s gets text appended");
            c.Engine = "C:\\Windows\\System32\\calc.exe %s";
            Same(null, c.BuildUrl("x"), "non-web address refused");
            c.Engine = "file:///C:/x?q=%s";
            Same(null, c.BuildUrl("x"), "file address refused");

            var plain = new Config();
            Same("google.com", Config.SiteName(plain.Engine), "site name drops www");
            Same("en.wikipedia.org", Config.SiteName(plain.Shortcuts["w"]), "site name keeps subdomain");
            string rest;
            Same("youtube.com", Config.SiteName(plain.PickAddress("yt lofi", out rest)), "tag follows shortcut word");
            Same("lofi", rest, "shortcut word removed from search text");
            Same(null, Config.SiteName("calc.exe %s"), "no site name for non-web address");
            Same(null, Config.SiteName("https://"), "no site name for broken address");

            uint mods; Keys key;
            Check(Config.TryParseHotkey("Alt+Shift+Space", out mods, out key) && mods == (Config.ModAlt | Config.ModShift) && key == Keys.Space, "parse Alt+Shift+Space");
            Check(Config.TryParseHotkey(" ctrl + alt + g ", out mods, out key) && mods == (Config.ModCtrl | Config.ModAlt) && key == Keys.G, "parse with spaces and lowercase");
            Check(Config.TryParseHotkey("Win+Shift+F5", out mods, out key) && mods == (Config.ModWin | Config.ModShift) && key == Keys.F5, "parse Win and F-keys");
            Check(!Config.TryParseHotkey("G", out mods, out key), "reject key without modifier");
            Check(!Config.TryParseHotkey("Ctrl+Shift", out mods, out key), "reject modifiers only");
            Check(!Config.TryParseHotkey("Shift+A", out mods, out key), "reject Shift alone (would break typing capitals)");
            Check(!Config.TryParseHotkey("Ctrl+ShiftKey", out mods, out key), "reject modifier as the key");
            Check(!Config.TryParseHotkey("Ctrl+Banana", out mods, out key), "reject unknown key");
            Check(!Config.TryParseHotkey("Hyper+G", out mods, out key), "reject unknown modifier");
            Check(!Config.TryParseHotkey("", out mods, out key), "reject empty");
            Check(!Config.TryParseHotkey(null, out mods, out key), "reject null");
            Same("Alt+Shift+Space", Config.FormatHotkey(Keys.Alt | Keys.Shift | Keys.Space), "format matches parse");
            Same("Ctrl+Alt+G", Config.FormatHotkey(Keys.G | Keys.Alt | Keys.Control), "format orders modifiers");

            var parsed = new System.Collections.Generic.SortedDictionary<string, string>();
            Same(null, Config.ParseShortcuts("\r\n  yt    https://www.youtube.com/results?search_query=%s  \r\n\r\nmaps https://www.google.com/maps/search/%s", parsed), "shortcut box parses with extra spaces and blank lines");
            Check(parsed.Count == 2 && parsed["maps"] == "https://www.google.com/maps/search/%s", "shortcut box values trimmed");
            Check(Config.ParseShortcuts("yt\r\nw https://x.test/%s", parsed) == "Shortcut line 1 needs a word, a space, then an address starting with https://", "word without address reported by line");
            Check(Config.ParseShortcuts("w https://x.test/%s\r\nbad calc.exe", parsed) != null, "non-web shortcut address rejected");
            Check(Config.ParseShortcuts("a=b https://x.test/%s", parsed) != null, "word with = rejected (would break the settings file)");
            Same(null, Config.ParseShortcuts(Config.ShortcutsText(plain.Shortcuts), parsed), "shortcut box round trip");
            Check(parsed.Count == 3 && parsed["gh"] == plain.Shortcuts["gh"], "shortcut box round trip keeps values");

            var path = Path.Combine(Path.GetTempPath(), "quicksearch-test-" + Guid.NewGuid() + ".txt");
            try
            {
                Check(Config.Load(path).Shortcuts.Count == 3, "missing file gives defaults");
                var a = new Config();
                a.Hotkey = "Ctrl+Alt+G";
                a.Engine = Config.Engines[2][1];
                a.Shortcuts.Remove("gh");
                a.Shortcuts["maps"] = "https://www.google.com/maps/search/%s";
                a.Save(path);
                var b = Config.Load(path);
                Same("Ctrl+Alt+G", b.Hotkey, "hotkey survives save");
                Same("DuckDuckGo", b.EngineName(), "engine survives save");
                Check(!b.Shortcuts.ContainsKey("gh"), "deleted shortcut stays deleted");
                Same("https://www.google.com/maps/search/%s", b.Shortcuts.ContainsKey("MAPS") ? b.Shortcuts["MAPS"] : null, "added shortcut survives save");
                File.WriteAllText(path, "# comment\n\njunk line\nhotkey = Ctrl+Shift+K \nshortcut.x=https://x.test/?q=%s\n");
                var d = Config.Load(path);
                Same("Ctrl+Shift+K", d.Hotkey, "hand-edited file read");
                Same("Google", d.EngineName(), "missing engine line keeps default");
                Check(d.Shortcuts.Count == 1, "hand-edited shortcut list replaces defaults");
            }
            finally { File.Delete(path); }

            Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
