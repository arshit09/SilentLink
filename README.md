# SilentLink

> A lightweight, zero-dependency background server to trigger Windows actions (Hibernate, launch games, apps) directly from your phone's **Android Quick Settings Tiles** or **MacroDroid**.

---

## 💡 Why This Exists

Apps like **KDE Connect** let you run commands on your PC, but they fall short for quick, everyday use:
- You have to unlock your phone, open the app, find your PC, and navigate to the commands menu.
- **You cannot turn commands into Android Quick Settings Tiles** or home screen 1-tap toggles.

This project solves that pain point. It runs silently as a background service on your PC, accepting authenticated local requests from MacroDroid, Tasker, or shortcuts.

### Common Use Cases
- 🛌 **1-Tap from Bed**: Pull down your phone's notification shade and tap a Quick Settings tile to **Hibernate** or **Sleep** your PC across the room.
- 🎮 **Pre-launch Games**: Tap a tile on your way to your desk to launch **Dota 2** or **Discord** so everything is loaded by the time you sit down.
- 🔒 **Lock Workstation**: Lock your PC remotely when stepping away.

---

## 📥 Install

1. Download **`SilentLink.exe`** from the [latest release](https://github.com/arshit09/SilentLink/releases/latest).
2. Put it in the same folder as `start_server.vbs`, `stop_server.bat`, and `commands.json`.
3. Copy `config.example.json` to `config.json` and set your own `auth_token` — see [Configuration](#%EF%B8%8F-configuration) below.

---

## 🚀 How to Run

| File | Purpose | What to Expect |
| :--- | :--- | :--- |
| **`start_server.vbs`** | **Starts the server in the background** | **Nothing will pop up on your screen** (by design!). No black CMD window, no taskbar clutter. |
| **`stop_server.bat`** | **Stops the server** | Terminates the background process cleanly and displays `SilentLink stopped.` |

### How to Verify It's Running
Since `start_server.vbs` runs invisibly:
1. Open **Windows Task Manager** (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Esc</kbd>).
2. Look under **Background processes** for **`SilentLink`**.
3. Check `server.log` in the project folder for live activity logs.

### Auto-Start with Windows
1. Press <kbd>Win</kbd> + <kbd>R</kbd>, type `shell:startup`, and press **Enter**.
2. Right-click `start_server.vbs` → **Create shortcut**.
3. Move that shortcut into the opened Startup folder.

---

## ⚙️ Configuration

### 1. Secret Key (`config.json`)

Copy the bundled template, then edit it:

```bat
copy config.example.json config.json
```

```json
{
  "auth_token": "YOUR_LONG_RANDOM_SECRET",
  "port": 5000,
  "restrict_to_local_network": true
}
```

| Key | Default | Purpose |
| :--- | :--- | :--- |
| `auth_token` | *(built-in — see warning)* | Shared secret that every request must present. |
| `port` | `5000` | TCP port the server listens on. |
| `restrict_to_local_network` | `true` | When `true`, only loopback and private IPv4 addresses are served. |

> ⚠️ **Create `config.json` before you start using the server.** If the file is missing, SilentLink falls back to a built-in default token that is visible in this public source code — anyone on your network could trigger your actions.

**Reload behaviour:** `auth_token` and `restrict_to_local_network` are re-read automatically whenever the file changes, so you can rotate your token without restarting. Changing `port` **does** need a restart, because the listener is bound at startup.

### 2. Commands Mapping (`commands.json`)

Add whatever commands or apps you want. **Changes reload automatically — no server restart required.**

```json
{
  "hibernate": {
    "path": "shutdown /h",
    "description": "Put PC into Hibernate"
  },
  "sleep": {
    "path": "rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
    "description": "Put PC to Sleep"
  },
  "lock": {
    "path": "rundll32.exe user32.dll,LockWorkStation",
    "description": "Lock Windows Workstation"
  },
  "dota2": {
    "path": "cmd.exe /c start steam://rungameid/570",
    "description": "Launch Dota 2 via Steam"
  },
  "discord": {
    "path": "cmd.exe /c start discord:",
    "description": "Launch Discord"
  }
}
```

Both of these forms work — use the shorthand when you don't need a note to yourself:

```json
{
  "notepad": { "path": "notepad.exe", "description": "Opens Notepad" },
  "calc": "calc.exe"
}
```

Action names are matched **case-insensitively**, and `description` is purely for your own reference — the server never reads it.

---

## 📱 MacroDroid Setup (Android Quick Settings Tile)

1. In MacroDroid, create a new macro:
   - **Trigger**: **Quick Settings Tile** (e.g. Tile 1 named "Hibernate PC").
   - **Action**: **Connectivity** → **HTTP Request**:

| Field | Value |
| :--- | :--- |
| **Method** | `POST` |
| **URL** | `http://<YOUR_PC_LOCAL_IP>:5000/run` *(e.g. `http://192.168.1.5:5000/run`)* |
| **Request Header** | `X-Auth-Token: YOUR_SECRET_KEY` |
| **Content Type** | `application/json` |
| **Request Body** | `{"action": "hibernate"}` |

---

## 📡 API Reference

### Authentication

Every request must carry your token one of three ways:

| Method | Example |
| :--- | :--- |
| `X-Auth-Token` header *(recommended)* | `X-Auth-Token: YOUR_SECRET_KEY` |
| `Authorization: Bearer` header | `Authorization: Bearer YOUR_SECRET_KEY` |
| `token` query parameter | `/ping?token=YOUR_SECRET_KEY` |

The query parameter is convenient for a quick browser test, but it leaks the token into logs and history — prefer a header everywhere else.

### `POST /run` — Execute an action
```
POST http://<PC_IP>:5000/run
X-Auth-Token: YOUR_SECRET_KEY
Content-Type: application/json

{"action": "hibernate"}
```
```json
{"status":"ok","action":"hibernate","message":"Launched successfully"}
```

### `GET /ping` — Health check
```
GET http://<PC_IP>:5000/ping
X-Auth-Token: YOUR_SECRET_KEY
```
```json
{"status":"ok","message":"pong","client_ip":"192.168.1.10"}
```

Any `GET` path other than `/run` answers the health check, so `/` works just as well as `/ping`.

### Status codes

| Code | When | Response body |
| :--- | :--- | :--- |
| `200` | Action launched, or health check | `{"status":"ok",...}` |
| `400` | `POST` body had no `action` key | `{"status":"error","message":"Missing 'action'."}` |
| `401` | Token missing or wrong | `{"status":"error","message":"Unauthorized: Missing or invalid token."}` |
| `403` | Caller is not on the local network | `{"status":"error","message":"Forbidden: Non-local IP"}` |
| `404` | Action not found in `commands.json` | `{"status":"error","action":"<name>","message":"Action not defined."}` |
| `405` | `GET /run` — use `POST` instead | `{"status":"error","message":"Use POST /run."}` |
| `500` | Action found, but Windows refused to launch it | `{"status":"error","message":"Failed to launch."}` |

---

## 🔨 Building from Source

Every [release](https://github.com/arshit09/SilentLink/releases/latest) ships a pre-built `SilentLink.exe`, so you only need this section if you want to **change `SilentLink.cs` and produce your own `.exe`**.

### Requirements

- Windows with **.NET Framework 4.0 or newer** — pre-installed on every modern Windows.
- **Nothing to download.** The C# compiler (`csc.exe`) already ships inside Windows. No IDE, no NuGet, no project files.

### The one rule that matters: build as `winexe`, not `exe`

SilentLink must be compiled as a **Windows application** (`winexe`), *not* a console application (`exe`). A console build pops up a black `cmd` window every time the server starts, which defeats the whole point of the project. Every option below sets this — don't skip that step.

### Option A — One command (recommended)

Open **PowerShell** in the project folder and run:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:SilentLink.exe SilentLink.cs
```

On 32-bit Windows, use `Framework` instead of `Framework64`:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" /target:winexe /out:SilentLink.exe SilentLink.cs
```

You should end up with a `SilentLink.exe` of roughly 15 KB in the same folder. Warnings are harmless; errors are not.

### Option B — Visual Studio

1. **File → New → Project → C# Console App (.NET Framework)**, targeting .NET Framework 4.0 or later.
2. Replace the generated `Program.cs` with the contents of `SilentLink.cs`.
3. **Project → Properties → Application → Output type → `Windows Application`.** ← required, per the rule above.
4. Build with <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd>.
5. Copy `bin\Release\SilentLink.exe` into this project folder.

### Option C — dotnet CLI

Needs the .NET SDK **and** the .NET Framework 4.8 targeting pack installed — Option A needs neither.

```powershell
dotnet new console -n SilentLinkBuild --framework net48
copy SilentLink.cs SilentLinkBuild\Program.cs /y
```

Set the output type in `SilentLinkBuild\SilentLinkBuild.csproj` so no console window appears:

```xml
<OutputType>WinExe</OutputType>
```

```powershell
dotnet publish SilentLinkBuild -c Release -o out
copy out\SilentLinkBuild.exe SilentLink.exe /y
```

### Replacing a build that's already running

A running server holds a lock on its own `.exe`, so stop it before overwriting:

```bat
stop_server.bat
:: copy your new SilentLink.exe into this folder
start_server.vbs
```

### Verify your build

Start it directly:

```powershell
.\SilentLink.exe
```

Your prompt returns immediately and **no window appears** — that is the correct behaviour for a `winexe` build. Confirm it came up by checking `server.log`, which should end with `SilentLink Server is RUNNING on port 5000`, then ping it from another shell:

```powershell
curl.exe -H "X-Auth-Token: YOUR_SECRET_KEY" http://localhost:5000/ping
```

```json
{"status":"ok","message":"pong","client_ip":"127.0.0.1"}
```

### Troubleshooting

| Symptom | Cause and fix |
| :--- | :--- |
| `csc.exe` not found | Wrong path. Run `dir $env:WINDIR\Microsoft.NET\Framework64` and use whichever `v4.0.*` folder exists. |
| A black console window appears at startup | Built as `exe`. Rebuild with `/target:winexe`, or set Output type to **Windows Application**. |
| `Access to the path 'SilentLink.exe' is denied` | The old server is still running. Run `stop_server.bat` first. |
| Starts, then immediately exits | Read `server.log` for `[MAIN CRASH]`. Usually the port is already taken — change `port` in `config.json`. |
| No `server.log` is ever created | The exe can't write to its own folder. Move the project somewhere user-writable (not `C:\Program Files`). |
| Every request returns `401` | `config.json` isn't being read. It must sit **next to the `.exe`**, and your token must match exactly. |

---

## 🔒 Security Notes

- **Set your own `auth_token` first.** Without a `config.json`, the server falls back to a default token that is public in this repository.
- Every request needs a valid token via `X-Auth-Token`, `Authorization: Bearer`, or `?token=`. Make it long and random.
- With `restrict_to_local_network: true`, only loopback and private IPv4 ranges are accepted — `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12` and `192.168.0.0/16`. Anything else gets a `403`. IPv6 callers are rejected too while this is on, so point your phone at your PC's **IPv4** address.
- Traffic is plain HTTP, so the token crosses your LAN unencrypted. That's fine on home Wi-Fi; don't rely on it on a network you don't trust.
- **Don't port-forward** the server's port on your router. Nothing is reachable from the internet unless you do.
- Entries in `commands.json` run with your Windows user's full privileges. Treat that file as trusted input.
