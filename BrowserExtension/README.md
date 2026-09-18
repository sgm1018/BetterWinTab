# BetterWinTab Chromium Bridge

This MV3 extension shares open Chrome, Edge, and Brave tabs with BetterWinTab.
It uses Native Messaging, so the browser does not need remote debugging enabled.

## Local installation

Run these steps from the repository root unless stated otherwise.

1. Open `chrome://extensions`, `edge://extensions`, or `brave://extensions`.
2. Enable Developer mode.
3. Choose **Load unpacked** and select this `BrowserExtension` folder.
4. Copy the extension ID shown by each browser. The IDs may differ.
5. Build the native host:

```powershell
dotnet build BrowserExtension\NativeHost\BetterWinTab.NativeHost.csproj -c Release
```

6. Register the host for the current user. Pass all IDs if they differ. Run this from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\BrowserExtension\install-native-host.ps1 -ExtensionId "CHROME_EXTENSION_ID","EDGE_EXTENSION_ID","BRAVE_EXTENSION_ID"
```

Use only the ID for the browser you installed if you are configuring one browser.

7. Reload the extension, then restart the browser and BetterWinTab.
8. Enable **Settings -> General -> Browser tabs** in BetterWinTab.

The native host executable must remain at the path written by the installer script.
The extension sends only tab metadata needed for search and activation: title, URL,
browser, tab ID, browser window ID, and browser process ID.
