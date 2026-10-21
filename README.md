# Quick Search

A search bar for Windows that's always one key combo away. Press **Alt+Shift+Space** anywhere, type a search or a link, and press **Enter**. It opens as a new tab in your default browser (or starts the browser if it isn't open).

![Bookmarks on top; typing "git" suggests the GitHub bookmark, then Google's suggestions](docs/bar.png)

Your bookmarks sit above the search box. As you type, matching bookmarks and Google's suggestions show underneath. Start with a shortcut word to search a different site. The tag on the left always shows where Enter will take you:

![Typing "yt lofi" switches the tag to youtube.com and suggests YouTube searches](docs/bar-shortcut.png)

## Using it

| Key | What it does |
| --- | --- |
| Alt+Shift+Space | Open or close the bar |
| Enter | Search, or open the link (or the highlighted suggestion) |
| Up / Down | Highlight a suggestion |
| Tab | Copy the highlighted (or top) suggestion into the box |
| Ctrl+1 to Ctrl+9 | Open bookmark 1 to 9 |
| Esc, or click anywhere else | Close the bar |

| Type | What Enter does |
| --- | --- |
| `cats` | Searches Google |
| `yt cats` | Searches YouTube (`w` searches Wikipedia, `gh` GitHub) |
| `yt` | Opens youtube.com |
| `reddit.com` or `github.com/Llyae` | Opens that page |

Common endings like `.com`, `.org`, `.io`, `.dev` and `.co.uk` count as links, so `node.js` is still searched. Start with `https://` to open any other address.

The magnifier icon by the clock opens the bar on a left-click. Right-click it for **Settings** and **Quit**.

## Settings

- **Hotkey:** click the box and press the combo you want. It needs Ctrl or Alt. If another app already uses that combo, Quick Search tells you.
- **Search engine:** Google, Bing, DuckDuckGo or YouTube, or any search address with `%s` where your search text goes.
- **Bookmarks:** one per line, a name, a space, then its link. For example, `Google Maps https://maps.google.com`. They show in this order above the search box.
- **Shortcut words:** one per line, a word, a space, then its search address. For example, `maps https://www.google.com/maps/search/%s`.
- **Show Google suggestions as I type:** on by default. With it on, Google sees what you type before you press Enter. Matching bookmarks are suggested either way.
- **Start with Windows:** starts Quick Search when you sign in, so it's always ready.

Settings are saved in `%APPDATA%\QuickSearch\settings.txt`. It's a plain text file, so you can edit it by hand too. Bookmark icons come from Google's site-icon service the first time each one is shown, and are kept in `%APPDATA%\QuickSearch\icons`.

## Install

1. Download `QuickSearch.exe` from the [latest release](../../releases/latest).
2. Put it in a folder you'll keep, for example `C:\Apps\QuickSearch`.
3. Run it. A tip near the clock tells you it's running.
4. Optional: right-click the tray icon, open **Settings**, and turn on **Start with Windows**.

The exe isn't code-signed, so the first time you run it Windows may show "Windows protected your PC". Click **More info**, then **Run anyway**.

Works on Windows 10 and 11. It uses .NET Framework 4.8, which already comes with Windows.

## Build it yourself

No SDK or packages needed. Windows ships with the C# compiler this uses.

```bat
build.bat
```

This runs the tests, then writes `bin\QuickSearch.exe`.

| File | What's in it |
| --- | --- |
| `src/Program.cs` | Startup, tray icon, global hotkey, Start with Windows |
| `src/SearchBar.cs` | The pop-up bar and its suggestions list |
| `src/Bookmarks.cs` | Bookmark buttons and their downloaded icons |
| `src/SettingsForm.cs` | The settings window |
| `src/Config.cs` | Settings file, search addresses, links, suggestions, hotkey parsing (no UI, so it's testable) |
| `tests/Tests.cs` | Checks for `Config.cs` |

## License

[MIT](LICENSE)
