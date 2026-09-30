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

## 🚀 How to Run

| File | Purpose | What to Expect |
| :--- | :--- | :--- |
| **`start_server.vbs`** | **Starts the server in the background** | **Nothing will pop up on your screen** (by design!). No black CMD window, no taskbar clutter. |
| **`stop_server.bat`** | **Stops the server** | Terminates the background process cleanly and displays `SilentLink stopped.` |

### How to Verify It's Running
Since `start_server.vbs` runs invisibly:
1. Open **Windows Task Manager** (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Esc</kbd>).
2. Look under **Background processes** for **`SilentLink`**.
3. You can also view [server.log](server.log) to see active logs.

### Auto-Start with Windows
1. Press <kbd>Win</kbd> + <kbd>R</kbd>, type `shell:startup`, and press **Enter**.
2. Right-click `start_server.vbs` $\rightarrow$ **Create shortcut**.
3. Move that shortcut into the opened Startup folder.

---

## ⚙️ Configuration

### 1. Secret Key (`config.json`)
Create `config.json` (or copy from `config.example.json`):
```json
{
  "auth_token": "YOUR_SECRET_KEY",
  "port": 5000,
  "restrict_to_local_network": true
}
```

### 2. Commands Mapping (`commands.json`)
Add whatever commands or apps you want. Changes reload automatically—no server restart required!
```json
{
  "hibernate": {
    "path": "shutdown /h"
  },
  "sleep": {
    "path": "rundll32.exe powrprof.dll,SetSuspendState 0,1,0"
  },
  "lock": {
    "path": "rundll32.exe user32.dll,LockWorkStation"
  },
  "dota2": {
    "path": "cmd.exe /c start steam://rungameid/570"
  },
  "discord": {
    "path": "cmd.exe /c start discord:"
  }
}
```

---

## 📱 MacroDroid Setup (Android Quick Settings Tile)

1. In MacroDroid, create a new macro:
   - **Trigger**: **Quick Settings Tile** (e.g. Tile 1 named "Hibernate PC").
   - **Action**: **Connectivity** $\rightarrow$ **HTTP Request**:

| Field | Value |
| :--- | :--- |
| **Method** | `POST` |
| **URL** | `http://<YOUR_PC_LOCAL_IP>:5000/run` *(e.g. `http://192.168.18.5:5000/run`)* |
| **Request Header** | `X-Auth-Token: YOUR_SECRET_KEY` |
| **Content Type** | `application/json` |
| **Request Body** | `{"action": "hibernate"}` |

---

## 📡 API Responses

### Success (`200 OK`)
```json
{
  "status": "ok",
  "action": "hibernate",
  "message": "Launched 'hibernate' successfully"
}
```

### Unauthorized (`401 Unauthorized`)
```json
{
  "status": "error",
  "message": "Unauthorized: Missing or invalid token."
}
```

### Health Check (`GET /ping`)
Send `GET http://<YOUR_PC_IP>:5000/ping` with header `X-Auth-Token: YOUR_SECRET_KEY` to test connection without running commands. Returns:
```json
{
  "status": "ok",
  "message": "pong"
}
```
