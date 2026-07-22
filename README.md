# RobloxKeeper

**Anti-AFK + Multi-Instance manager for Roblox on Windows.**
One tiny executable. Zero dependencies. No injection, no memory access, no file tampering.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![License](https://img.shields.io/badge/license-MIT-green)
![Dependencies](https://img.shields.io/badge/dependencies-none-blueviolet)

---

## Features

| | |
|---|---|
| **Anti-AFK** | Nudges every Roblox client on a timer (default 15 min, adjustable 1–19) so the 20-minute idle kick never fires. Briefly focuses each client, sends the input, and returns focus to whatever you were doing. Minimized clients are restored, nudged, and re-minimized. |
| **Nudge key profiles** | Choose what the nudge sends: **Turn camera** (`←`, `→` — default), **Zoom out + in** (`O`, `I`), or **Jump** (`Space`). Pick whichever is safe for your game's keybinds. |
| **Per-client selection** | Every running client appears as a row in the Clients panel (scrollable, so any number of clients works). Untick one and the nudger leaves it alone — run anti-AFK on two accounts while a third stays untouched. **Show** brings that client's window to the front so you can tell which is which. New clients default to enabled. |
| **Multi-Instance** | Holds Roblox's `ROBLOX_singletonMutex` (and `ROBLOX_singletonEvent`) so multiple clients can run simultaneously. A dedicated thread queue-waits on the mutex the same way Roblox clients do, so ownership transfers to RobloxKeeper at the kernel level the instant it frees — a launching client can never win the race. If clients already own it, one click on **Close all Roblox** clears them (ghost processes included) and takeover is immediate. |
| **Client monitor** | Live count of open Roblox clients, plus detection of window-less "ghost" Roblox processes (they can silently block multi-instance) with a one-click **End background** button. |
| **Single instance** | Launching RobloxKeeper while it's already running won't open a second copy — it surfaces the existing window instead, restoring it from the tray if needed. |
| **Start with Windows** | Optional autostart toggle (top-right). With it on, RobloxKeeper starts **minimized to the tray** at boot and holds the mutex before any Roblox client can exist, which makes the launch-order problem impossible. |
| **Saved settings** | Every setting — anti-AFK on/off, interval, nudge profile, multi-instance, auto-clear ghosts — is written to `%APPDATA%\RobloxKeeper\settings.txt` and restored on the next launch. |
| **Diagnostic log** | Every client open/close is logged with the reason, naming a **singleton kill**, the **Roblox bootstrapper**, or a normal close. **Copy log** puts the whole thing plus your version, Windows build, settings, and Roblox launch path on the clipboard for sharing. |
| **Launch-path check** | Warns at startup if Roblox launches via the legacy bootstrapper (`RobloxPlayerLauncher`), which closes running clients on every launch no matter who holds the mutex — the one failure mode multi-instance cannot fix from outside. |
| **Auto-clear ghosts** | Stuck window-less Roblox processes that block the mutex are ended automatically once they're over 60 seconds old (the age check protects clients that are still starting up). On by default; untick in the Clients panel to disable. |
| **Quality of life** | Dark modern UI, live countdown, activity log, minimize-to-tray with tray menu (Open / Nudge now / Exit). |

## Quick start

1. Download (or build) `RobloxKeeper.exe` and run it — **before** opening Roblox.
2. Open as many Roblox clients as you need.
3. Minimize RobloxKeeper to the tray. Done.

Both features are enabled by default on launch.

> **Note:** one Roblox *account* can't be in two games at once — that's enforced server-side. Multi-instance is for running multiple accounts (or one in-game plus others at the home screen).

## Building from source

No SDK or IDE required — it compiles with the C# compiler that ships inside Windows:

```bat
build.bat
```

To publish a new version (maintainers): `release.bat <version>` bumps `APP_VERSION`, builds, commits, pushes, and publishes a GitHub release with the exe attached.

That's it. The script generates the app icon (`make-icon.ps1`) and produces `RobloxKeeper.exe` (~45 KB) using `csc.exe` from the .NET Framework already on your machine.

## How it works

**Anti-AFK** uses `SendInput` with hardware scan codes — the same level of the input stack a physical keyboard writes to, which is why clients reading raw input register it. Extended keys (arrows) are sent with the `E0` flag so they aren't misread as numpad input. Each nudge: focus client → send keys → restore your previous window. The two-key profiles (zoom out/in, turn left/right) cancel themselves out, so your camera ends up where it started.

**Multi-Instance** relies on how Roblox enforces single-instancing: at startup the client checks a named mutex, `ROBLOX_singletonMutex`. When an external process already owns that mutex, clients skip the "close the other instance" path entirely. RobloxKeeper holds it from a dedicated thread that *queue-waits* on the mutex — Roblox clients wait in the same kernel queue, so whoever is queued first wins, and RobloxKeeper queues the moment it starts. When the owning client exits, ownership transfers to RobloxKeeper in microseconds; in testing, a competitor hammering the mutex with 113,000+ acquire attempts during the handover never won it once.

The most common reason multi-instance "sometimes doesn't work" with any tool: closing a Roblox window doesn't always end its process. A window-less ghost process lingers and **keeps owning the mutex**. RobloxKeeper surfaces these as "background" processes and removes them via **Close all Roblox** / **End background**.

## Byfron / Hyperion compatibility

RobloxKeeper is designed to stay entirely **outside** the Roblox process:

- **No DLL injection** — nothing is loaded into the client.
- **No memory reads or writes** — the game's process memory is never opened.
- **No file modification** — the Roblox installation is untouched.
- **OS-level only** — a named kernel mutex (a Windows object, not a Roblox one) and synthesized keyboard input, identical in mechanism to a hardware keyboard.

This is the same externally-held-mutex technique used by established multi-instance managers, and it does not interact with the anti-cheat's protected surface. That said, automation and multi-instancing are against the [Roblox Terms of Use](https://en.help.roblox.com/hc/en-us/articles/115004647846) — use at your own risk.

## FAQ

**Does it work while Roblox is minimized?**
Yes — the client is restored for about a second, nudged, and re-minimized.

**Multi-instance shows "Waiting" but I closed everything.**
A window-less Roblox process is probably still holding the mutex — the client counter will show it as "background". With **Auto-clear ghosts** on (the default) it's removed automatically within about a minute; **End background** clears it instantly.

**A client closed and I don't know why.**
Read the Activity log — it names the cause. `SINGLETON KILL` means another client launched while a Roblox process (not RobloxKeeper) owned the mutex: close all clients, wait for the green light, reopen. If a Roblox update was installing, its own updater closes every client and no tool can prevent that. Click **Copy log** to share the full report.

**My clients keep closing every few minutes and Roblox seems to "update" over and over.**
Two Roblox installs are fighting over the `roblox-player` registration. Each hand-over runs an installer, and that installer closes every open client — then the other install claims it back on the next launch. It never settles, so it looks like an endless update loop. Holding the mutex cannot stop this; it is Roblox's own installer.

**The usual cause is a third-party launcher** — Bloxstrap, Fishstrap, Voidstrap, and similar tools install and register their *own* Roblox version alongside the official one. Uninstalling Roblox alone does **not** fix it, because the third-party launcher reinstalls its copy on the next launch.

RobloxKeeper reports all of this: `ROBLOX RE-REGISTERED ITSELF` when the flip happens, plus `Version folders:` and `Third-party launchers:` in the **Copy log** header.

**Fix — pick ONE launcher and remove the rest:**

1. Close Roblox, all clients, and any third-party launcher.
2. Uninstall the launchers you don't want (Bloxstrap / Fishstrap / Voidstrap) and delete their folders in `%LOCALAPPDATA%` and `%APPDATA%`.
3. Uninstall Roblox and delete `%LOCALAPPDATA%\Roblox`.
4. Reinstall **once**, from your single chosen source.
5. Always launch from that same one. Mixing the website and a third-party launcher re-creates the conflict.

**My clients close every time I open another one, even though the light is green.**
Your Roblox install probably launches through the **legacy bootstrapper** (`RobloxPlayerLauncher.exe`). That bootstrapper validates/updates the install and **closes running clients on every launch** — it's a completely separate mechanism from the singleton mutex, so holding the mutex can't stop it. RobloxKeeper detects this at startup and warns you in the log; the **Copy log** header also reports `Legacy bootstrapper: True/False`.

Check it yourself:

```bat
reg query "HKCU\Software\Classes\roblox-player\shell\open\command" /ve
```

A healthy install points at **`RobloxPlayerBeta.exe`**. If it points at `RobloxPlayerLauncher.exe` or `RobloxPlayerInstaller.exe`, uninstall Roblox, delete `%LOCALAPPDATA%\Roblox`, and reinstall from roblox.com.

**My whole session died during a "big loading" screen.**
That's a Roblox version update. The updater terminates all running clients of the old version — no tool can prevent it. RobloxKeeper shows an amber warning and a tray notification when it detects the launcher/updater, and the log records it as the cause. Reopen your clients afterwards; multi-instance resumes automatically.

**To avoid it entirely:** open **one** client first and let it fully load into a game. That triggers any pending update while only one client is open. Once it's running, open the rest — no update can interrupt you mid-session.

**It works for me but not for my friend — their first client closes when they open a second.**
That symptom means RobloxKeeper wasn't holding the mutex when the second client launched — it's an ordering problem, not detection. On the friend's machine: (1) make sure the status light is **green before** opening any Roblox client — if it's amber, click **Close all Roblox** once; (2) enable **Start with Windows** so the app always wins the ordering race; (3) note the Microsoft Store version of Roblox is not supported — use the desktop client (installed via the website).

**Why does my camera zoom blink every 15 minutes?**
That's the nudge. Switch the key profile or raise the interval if it bothers you.

**Do I need to keep RobloxKeeper open?**
Yes — the mutex is only held while the app runs. Closing it releases the mutex (already-open clients stay open, but the next client you launch will single-instance again).

## License

[MIT](LICENSE) — do whatever you want, no warranty.

---

*** byVladDerKing***
