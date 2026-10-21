# Quick Search

A search bar for Windows that's always one key combo away. Press **Alt+Shift+Space** anywhere, type, and press **Enter**. The search opens as a new tab in your default browser (or starts the browser if it isn't open).

![The search bar, ready to search google.com](docs/bar.png)

Start your search with a shortcut word to search a different site. The tag on the left always shows where Enter will take you:

![Typing "yt lofi beats" switches the tag to youtube.com](docs/bar-shortcut.png)

## Using it

| Key | What it does |
| --- | --- |
| Alt+Shift+Space | Open or close the bar |
| Enter | Search |
| Esc, or click anywhere else | Close the bar |

Built-in shortcut words:

| Type | Searches |
| --- | --- |
| `yt cats` | YouTube |
| `w cats` | Wikipedia |
| `gh cats` | GitHub |

The magnifier icon by the clock opens the bar on a left-click. Right-click it for **Settings** and **Quit**.

## Settings

- **Hotkey:** click the box and press the combo you want. It needs Ctrl or Alt. If another app already uses that combo, Quick Search tells you.
- **Search engine:** Google, Bing, DuckDuckGo or YouTube, or any search address with `%s` where your search text goes.
- **Shortcut words:** one per line, a word, a space, then its search address. For example, `maps https://www.google.com/maps/search/%s`.
- **Start with Windows:** starts Quick Search when you sign in, so it's always ready.

Settings are saved in `%APPDATA%\QuickSearch\settings.txt`. It's a plain text file, so you can edit it by hand too.

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
| `src/SearchBar.cs` | The pop-up bar |
| `src/SettingsForm.cs` | The settings window |
| `src/Config.cs` | Settings file, search addresses, hotkey parsing (no UI, so it's testable) |
| `tests/Tests.cs` | Checks for `Config.cs` |

## License

[MIT](LICENSE)
