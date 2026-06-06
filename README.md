# Mimics Mod for R.E.P.O

[![Thunderstore](https://img.shields.io/thunderstore/v/ToxesFoxes/Mimics?logo=thunderstore&logoColor=white&label=Thunderstore)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/ToxesFoxes/Mimics?logo=thunderstore&logoColor=white&label=Downloads)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![GitHub Release](https://img.shields.io/github/v/release/ToxesFoxes/Repo-Mimics?label=GitHub%20Release&color=green)](https://github.com/ToxesFoxes/Repo-Mimics/releases)

Mimics adds a voice-mimic mechanic to monsters in R.E.P.O.
They can replay random phrases recorded during gameplay, making it harder to trust what you hear.

## 📖 Description

Think that was your teammate calling for help? Think again.
Mimics brings a Skinwalker-style mechanic into R.E.P.O by allowing monsters to mimic player voices using random recorded phrases.

## 🎯 Who Needs This Mod

Both Host and Clients need to install this mod to work.

## 🧩 Compatibility

This mod is incompatible with mods:
1.  [Disable Noise Reduction](https://thunderstore.io/c/repo/p/SVRZ/Disable_Noise_Reduction/) by [SVRZ](https://thunderstore.io/c/repo/p/SVRZ/), as it relies on the game's built-in noise reduction to capture audio and send chunks to other players correctly.

## ✨ Features

- Monsters mimic player voices recorded during the current session
- In multiplayer, all players hear the same mimic clip on the same enemy at the same time
- Voice can be played back with a random pitch or alien filter — the host decides which, so all players hear the same effect
- Custom audio support — drop your own `.mp3`, `.wav`, or `.ogg` files into the `custom-audio` folder to add more sounds to the mimic pool
- Mod authors can register custom clips at runtime via the public `MimicsAPI`

## 📦 Installation via r2modman

1. Install r2modman from https://github.com/ebkr/r2modmanPlus/releases
2. Open r2modman and select R.E.P.O
3. Create or choose a profile
4. Search for `Mimics` by ToxesFoxes
5. Install the mod

Dependencies are pulled automatically:

- BepInEx-BepInExPack-5.4.2305
- nickklmao-REPOConfig-1.2.6
- nickklmao-MenuLib-2.5.2
- Rune580-REPO_SteamNetworking_Lib-0.1.2

## ⚙️ Mod Settings

Settings are stored in the BepInEx config file:

```text
<REPO_PROFILE>/BepInEx/config/TFS_Mimics.cfg
```

### General

| Setting                        | Default | Acceptable Values | Description                                                                       |
| ------------------------------ | ------- | ----------------- | --------------------------------------------------------------------------------- |
| `Volume`                       | `30`    | `0-100`           | Mimic playback volume in percent.                                                 |
| `Hear Yourself?`               | `true`  | `true` / `false`  | If `false`, only other players hear mimic playback.                               |
| `Persist Audio Cache`          | `false` | `true` / `false`  | Enables saving received mimic clips to disk between sessions.                     |
| `Persist Max Files Per Player` | `100`   | `1-5000`          | Max saved recordings per player folder.                                           |
| `Normalize Target`             | `85`    | `0-100`           | Peak normalization target for voice and custom audio (`0` = off, `100` = 0 dBFS). |

### Host Only

These settings only take effect on the player who is host. Clients can leave them at defaults.

| Setting                          | Default | Acceptable Values | Description                                                                                      |
| -------------------------------- | ------- | ----------------- | ------------------------------------------------------------------------------------------------ |
| `Playback Near Radius`           | `15`    | `5-100`           | Preferred radius around players for selecting a mimic target enemy.                              |
| `MinDelay`                       | `10`    | `1-300`           | Minimum seconds between mimic playback cycles.                                                   |
| `MaxDelay`                       | `20`    | `5-600`           | Maximum seconds between mimic playback cycles.                                                   |
| `Playback Voice Filters Enabled` | `true`  | `true` / `false`  | If `true`, the host randomly applies voice filter to playback. All players hear the same effect. |

### Filter

- `Filter Enabled?` (default: `false`) — enables per-enemy mimic filter.

### Experimental

- `Sampling Rate` (default: `48000`, range: `16000-48000`) — microphone and processing sample rate.

### Debug

- `Menu Keybind` (default: `F8`) — keybind to open the Mimics debug menu in-game.
- `Verbose Logging` (default: `false`) — enables detailed debug logs.

## 📁 Custom Audio

Drop your own sound files here to add them to the mimic pool:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/custom-audio/
```

Supported formats: `.mp3`, `.wav`, `.ogg`.

If persistence is enabled, recorded clips are saved here:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/audio-cache/
    players.json
    <player_id>/audio_<player_id>_<guid>.bin
```

## 🧭 Roadmap

Planned: context-aware audio playback linked to gameplay events, for example:

- Enemy sees player
- Enemy hits player
- Player death / damage
- Item destroyed

Goal: make mimic behavior feel more believable and tied to events, instead of only random timing.

## 📝 Requirements

- R.E.P.O (Steam)
- BepInEx pack for R.E.P.O
- Rune580-REPO_SteamNetworking_Lib
- Windows OS

## ⚠️ Disclaimer

- This mod is not affiliated with or endorsed by the developers of R.E.P.O
- Use at your own risk
- Always back up your saves before using any mods

## 📞 Support

- 🐛 [Report Issues](https://github.com/ToxesFoxes/Repo-Mimics/issues)
- 📧 Contact: toxes_foxes@outlook.com

---

## 🔧 Manual Installation

1. Install BepInEx for R.E.P.O
2. Download the latest release from:
   - Thunderstore: https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/
   - GitHub: https://github.com/ToxesFoxes/Repo-Mimics/releases
3. Copy `TFS_Mimics.dll` into your plugins folder:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/TFS_Mimics.dll
```

4. Launch the game

## 🔍 Differences From Original Mimic

This mod is a full rework focused on compatibility with newer R.E.P.O versions and long-term stability.

- Reworked enemy discovery for newer game versions instead of relying on a fixed scene path.
- New audio transmission pipeline with per-transmission tracking and improved chunk handling.
- Backward-compatible network receive path for older packet format support.
- Improved recording flow with pre-speech capture and smarter silence handling.
- Persistent audio cache with per-player storage and players index.
- Safer runtime behavior with additional validation, stale transmission cleanup, and better diagnostics.
- Expanded configuration options for playback radius, persistence, filters, and debug logging.
- **Host-authority sound sync** — greedy set-cover algorithm selects the minimum number of enemies needed to cover all nearby players. Host decides the voice filter mode (none / pitch down ×0.5 / pitch up ×1.2 / alien) and broadcasts a `SyncPlayCommandPacket` so every client plays the exact same clip with the exact same effect simultaneously.
- **Custom audio folder** — add your own `.mp3`, `.wav`, or `.ogg` files without writing any code; mod authors can also register clips at runtime via `MimicsAPI`.

## 🛠️ Build from Source

1. Configure local paths in `TFS_Mimics.local.props`
2. Build:

```powershell
dotnet build TFS_Mimics.csproj -c Release
```

3. Output DLL:

```text
bin/Release/TFS_Mimics.dll
```

For one-command build and deploy, see README.build.md.

## 🤝 Contributing

Contributions are welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## 🙏 Credits

- **Harmony** - [Harmony Patching Library](https://github.com/pardeike/Harmony)
- **BepInEx** - [BepInEx Mod Loader](https://github.com/bepinex/bepinex)
- **r2modman** - [r2modmanPlus Mod Manager](https://github.com/ebkr/r2modmanPlus)
- **Thunderstore** - [Thunderstore Mod Hosting](https://thunderstore.io/)
- **Mimic** - [Original mod](https://thunderstore.io/c/repo/p/eth9n/Mimic/) for R.E.P.O v0.3.0 and lower by [eth9n](https://thunderstore.io/c/repo/p/eth9n/)
- **R.E.P.O** - Game by semiwork
- **ToxesFoxes** - Mod development
- **Contributors** - See GitHub contributors page for details

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/ToxesFoxes/Repo-Mimics/blob/main/LICENSE) file for details.

## 🤖 AI Content Disclaimer

This project used AI-assisted tooling during development for debugging, bug fixing, and technical iteration support.

- AI assistance was used as a development aid, not as an autonomous publisher.
- Final implementation decisions, integration, and testing were performed manually by the author.
- This notice is provided for transparency regarding the development workflow.

