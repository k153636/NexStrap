# NexStrap

A third-party Roblox launcher for Windows, inspired by Bloxstrap.

> **Not affiliated with Roblox Corporation.**

---

## Features

### Home
- Shows currently playing game with icon, server region, and elapsed time
- Favorite games list with quick-launch buttons
- Join Game button from Discord Rich Presence
- Windows Jump List integration for recent games

### Friends
- Real-time friends list with online/in-game presence
- Avatar thumbnails and last location display
- Toast notifications when a friend comes online

### Account
- Manage multiple Roblox accounts
- Import cookies directly from Chrome (no password required)
- Switch active account with one click

### Fast Flags
- Read and edit `ClientAppSettings.json` flags
- Profile system — save and load named flag sets
- Preset bundles: Graphics Lite, Render Optimized, Memory/CPU, Network Optimized
- Hot reload: apply flags to a running Roblox session without restart
- FPS limit slider with instant apply

### Mods
- Import mod folders (textures, sounds, fonts)
- Enable/disable individual mods
- Apply all enabled mods to Roblox's content directory

### Discord Rich Presence
- In-game presence: game icon (large) + your avatar (small)
- Per-toggle display settings:
  - Show creator name after game title
  - Show Join Game button
  - Show launcher presence while outside a game
  - Show Details text line

### Theme
- Glass UI with semi-transparent sidebar
- 8 accent colors + custom color picker
- Custom background image with blur and brightness sliders

### Settings
- FPS unlock (removes the 60fps cap)
- Multi-threading renderer toggle
- Minimize to system tray
- Auto-update Roblox on launch
- Performance overlay

### Stats
- Total play time, session count, top game
- Per-game breakdown: time, sessions, last played
- Sort by time, sessions, last played, or name
- 7-day bar chart

---

## Requirements

- Windows 10 (build 19041) or later
- .NET 9 Desktop Runtime
- Roblox installed via the official installer

---

## Building from Source

```
git clone https://github.com/k153636/NexStrap.git
cd NexStrap
dotnet build src/NexStrap/NexStrap.csproj -c Release
```

The output is in `src/NexStrap/bin/Release/net9.0-windows10.0.19041.0/`.

---

## Discord Application ID

NexStrap uses Discord Rich Presence. To enable it, create a `.env` file in the project root (or next to the executable) with:

```
DISCORD_APP_ID=your_application_id
```

Create a Discord application at [discord.com/developers](https://discord.com/developers/applications) and upload your assets named `nexstrap` and `roblox`.

---

## Credits

- Built with [Avalonia](https://avaloniaui.net/) and .NET 9
- Inspired by [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap)
- Discord RPC via [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp)
