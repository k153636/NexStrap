<p align="center">
  <img src="src/NexStrap/Assets/nexstrap.png" width="120" alt="NexStrap Logo">
</p>

<h1 align="center">NexStrap</h1>

<div align="center">

[![Downloads](https://img.shields.io/github/downloads/k153636/NexStrap/total?color=981bfe&label=downloads&cacheSeconds=3600)](https://github.com/k153636/NexStrap/releases/latest)
[![Version](https://img.shields.io/github/v/release/k153636/NexStrap?color=7a39fb&cacheSeconds=3600)](https://github.com/k153636/NexStrap/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Discord](https://img.shields.io/discord/1511974633056895076?logo=discord&logoColor=white&label=discord&color=5865F2&cacheSeconds=60)](https://discord.gg/PPrKt97jRn)

</div>

<p align="center">
  A Roblox launcher built for developers and power users<br>
  with full <strong>Roblox Studio support</strong>, multi-instance play, deep Discord integration, and extensive customization.
</p>

<p align="center">
  <a href="docs/preview.md"><strong>Preview</strong></a> ·
  <a href="#features">Features</a> ·
  <a href="#installing">Installing</a> ·
  <a href="#faq">FAQ</a>
</p>

> [!CAUTION]
> The only official place to download NexStrap is this GitHub repository. Any other sources are not affiliated with us.

> [!NOTE]
> NexStrap is not affiliated with Roblox Corporation.

---

## Features

**Discord Rich Presence**

- Game icon, name, creator, server region, and elapsed playtime
- Multi-instance support that tracks each Roblox window independently and displays the focused window's game in real time
- Automatically switches between NexStrap / Roblox / Studio Discord apps
- Join Game button with direct game link
- Graceful fallback for private or friends-only games
- Studio presence with Home / Editing / Testing states via the Roblox Studio plugin

**Party Presets**

- Optional Discord party presets with per-place label and party size
- Separate Party page instead of mixing party controls into the main Discord RPC page
- Presets are off by default and only apply after pressing Save

**Roblox Studio**

- Independent Studio install and launch with no official installer needed
- Discord presence with Home / Editing / Testing detection
- Separate Fast Flags profile for Studio

**Fast Flags**

- Edit `ClientAppSettings.json` flags directly
- Save and load named profiles
- Preset bundles: Graphics Lite, Render Optimized, Memory/CPU, Network
- Hot reload to apply flags to a running session without restart
- Bulk import from text

**Friends**

- Real-time friends list with online / in-game presence
- Toast notifications when a friend comes online
- Avatar thumbnails and last-seen location

**Multi-Account Manager**

- Manage multiple Roblox accounts
- Import cookies from Chrome without a password
- One-click account switching
- Quick sign-in dialog

**Play Stats**

- Total playtime, session count, and top games
- Per-game breakdown with 7-day bar chart

**Theme & UI**

- Glass UI with semi-transparent sidebar
- 8 accent colors plus a custom color picker
- Custom background image with blur and brightness controls
- Stretch resolution helper

**Mods**

- Import and enable / disable mod folders
- Apply textures, sounds, and fonts to Roblox's content directory

**Safer Install / Update Flow**

- Signed executable verification for installer and update paths
- Safer diagnostic log masking around sensitive values
- More consistent release packaging and update handling

---

## Preview

Preview examples are available on the dedicated page:

[![Preview](https://img.shields.io/badge/Preview-7a39fb?style=for-the-badge)](docs/preview.md)

---

## Installing

Download the [latest release](https://github.com/k153636/NexStrap/releases/latest).

Current releases ship with:

- `NexStrap.exe`
- `NexStrap-x64.exe`

For normal Windows use, run `NexStrap-x64.exe`.

You will need [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). If it's not installed, Windows will prompt you automatically.

> Windows SmartScreen may show a warning on first launch. Click **More info** -> **Run anyway**. This happens because NexStrap is not yet code-signed.

---

## FAQ

**Q: Is this malware?**  
A: No. NexStrap is fully open source - the complete source code is available here on GitHub for anyone to verify.

**Q: Will I get banned from Roblox?**  
A: No. NexStrap does not interact with the Roblox client the way exploits do. It only manages launch settings, Fast Flags, and Discord Rich Presence.

**Q: Windows Defender flagged it**  
A: This is a false positive common with unsigned applications. You can review the full source code here to verify it is safe.

**Q: Does it work with multiple Roblox instances?**  
A: Yes. NexStrap tracks each instance independently and shows the focused window's game in your Discord status.

**Q: Does it work with Roblox Studio?**  
A: Yes. NexStrap can install and launch Studio independently, with full Discord presence support.

---

## Building from Source

```
git clone https://github.com/k153636/NexStrap.git
cd NexStrap
dotnet build NexStrap.sln -c Release
```

To produce the win-x64 release executable bundle used for GitHub Releases:

```
dotnet publish src/NexStrap/NexStrap.csproj /p:PublishProfile=win-x64-standalone
```

---

## Credits

- Built with [Avalonia](https://avaloniaui.net/) and .NET 9
- Discord RPC via [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp)
