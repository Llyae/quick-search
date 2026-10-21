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
            Same("https://www.youtube.com/", c.BuildUrl("yt"), "shortcut word alone opens the site");
            Same("https://en.wikipedia.org/", c.BuildUrl(" W "), "shortcut word alone ignores case and spaces");
            Same("https://www.google.com/search?q=ytx%20cats", c.BuildUrl("ytx cats"), "only whole words are shortcuts");

            Same("https://reddit.com/", c.BuildUrl("reddit.com"), "typed address opens directly");
            Same("https://github.com/Llyae", c.BuildUrl("github.com/Llyae"), "typed address with a path");
            Same("https://bbc.co.uk/news", c.BuildUrl("BBC.co.uk/news"), "two-part ending");
            Same("https://www.example.museum/", c.BuildUrl("www.example.museum"), "www. always counts as an address");
            Same("https://example.com/a?b=c", c.BuildUrl("https://example.com/a?b=c"), "full https link kept");
            Same("http://localhost:3000/", c.BuildUrl("http://localhost:3000"), "full http link kept");
            Same("https://www.google.com/search?q=node.js", c.BuildUrl("node.js"), "file-like name is searched");
            Same("https://www.google.com/search?q=readme.md", c.BuildUrl("readme.md"), "uncommon ending is searched");
            Same("https://www.google.com/search?q=me%40gmail.com", c.BuildUrl("me@gmail.com"), "email address is searched");
            Same("https://www.google.com/search?q=3.14", c.BuildUrl("3.14"), "number is searched");
            Same("https://www.google.com/search?q=reddit.com%20tips", c.BuildUrl("reddit.com tips"), "address plus words is searched");
            Same("https://en.wikipedia.org/w/index.php?search=github.com", c.BuildUrl("w github.com"), "shortcut word wins over address");
            Same("https://www.google.com/search?q=javascript%3Aalert(1)", c.BuildUrl("javascript:alert(1)"), "script link is searched, never opened");
            Same("reddit.com", c.Destination("reddit.com/r/windows"), "tag shows typed address site");
            Same("youtube.com", c.Destination("yt"), "tag shows shortcut site for word alone");
            Same("google.com", c.Destination("node.js"), "tag shows engine for searches");

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

            Check(plain.Bookmarks.Count == 4 && plain.Bookmarks[0][0] == "YouTube" && plain.Bookmarks[3][0] == "Reddit", "starter bookmarks in order");
            var marks = new System.Collections.Generic.List<string[]>();
            Same(null, Config.ParseBookmarks("Google Maps   https://maps.google.com\r\n\r\nReddit reddit.com\r\nhttps://news.ycombinator.com/news", marks), "bookmark box parses");
            Check(marks.Count == 3, "bookmark box skips blank lines");
            Check(marks.Count == 3 && marks[0][0] == "Google Maps" && marks[0][1] == "https://maps.google.com/", "bookmark name keeps its spaces");
            Check(marks.Count == 3 && marks[1][0] == "Reddit" && marks[1][1] == "https://reddit.com/", "bookmark link without https:// accepted");
            Check(marks.Count == 3 && marks[2][0] == "news.ycombinator.com", "bookmark without a name uses the site name");
            Same("Bookmark line 2 needs a name, a space, then a link like https://example.com", Config.ParseBookmarks("A https://a.com\r\nNotes readme.md", marks), "bad bookmark link reported by line");
            Check(Config.ParseBookmarks("Run calc.exe", marks) != null, "program path is not a bookmark");
            Same(null, Config.ParseBookmarks(Config.BookmarksText(plain.Bookmarks), marks), "bookmark box round trip");
            Check(marks.Count == 4 && marks[1][0] == "Gmail" && marks[1][1] == plain.Bookmarks[1][1], "bookmark box round trip keeps order and links");

            var path = Path.Combine(Path.GetTempPath(), "quicksearch-test-" + Guid.NewGuid() + ".txt");
            try
            {
                Check(Config.Load(path).Shortcuts.Count == 3, "missing file gives defaults");
                var a = new Config();
                a.Hotkey = "Ctrl+Alt+G";
                a.Engine = Config.Engines[2][1];
                a.Shortcuts.Remove("gh");
                a.Shortcuts["maps"] = "https://www.google.com/maps/search/%s";
                a.Bookmarks.RemoveAt(0);
                a.Bookmarks.Add(new[] { "Google Maps", "https://maps.google.com/" });
                a.Save(path);
                var b = Config.Load(path);
                Same("Ctrl+Alt+G", b.Hotkey, "hotkey survives save");
                Same("DuckDuckGo", b.EngineName(), "engine survives save");
                Check(!b.Shortcuts.ContainsKey("gh"), "deleted shortcut stays deleted");
                Same("https://www.google.com/maps/search/%s", b.Shortcuts.ContainsKey("MAPS") ? b.Shortcuts["MAPS"] : null, "added shortcut survives save");
                Check(b.Bookmarks.Count == 4 && b.Bookmarks[0][0] == "Gmail" && b.Bookmarks[3][0] == "Google Maps" && b.Bookmarks[3][1] == "https://maps.google.com/", "bookmarks survive save in order");
                File.WriteAllText(path, "# comment\n\njunk line\nhotkey = Ctrl+Shift+K \nshortcut.x=https://x.test/?q=%s\n");
                var d = Config.Load(path);
                Same("Ctrl+Shift+K", d.Hotkey, "hand-edited file read");
                Same("Google", d.EngineName(), "missing engine line keeps default");
                Check(d.Shortcuts.Count == 1, "hand-edited shortcut list replaces defaults");
                Check(d.Bookmarks.Count == 0, "file without bookmarks keeps the row empty");
            }
            finally { File.Delete(path); }

            Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
