# BetterWinTab
<div align="center">
  <img width="254" height="265" alt="image" src="https://github.com/user-attachments/assets/aea51c2e-53df-43b5-965b-28b34e43e75b" />
</div>

<!-- VIDEO -->
https://github.com/user-attachments/assets/43d3c613-c7c2-45a5-be38-eede70fd772a
<!-- END VIDEO -->

**The tool that should have shipped with your OS.**

BetterWinTab is a native Windows window navigator for people who keep many apps, browsers, terminals, editors, and work contexts open at the same time. Press `Ctrl + Tab`, search or browse your windows, and focus the one you need without touching the mouse.

Free. Open source. Native Windows. MIT licensed.

[Download Free for Windows 10/11](https://github.com/sgm1018/BetterWinTab/releases) · [View Source on GitHub](https://github.com/sgm1018/BetterWinTab)

---

## What Is BetterWinTab?

BetterWinTab, or **BWT**, is a keyboard-first replacement for the classic SO switching experience.

Instead of showing one flat, recency-ordered list like `Alt + Tab`, it gives you one compact surface for every open window, grouped by context, searchable in real time, and designed to disappear the moment you select what you need.

<img width="2547" height="1069" alt="image" src="https://github.com/user-attachments/assets/7fb79db3-d2de-4727-9e70-bd1fa9178efa" />


---

## The Problem

Modern desktops are not small anymore.

Developers, designers, students, and power users often run multiple VS Code windows, browser profiles, terminals, file explorers, notes, dashboards, and communication apps at the same time. All the SOs still treats them mostly as a flat list ordered by whatever you touched last.

That creates three problems:

| Problem | What Happens |
|---|---|
| No context | `code.exe`, `chrome.exe`, terminals, and explorers sit side by side with no meaning. |
| Volatile order | Every click reshuffles the switcher, so spatial memory breaks. |
| Slow discovery | Finding one window among dozens becomes scanning, guessing, and repeating. |

BetterWinTab solves this by making window navigation **context-aware, searchable, and keyboard-driven**.

---

## What Is It For?

Use BetterWinTab when you want to:

- Jump to any open window by typing part of its title, app, or process name.
- Group windows automatically into Smart Folders.
- Keep manual folders for personal workflows.
- Launch an app when the window you searched for is not open yet.
- Open a focused clipboard history faster than `Win + V`.
- Access common system actions like Recycle Bin without minimizing everything.
- Customize the look of your switcher with themes and custom colors.

<img width="2556" height="1076" alt="Captura de pantalla 2026-04-29 025517" src="https://github.com/user-attachments/assets/1887c384-25a4-4f7d-8a9b-05e0c816b69e" />


---

## Features

### Smart Folders

Smart Folders fill themselves automatically based on process names, window classes, or custom rules. Open a new VS Code, Chrome, Terminal, Explorer, Vivaldi, Brave, or Edge window and it appears in the right context immediately.

You can also create manual folders and drag windows into them when you want full control.

<img width="2556" height="1067" alt="Captura de pantalla 2026-04-29 025547" src="https://github.com/user-attachments/assets/57191f88-bfaf-4539-8971-cbb85b66d13c" />


<img width="620" height="878" alt="Captura de pantalla 2026-04-29 030010" src="https://github.com/user-attachments/assets/8422b455-2989-4c11-89d9-4d27d3b04a35" />



### Fuzzy Search

Search is always ready. Type part of a title, app name, subtitle, or process name and BetterWinTab filters every open window instantly.

The matcher is typo-tolerant and built for fast, imperfect typing under pressure.

### App Launcher

If no open window matches your search, BetterWinTab becomes an application launcher. Find installed programs and open them with one keystroke.

<img width="2547" height="1070" alt="Captura de pantalla 2026-04-29 025718" src="https://github.com/user-attachments/assets/9a55c4c3-7fc7-40d3-93ad-ed9c62058af2" />


### Keyboard-First Navigation

BetterWinTab is designed for flow:

| Shortcut | Action |
|---|---|
| `Ctrl + Tab` | Show or hide the overlay |
| `Tab` / `Shift + Tab` | Move between folders |
| `Arrow keys` | Move through windows |
| `Enter` | Focus the selected window or launch the selected app |
| `Esc` | Clear search or close the overlay |
| `F5` | Refresh windows |
| `Delete` | Close the selected window |
| `0-9`, `a-z`, `A-Z` | Start typing instantly to search across all open windows — no need to click the search box |
| `Tab` (while searching) | Toggle search mode between **All windows** and **Apps** (app launcher) |

The activation hotkey can be changed from **Settings -> General**.

### Clipboard History

BetterWinTab includes a focused clipboard panel for recent text items. It is built to open quickly, stay clean, and work from the keyboard.

<img width="2554" height="1070" alt="Captura de pantalla 2026-04-29 025645" src="https://github.com/user-attachments/assets/1697cb59-17ac-49ac-8701-df1290018fe2" />


### Recycle Bin Shortcut

Open the Recycle Bin directly from the overlay without minimizing windows or hunting through the desktop.

### Themes And Customization

Use built-in theme presets or create your own. BetterWinTab exposes semantic color tokens for accent, background, surfaces, cards, borders, text hierarchy, and danger states.

<img width="2545" height="1075" alt="Captura de pantalla 2026-04-29 030156" src="https://github.com/user-attachments/assets/bdc3487f-7a69-4f0a-aefe-e7b18385eb7b" />


### Native Windows Runtime

BetterWinTab is not an Electron app. It is built with **C#**, **.NET 8**, and **WinUI 3**, with Win32 interop for window enumeration, global hotkeys, live thumbnails, and native shell integration.

---

## Download

The easiest way to install BetterWinTab is to download the latest `.exe` from GitHub Releases:

**Download:** https://github.com/sgm1018/BetterWinTab/releases

Releases may include:

- A Windows installer built with Inno Setup.
- A portable x64 build.

BetterWinTab supports Windows 10/11.

---

## Requirements For Development

| Tool | Version |
|---|---|
| Windows | Windows 10 19041+ or Windows 11 |
| .NET SDK | 8.0.x |
| Visual Studio Code | Any recent version |
| Inno Setup | 6.x, only needed to build the installer |

Check your SDK:

```powershell
dotnet --version
```

---

## Build

BetterWinTab must be built for `x64`. Building without the platform flag can cause native WinUI dependencies to fail at startup.

```powershell
dotnet build BetterWinTab.csproj -p:Platform=x64
```

Build output:

```text
bin\x64\Debug\net8.0-windows10.0.19041.0\
```

From VS Code, you can also run the predefined task:

```text
Ctrl + Shift + B -> build-x64
```

---

## Run In Debug

Recommended VS Code task:

```text
Ctrl + Shift + P -> Tasks: Run Task -> run-x64
```

Manual run:

```powershell
Stop-Process -Name BetterWinTab -Force -ErrorAction SilentlyContinue
dotnet build BetterWinTab.csproj -p:Platform=x64
Start-Process "bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BetterWinTab.exe"
```

Hot reload:

```powershell
dotnet watch run --project BetterWinTab.csproj -p:Platform=x64 --runtime win-x64
```

Once the app starts, press `Ctrl + Tab` to show the overlay.

---

## Debug With VS Code

Create `.vscode/launch.json` if needed:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug BetterWinTab",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build-x64",
      "program": "${workspaceFolder}/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/BetterWinTab.exe",
      "cwd": "${workspaceFolder}",
      "stopAtEntry": false,
      "console": "internalConsole"
    }
  ]
}
```

Then press `F5` and use breakpoints in the C# files.

---

## Publish

Create a self-contained release build:

```powershell
dotnet publish BetterWinTab.csproj -p:Platform=x64 -c Release
```

Publish output:

```text
bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

Publish profiles are available under `Properties/PublishProfiles/` for `win-x64`, `win-x86`, and `win-arm64`.

---

## Deploy / Build The Installer

Publish first:

```powershell
dotnet publish BetterWinTab.csproj -p:Platform=x64 -c Release
```

Then compile the installer with Inno Setup:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer.iss
```

Installer output:

```text
installer-output\BetterWinTab-Setup-1.0.0-x64.exe
```

---

## License

BetterWinTab is released under the **MIT License**.

Built by **sgm108**.

## Star History

<a href="https://www.star-history.com/?repos=sgm1018%2FBetterWinTab&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=sgm1018/BetterWinTab&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=sgm1018/BetterWinTab&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=sgm1018/BetterWinTab&type=date&legend=top-left" />
 </picture>
</a>
