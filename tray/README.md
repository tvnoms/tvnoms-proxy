# tvnoms-tray

A small notification-area / menu-bar companion for [tvnoms-proxy](https://github.com/tvnoms/tvnoms-proxy). It shows whether a recording is currently in progress, and can start or stop the proxy from its menu.

tvnoms-proxy runs headless (a Windows Service or a per-user systemd daemon) and is designed to stay that way. This app is a separate, independent process: it connects to the proxy's existing SignalR hub and REST API to find out what's happening, and shows a tray icon accordingly. Quitting this app does not affect the proxy.

## How it works

- On launch, reads the proxy's own config file (`~/.config/tvnoms-proxy/appsettings.json` on Linux/macOS, `%AppData%\tvnoms-proxy\appsettings.json` on Windows) to find its port and web UI URL. No changes to tvnoms-proxy are required.
- Calls `GET /recordings` once at startup (and after every reconnect) to snapshot what's currently recording.
- Subscribes to the proxy's `/hubs/proxyStatus` SignalR hub for live `RecordingChanged`/`RecordingProgress` events.
- Shows one of three icon states: idle, recording (red badge), or disconnected (can't reach the proxy — shown only after ~5s of failed connection, so brief reconnects don't flicker).
- Tray menu: **Open web UI** (opens the proxy's configured web frontend), **Set web UI URL…**, **Start service** / **Stop service** (whichever applies to the current connection state), and **Quit**.
- The menu opens on left click as well as right click. On Windows that's done by calling Avalonia's own (private) menu-showing code on left click. On Linux the tray publishes its own StatusNotifierItem + dbusmenu over D-Bus (`Services/Linux/`) advertising `ItemIsMenu=true` with no `Activate` method, like libayatana-appindicator apps do, since Avalonia's own Linux tray icon hardcodes the opposite. The Avalonia `TrayIcon` in `App.axaml` stays the single definition of icon, tooltip and menu; on Linux it's kept hidden and mirrored onto the bus.
- Windows self-installs a per-user autostart entry on first run (`HKCU\...\Run`). Linux doesn't — see [Lifecycle](#lifecycle). macOS autostart (`LaunchAgent`) is implemented but unused until macOS packaging exists.

## Lifecycle

The tray starts with the proxy but doesn't stop with it — it stays up (showing the disconnected icon) while the proxy is stopped, so **Start service** is always reachable. It's a separate process (the proxy has no desktop session to draw an icon in), so each OS handles the two differently:

- **Linux** — packaged as a systemd user unit, `tvnoms-tray.service`, started by `tvnoms-proxy.service` (`Wants=` on the proxy side) and by a `graphical-session.target.wants` link when the desktop session comes up, whether or not the proxy is running. With no display available (e.g. a linger-started proxy with nobody logged in) the tray is skipped rather than failing. Quitting from the tray menu leaves it stopped until the proxy next starts or the next login. Start/Stop run `systemctl --user start|stop tvnoms-proxy.service`.
- **Windows** — the Velopack first-run hook launches the tray after installing the service, and the `HKCU\...\Run` entry starts it at login. Start/Stop follow the autostart mode picked at install: in Service mode they run `sc.exe start|stop TvnomsProxy` elevated (a UAC prompt each time); in ScheduledTask/RunKey mode they launch the proxy exe directly, or kill it (and any ffmpeg recordings under it), with no prompt.

## Running

```
dotnet run
```

Requires the proxy to be running (or it will simply show "disconnected" and retry with backoff until it appears).

## Scope notes

- macOS: the autostart/tray code path exists but there is no packaging (`.app` bundle, notarization, CI) yet, matching tvnoms-proxy's own current lack of macOS distribution.
- Packaging: the release workflow (`.github/workflows/build-installers.yaml`) publishes the tray alongside the proxy and bundles it into the Velopack installer (Windows) and the tarball, `.deb`, `.rpm` and Arch packages (Linux).

## Tests

```
dotnet test
```
