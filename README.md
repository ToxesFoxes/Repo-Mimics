# Mimics Mod for R.E.P.O

[![Thunderstore](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fv1%2Fpackage-metrics%2FToxesFoxes%2FMimics%2F&query=%24.latest_version&label=Thunderstore&prefix=v&logo=thunderstore&logoColor=white&color=blue)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![Thunderstore Downloads](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fv1%2Fpackage-metrics%2FToxesFoxes%2FMimics%2F&query=%24.downloads&label=Downloads&logo=thunderstore&logoColor=white&color=blue)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![GitHub Release](https://img.shields.io/github/v/release/ToxesFoxes/Repo-Mimics?label=GitHub%20Release&color=green)](https://github.com/ToxesFoxes/Repo-Mimics/releases)

Mimics adds a voice-mimic mechanic to monsters in R.E.P.O.
They can replay random phrases recorded during gameplay, making it harder to trust what you hear.

## 📖 Description

Think that was your teammate calling for help? Think again.
Mimics brings a Skinwalker-style mechanic into R.E.P.O by allowing monsters to mimic player voices using random recorded phrases.

## 🎯 Who Needs This Mod

Both Host and Clients need to install this mod to work.

## 🧩 Compatibility

Works with R.E.P.O **v0.5.0**.

`REPO_SteamNetworking_Lib` 0.1.2 is broken on v0.5.0 (it still hooks the removed `SteamManager`). Mimics ships a built-in compatibility fix that re-attaches the library to the new `SteamLobbyManager`, so other mods that depend on the library keep working too while Mimics is installed. The `Undefined target method ... SteamManagerPatches` error at startup comes from the library itself and is harmless. The fix turns itself off once an updated library hooks the game on its own.

All players in a lobby need the same Mimics version.

This mod is incompatible with mods:
1.  [Disable Noise Reduction](https://thunderstore.io/c/repo/p/SVRZ/Disable_Noise_Reduction/) by [SVRZ](https://thunderstore.io/c/repo/p/SVRZ/), as it relies on the game's built-in noise reduction to capture audio and send chunks to other players correctly.

## ✨ Features

- Monsters mimic player voices recorded during the current session
- In multiplayer, all players hear the same mimic clip on the same enemy at the same time
- Voice can be played back with a random pitch or alien filter — the host decides which, so all players hear the same effect
- **Smart director** — the host picks the voice, the enemy and the listeners by context instead of at random (see below)
- **Event reactions** — mimics react to deaths, damage, enemies spotting players, broken or grabbed valuables, and the Head Grabber carrying a player's head
- **Enemy-aware mimics** — sneaking and searching enemies lure with a teammate's voice, talkative enemies say stolen lines instead of their own, and signature moments (grabs, the Bomb Thrower's thrown head, Spinny's roulette…) get a voice
- **Talk back** — speak to an enemy while looking at it and it answers, up to a short back-and-forth
- **Enemies talk to each other** — two enemies replay a real conversation between players, line by line
- **Recording context** — every phrase remembers what was happening when it was said, and the director picks phrases that fit the moment
- **Sounds like a real voice** — mouths move with the voice, and mimics go through the game's voice mix: room reverb and muffled behind walls
- **Microphone setup wizard** on the main menu tunes how loud you need to speak to be recorded
- **Debug menu** (`F8`) with a live mic meter, recordings manager, director decisions and event feed
- Custom audio support — drop your own `.mp3` or `.wav` files into the `custom-audio` folder to add more sounds to the mimic pool
- Mod authors can register custom clips at runtime via the public `MimicsAPI`
- Built for long sessions: per-player memory limit, and players who join mid-session don't break the voice pool

## 🧠 Smart Director

The host scores every *voice × enemy × listener* combination and plays the most convincing one (with a bit of randomness):

- A voice of someone the listener can **see**, or who stands **right next to them**, is avoided — that would give the mimic away
- Voices of **far away** or **dead** players are preferred
- Enemies **hidden behind walls** or out of view are preferred over ones in plain sight
- **Lonely** listeners are targeted more often
- Recently and frequently played clips are used less; a fresh phrase is never played back in its first 10 s, and the same clip never twice within 30 s
- Your own voice is never played to you if `Hear Yourself?` is off
- A dead player's voice is aimed at those who **didn't see them die**
- An enemy tends to **keep the voice** it spoke with last
- The phrase has to **fit the scene**: screams for pain, calm lines and greetings for lures, whispers for a hiding player, phrases said to someone for an enemy that is being watched
- **Persona rhythm** — an enemy speaking as someone may continue after that player's usual pause between phrases

### Recording context

When you speak, Mimics remembers the moment together with the phrase: how loud it was (whisper / normal / loud / scream), your health and pose, the room, what you held, who you were looking at (an enemy or a teammate), teammates and enemies around you, and what had just happened (you got hurt, saw a teammate hurt or die, an enemy noticed you, a teammate showed up, a mimic spoke nearby…). If another player had just stopped talking, the phrase is marked as a reply to them. The context travels with the clip and is saved with it. You can see it in the **Recordings** tab of the debug menu.

### Event reactions

| Event                     | What happens                                                                                                     |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Player death              | 8–20 s later, an enemy near the body calls out with the dead player's voice                                      |
| Player hurt               | Shortly after, teammates nearby hear the injured player's voice from an enemy                                    |
| Enemy spotted player      | That enemy tries to lure the player with **another** teammate's voice                                            |
| Valuable broken           | An enemy nearby reacts with the voice of whoever held it                                                         |
| Valuable picked up        | Sometimes a hidden enemy nearby talks to the player who grabbed loot worth $200+                                 |
| Head Grabber carries head | The carried head calls out with its owner's voice and screams when the grabber attacks — the dead owner watches through it |
| Enemy sneaks / searches   | Enemies creeping up unseen call from behind; enemies looking under furniture try to talk a hiding player out      |
| Enemy voice lines         | Oogly, Birthday Boy, Tricycle and Bang sometimes say a teammate's words instead of their own line; Duck and Bomb Thrower answer theirs |
| Special moments           | Grabbed players (Spewer, Hidden, Snatcher, Tick) call for help in their own voice; Heart Hugger, Birthday Boy, Thin Man, Valuable Thrower and Ceiling Eye lure; the Bomb Thrower's thrown head talks; Spinny's roulette gets a reaction |
| Hunter hears mimics       | The blind Hunter goes to check — and may shoot at — a mimic voice near it                                         |
| Talking to an enemy       | Speak while looking at an enemy and it answers with a teammate's voice or an echo of yours; answer it back for up to 3 exchanges |
| Enemies talk to each other| Two calm enemies near each other replay a real exchange between players, so you overhear them                     |
| Enemy hits enemy / valuable | The one that got hit cries out; an enemy smashing loot speaks with the voice of whoever carried it              |
| Enemy hurt                | Hurt by a fall or a trap, it complains; hurt by a player, it screams in the attacker's voice or a teammate pleads to stop |
| Player flees / approaches / hides | Running from an enemy, someone calls after you; walking up to one, it greets you; hiding from one, it whispers that it's safe |

If no enemy is close enough when an event happens, it waits a few seconds for one to come near. Every event can be turned off in the **Host Only** settings, and `Smart Director = false` switches back to the classic random mode.

## 🎙️ Microphone Setup Wizard

On first launch (and after updates that change it) a short wizard opens on the main menu:

1. Checks the microphone selected in the game's audio settings
2. Measures your room's background noise (3 s of silence)
3. Measures your normal speaking level (5 s of talking)
4. Recommends a `Voice Detection Sensitivity` and lets you test it live

Reopen it any time by turning on `Show Setup Wizard` in the mod settings.

## 🧪 Debug Menu

Press `F8` in a level, the shop or the truck lobby. `Shift+F8` gives the mouse back to the game while the window stays visible. Rebind the key by pressing a new one in the **Settings** tab.

- **Status strip** — live mic level with noise floor and detection threshold markers, current playbacks
- **Recordings** — every cached voice and custom clip with waveform, play count and age: listen locally, play on the nearest enemy, pin (kept from auto-cleanup) or delete
- **Director** — pending events, recent decisions with a per-listener score breakdown (including why the phrase fit the moment), the event feed, and buttons that simulate every event for solo testing
- **Gizmos** (Overlays tab) — enemy pathfinding, who a player is talking to, enemy hits, player-vs-enemy moves and enemy dialogs, drawn in the world
- **Mobs / Players / Network / Overlays / Settings** — enemy list, player state and per-player mimic volume, transmissions and sound readiness, in-world overlays, all settings

## 📦 Installation via r2modman

1. Install r2modman from https://github.com/ebkr/r2modmanPlus/releases
2. Open r2modman and select R.E.P.O
3. Create or choose a profile
4. Search for `Mimics` by ToxesFoxes
5. Install the mod

Dependencies are pulled automatically:

- BepInEx-BepInExPack-5.4.2305
- REPOModding-REPOConfig-1.2.7
- REPOModding-MenuLib-2.6.0
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
| `Hear Yourself?`               | `true`  | `true` / `false`  | If `false`, your own voice is never played back to you; other players still hear it. |
| `Voice Detection Sensitivity`  | `85`    | `0-100`           | How easily your voice starts a recording. Higher catches quieter speech but also more background noise. Set by the setup wizard. |
| `Show Setup Wizard`            | `false` | `true` / `false`  | Opens the microphone setup wizard on the main menu. Turns itself off after Save / Skip. |
| `Max Clips In Memory Per Player` | `120` | `5-500`           | Recordings per player kept loaded during a session. The most-played unpinned ones are dropped first (from memory, not from disk). |
| `Persist Audio Cache`          | `false` | `true` / `false`  | Enables saving received mimic clips to disk between sessions.                     |
| `Persist Max Files Per Player` | `100`   | `1-5000`          | Max saved recordings per player folder. Pinned recordings don't count.            |
| `Normalize Target`             | `85`    | `0-100`           | Peak normalization target for voice and custom audio (`0` = off, `100` = 0 dBFS). |

### Host Only

These settings only take effect on the player who is host. Clients can leave them at defaults.

| Setting                          | Default | Acceptable Values | Description                                                                                      |
| -------------------------------- | ------- | ----------------- | ------------------------------------------------------------------------------------------------ |
| `Playback Near Radius`           | `15`    | `5-100`           | Preferred radius around players for selecting a mimic target enemy.                              |
| `MinDelay`                       | `10`    | `1-300`           | Minimum seconds between mimic playback cycles.                                                   |
| `MaxDelay`                       | `20`    | `5-600`           | Maximum seconds between mimic playback cycles.                                                   |
| `Playback Voice Filters Enabled` | `true`  | `true` / `false`  | If `true`, the host randomly applies voice filter to playback. All players hear the same effect. |
| `Smart Director`                 | `true`  | `true` / `false`  | Context-aware choice of voice, enemy and listeners. `false` = classic random selection.          |
| `Event: Player Death`            | `true`  | `true` / `false`  | The dead player's voice calls out near the body.                                                 |
| `Event: Player Hurt`             | `true`  | `true` / `false`  | Teammates nearby hear the injured player's voice.                                                |
| `Event: Enemy Spotted Player`    | `true`  | `true` / `false`  | The enemy lures the spotted player with another teammate's voice.                                |
| `Event: Valuable Broken`         | `true`  | `true` / `false`  | An enemy reacts with the voice of whoever held the broken valuable.                              |
| `Event: Head Grabber Talks`      | `true`  | `true` / `false`  | A head carried by the Head Grabber talks with its owner's voice.                                 |
| `Event: Valuable Picked Up`      | `true`  | `true` / `false`  | Sometimes a hidden enemy talks to the player who grabbed a valuable worth $200+.                 |
| `Event: Sneaking Enemy Lure`     | `true`  | `true` / `false`  | An enemy sneaking up unseen calls out in a teammate's voice from behind.                         |
| `Event: Look-Under Lure`         | `true`  | `true` / `false`  | An enemy searching under furniture tries to talk a hiding player out.                            |
| `Event: Enemy Voice Lines`       | `true`  | `true` / `false`  | Talkative enemies say a teammate's words instead of their own lines.                             |
| `Event: Enemy Special Moments`   | `true`  | `true` / `false`  | Grabs, lures, the thrown head and the roulette get a voice.                                      |
| `Event: Hunter Hears Mimics`     | `true`  | `true` / `false`  | The Hunter goes to check mimic voices within 20 m.                                               |
| `Event: Enemy Answers Talk`      | `true`  | `true` / `false`  | Enemies answer players who talk to them.                                                         |
| `Event: Enemies Talk To Each Other` | `true` | `true` / `false` | Two enemies replay a real exchange between players.                                             |
| `Event: Enemy Hits Enemy`        | `true`  | `true` / `false`  | An enemy hit by another one cries out.                                                           |
| `Event: Enemy Hits Valuable`     | `true`  | `true` / `false`  | An enemy smashing loot speaks with its last carrier's voice.                                     |
| `Event: Enemy Hurts Itself`      | `true`  | `true` / `false`  | An enemy hurt by a fall, a trap or a crash complains.                                            |
| `Event: Player Hurts Enemy`      | `true`  | `true` / `false`  | An enemy hit by a player screams or a teammate pleads to stop.                                   |
| `Event: Player Flees/Approaches/Hides` | `true` | `true` / `false` | Reactions to what a player does about an enemy.                                              |
| `Persona Rhythm`                 | `true`  | `true` / `false`  | An enemy speaking as someone may continue after that player's usual pause.                       |

### Filter

- `Filter Enabled?` (default: `false`) — enables per-enemy mimic filter.

### Experimental

- `Sampling Rate` (default: `48000`, range: `16000-48000`) — microphone and processing sample rate.
- `Storage Sample Rate` (default: `16000`, values: `16000`, `24000`, `32000`, `48000`) — rate recordings are stored and sent at. Playback filters out everything above 4.5 kHz anyway, so 16 kHz sounds the same at a third of the size.
- `Sound Like Player Voices` (default: `true`) — mimic voices go through the game's player-voice mix (room reverb, voice volume) and are muffled behind walls like real voices.
- `Player Voice Level` (default: `15`, range: `5-100`) — speech loudness of mimic voices in that mode. Raise it if mimics sound quieter than real teammates.

### Debug

- `Menu Keybind` (default: `F8`) — key that opens the Mimics debug menu (any Unity `KeyCode` name, or rebind by key press in the menu).
- `Verbose Logging` (default: `false`) — enables detailed debug logs.

## 📁 Custom Audio

Drop your own sound files here to add them to the mimic pool:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/custom-audio/
```

Supported formats: `.mp3`, `.wav`.

If persistence is enabled, recorded clips are saved here:

```text
<REPO_PROFILE>/BepInEx/plugins/ToxesFoxes-Mimics/audio-cache/
    players.json
    pinned.txt
    <player_id>/audio_<player_id>_<guid>.bin
```

Clips saved by 2.0 also keep their recording context; older files still load.

## 🧭 Roadmap

Done in 2.0: enemy-aware mimics, talking back, enemies talking to each other, recording context, voices that sound like players'.

Ideas for later:

- Opus compression for recordings (much smaller clips)
- Sending a few recent clips to players who join mid-session
- Mimic voices distorted like a walkie-talkie when played through one

## 📝 Requirements

- R.E.P.O v0.5.0 (Steam)
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
- **Custom audio folder** — add your own `.mp3` or `.wav` files without writing any code; mod authors can also register clips at runtime via `MimicsAPI`.
- **Smart director and event reactions** — context-aware selection of voice, enemy and listeners, plus reactions to deaths, damage, spotted players, valuables and the Head Grabber.
- **Enemy-aware behaviour and recording context** — per-enemy lures, voice lines, lip-sync and special moments; phrases carry the context they were said in, enemies answer players and replay real conversations to each other.

## 🛠️ Build from Source

1. Optionally point the build at your game install in `TFS_Mimics.local.props` (without it the build uses the `R.E.P.O.GameLibs.Steam` NuGet package, which can lag behind the current game version):

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\REPO</GameDir>
  </PropertyGroup>
</Project>
```

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

