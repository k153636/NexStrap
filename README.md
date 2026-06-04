<p align="center">
  <img src="src/NexStrap/Assets/nexstrap.png" width="120" alt="NexStrap Logo">
</p>

<h1 align="center">NexStrap</h1>

<div align="center">

[![Downloads](https://img.shields.io/github/downloads/k153636/NexStrap/latest/total?color=981bfe)](https://github.com/k153636/NexStrap/releases/latest)
[![Version](https://img.shields.io/github/v/release/k153636/NexStrap?color=7a39fb)](https://github.com/k153636/NexStrap/releases/latest)
[![License](https://img.shields.io/github/license/k153636/NexStrap)](LICENSE)
[![Discord](https://img.shields.io/discord/1511974633056895076?logo=discord&logoColor=white&label=discord&color=5865F2)](https://discord.gg/PPrKt97jRn)

</div>

<p align="center">
  A third-party Roblox launcher for Windows with powerful features for power users.
</p>

<p align="center">
  <img src="Images/preview.png" alt="NexStrap Preview" width="700">
</p>

> [!CAUTION]
> The only official place to download NexStrap is this GitHub repository. Any other sources are not affiliated with us.

> [!NOTE]
> NexStrap is not affiliated with Roblox Corporation.

---

## Features

**Discord Rich Presence**
- Game icon, server region, and elapsed playtime
- Roblox Studio support — Home / Editing / Testing presence
- Show Join Game button and creator name

**Fast Flags**
- Edit `ClientAppSettings.json` flags directly
- Save and load named profiles
- Preset bundles: Graphics Lite, Render Optimized, Memory/CPU, Network
- Hot reload — apply flags to a running session without restart

**Multi-Account Manager**
- Manage multiple Roblox accounts
- Import cookies from Chrome — no password required
- One-click account switching

**Mods**
- Import and enable/disable mod folders
- Apply textures, sounds, and fonts to Roblox's content directory

**Play Stats**
- Total playtime, session count, top games
- Per-game breakdown with 7-day bar chart

**Roblox Studio**
- Independent Studio install and launch
- Full Discord presence support

**Theme**
- Glass UI with semi-transparent sidebar
- 8 accent colors + custom color picker
- Custom background image with blur and brightness

---

## Installing

Download the [latest release](https://github.com/k153636/NexStrap/releases/latest) and run `NexStrap.exe`.

You will need [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). If it's not installed, Windows will prompt you automatically.

> Windows SmartScreen may show a warning on first launch. Click **More info** → **Run anyway**. This happens because NexStrap is not yet code-signed.

---

## FAQ

**Q: Is this malware?**
A: No. NexStrap is fully open source — the complete source code is available here on GitHub for anyone to verify.

**Q: Will I get banned from Roblox?**
A: No. NexStrap does not interact with the Roblox client the way exploits do. It only manages launch settings, FastFlags, and Discord Rich Presence.

**Q: Windows Defender flagged it**
A: This is a false positive common with unsigned applications. You can review the full source code here to verify it is safe.

---

## Building from Source

```
git clone https://github.com/k153636/NexStrap.git
cd NexStrap
dotnet build src/NexStrap/NexStrap.csproj -c Release
```

---

## Credits

- Built with [Avalonia](https://avaloniaui.net/) and .NET 9
- Inspired by [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap)
- Discord RPC via [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp)
