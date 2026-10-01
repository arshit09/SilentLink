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
3. Create your `config.json` — see [Configuration](#%EF%B8%8F-configuration) below.

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
Create `config.json` in the project folder:
```json
{
  "auth_token": "YOUR_SECRET_KEY",
  "port": 5000,
  "restrict_to_local_network": true
}
```

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

### `POST /run` — Execute an action
```
POST http://<PC_IP>:5000/run
X-Auth-Token: YOUR_SECRET_KEY
Content-Type: application/json

{"action": "hibernate"}
```

**Success (`200 OK`)**
```json
{"status":"ok","action":"hibernate","message":"Launched successfully"}
```

**Action not found (`404`)**
```json
{"status":"error","action":"unknown","message":"Action not defined."}
```

**Unauthorized (`401`)**
```json
{"status":"error","message":"Unauthorized."}
```

### `GET /ping` — Health check
```
GET http://<PC_IP>:5000/ping
X-Auth-Token: YOUR_SECRET_KEY
```
```json
{"status":"ok","message":"pong","client_ip":"192.168.1.10"}
```

---

## 🔨 Building from Source

A pre-built `SilentLink.exe` is attached to every [release](https://github.com/arshit09/SilentLink/releases/latest), so you only need this section if you want to **modify the source** (`SilentLink.cs`) and recompile it yourself.

### Requirements
- Windows (any version with .NET Framework 4.0+, which is pre-installed on every modern Windows)
- No external tools needed — the compiler ships with Windows itself

### Option A — One command (no install required)

Open **PowerShell** or **Command Prompt** in the project folder and run:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:SilentLink.exe SilentLink.cs
```

> If you're on a 32-bit system, use `Framework` instead of `Framework64`:
> ```
> C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
> ```

That's it — `SilentLink.exe` is rebuilt in place. No project files, no IDE, no NuGet.

### Option B — Visual Studio

1. Create a new **C# Console App (.NET Framework)** project.
2. Replace the generated `Program.cs` content with the contents of `SilentLink.cs`.
3. Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> to build.
4. Copy the resulting `.exe` from `bin\Release\` back into this folder.

### Option C — dotnet CLI

```powershell
dotnet new console -n SilentLink --framework net48
# Replace Program.cs with SilentLink.cs content, then:
dotnet publish -c Release -r win-x64 --self-contained false
```

### After rebuilding
Stop the running server first, then replace the `.exe`:
```bat
stop_server.bat
# copy your new SilentLink.exe here
start_server.vbs
```

---

## 🔒 Security Notes

- The server only accepts requests from **private/local network IPs** (192.168.x.x, 10.x.x.x, etc.) when `restrict_to_local_network` is `true`.
- Every request requires a valid `X-Auth-Token` header. Choose a long, random token.
- No ports are exposed to the internet as long as you don't forward port 5000 on your router.
