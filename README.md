# Mimics Mod for R.E.P.O

Mimics adds a voice-mimic mechanic to monsters in R.E.P.O.
They can replay random phrases recorded during gameplay, making it harder to trust what you hear.

[![Thunderstore](https://img.shields.io/badge/Thunderstore-Rejected-red)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![GitHub Release](https://img.shields.io/github/v/release/ToxesFoxes/Repo-Mimics?label=GitHub%20Release&color=orange)](https://github.com/ToxesFoxes/Repo-Mimics/releases)

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-R.E.P.O-green)
![Loader](https://img.shields.io/badge/loader-BepInEx-6aa84f)
![Status](https://img.shields.io/badge/status-working-brightgreen)

## 📖 Description

Think that was your teammate calling for help? Think again.
Mimics brings a Skinwalker-style mechanic into R.E.P.O by allowing monsters to mimic player voices using random recorded phrases.

## 🎯 Who Needs This Mod

Both Host and Clients need to install this mod to work.

## ✨ Features

- Monsters can mimic player voices during gameplay
- Random phrase playback from recorded in-game voice lines
- Works with standard BepInEx mod workflow for R.E.P.O

## 🔍 Differences From Original Mimic

This mod is a full rework focused on compatibility with newer R.E.P.O versions and long-term stability.

- Reworked enemy discovery for newer game versions instead of relying on a fixed scene path.
- New audio transmission pipeline with per-transmission tracking and improved chunk handling.
- Backward-compatible network receive path for older packet format support.
- Improved recording flow with pre-speech capture and smarter silence handling.
- Persistent audio cache with per-player storage and players index.
- Safer runtime behavior with additional validation, stale transmission cleanup, and better diagnostics.
- Expanded configuration options for playback radius, persistence, filters, and debug logging.

## ⚙️ Mod Settings

Settings are stored in the BepInEx config file:

```text
<REPO_PROFILE>/BepInEx/config/TFS_Mimics.cfg
```

Main options:

- `General > Volume` (default: `10`, range: `0-20`) - mimic playback volume in percent.
- `General > Playback Near Radius` (default: `15`, range: `5-100`) - preferred radius around local player for playback target selection.
- `General > MinDelay` (default: `15`, range: `5-300`) - minimum delay before random record/play cycle.
- `General > MaxDelay` (default: `60`, range: `10-600`) - maximum delay before random record/play cycle.
- `General > Hear Yourself?` (default: `false`) - if `false`, only other players hear mimic playback.
- `General > Playback Voice Filters Enabled` (default: `true`) - enables random pitch/alien filters on playback.
- `General > Persist Audio Cache` (default: `true`) - enables saving received mimic clips to disk.
- `General > Persist Max Files Per Player` (default: `100`, range: `1-5000`) - max saved recordings per player folder.
- `Experimental > Sampling Rate` (default: `48000`, range: `16000-48000`) - microphone and processing sample rate.
- `Filter > Filter Enabled?` (default: `false`) - enables per-enemy mimic filter.
- `Debug > Verbose Logging` (default: `false`) - enables detailed debug logs.

If persistence is enabled, files are saved inside the mod folder:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/audio-cache
```

Inside `audio-cache`, the mod also creates a players index file:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/audio-cache/players.json
```

Each player gets a separate folder by player ID, and their recordings are stored there:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/audio-cache/
	players.json
	<player_id_1>/audio_<player_id_1>_<guid>.bin
	<player_id_2>/audio_<player_id_2>_<guid>.bin
```

## 📝 Requirements

- R.E.P.O (Steam)
- BepInEx pack for R.E.P.O
- Windows OS

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

## 🔧 Manual installation

1. Install BepInEx for R.E.P.O
2. Download the latest release from:
	- Thunderstore: https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/
	- GitHub: https://github.com/ToxesFoxes/Repo-Mimics/releases
3. Copy TFS_Mimics.dll into your plugins folder:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/TFS_Mimics.dll
```

4. Launch the game

## 🛠️ Build from Source

1. Configure local paths in TFS_Mimics.local.props
2. Build:

```powershell
dotnet build TFS_Mimics.csproj -c Release
```

3. Output DLL:

```text
bin/Release/TFS_Mimics.dll
```

For one-command build and deploy, see README.build.md.

## ⚠️ Disclaimer

- This mod is not affiliated with or endorsed by the developers of R.E.P.O
- Use at your own risk
- Always back up your saves before using any mods

## 🤖 AI Content Disclaimer

This project used AI-assisted tooling during development for debugging, bug fixing, and technical iteration support.

- AI assistance was used as a development aid, not as an autonomous publisher.
- Final implementation decisions, integration, and testing were performed manually by the author.
- This notice is provided for transparency regarding the development workflow.

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

## 🧭 Roadmap

Planned feature: context-aware audio system.

Mimic playback will be linked to gameplay context, for example:

- Enemy sees player
- Enemy hits player
- Enemy death
- Enemy destroyed
- Item destroyed
- Player death
- Player damaged
- Player carrying item

Goal: make mimic behavior feel more believable and tied to events, instead of only random playback timing.

## 📞 Support

- 🐛 [Report Issues](https://github.com/ToxesFoxes/Repo-Mimics/issues)
- 📧 Contact: toxes_foxes@outlook.com
