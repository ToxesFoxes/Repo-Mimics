# Mimics Mod for R.E.P.O

Mimics adds a voice-mimic mechanic to monsters in R.E.P.O.
They can replay random phrases recorded during gameplay, making it harder to trust what you hear.

[![Thunderstore](https://img.shields.io/badge/Thunderstore-Available-blueviolet)](https://thunderstore.io/c/repo/p/ToxesFoxes/Mimics/)
[![GitHub Release](https://img.shields.io/github/v/release/ToxesFoxes/Repo-Mimics?label=GitHub%20Release&color=orange)](https://github.com/ToxesFoxes/Repo-Mimics/releases)

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-R.E.P.O-green)
![Loader](https://img.shields.io/badge/loader-BepInEx-6aa84f)
![Status](https://img.shields.io/badge/status-working-brightgreen)

## 📖 Description

Think that was your teammate calling for help? Think again.
Mimics brings a Skinwalker-style mechanic into R.E.P.O by allowing monsters to mimic player voices using random recorded phrases.

## 🎯 Who Needs This Mod

Only clients need to install this mod.

## ✨ Features

- Monsters can mimic player voices during gameplay
- Random phrase playback from recorded in-game voice lines
- Works with standard BepInEx mod workflow for R.E.P.O

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
- **R.E.P.O** - Game by semiwork
- **ToxesFoxes** - Mod development
- **Contributors** - See GitHub contributors page for details

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/ToxesFoxes/Repo-Mimics/blob/main/LICENSE) file for details.

## 📞 Support

- 🐛 [Report Issues](https://github.com/ToxesFoxes/Repo-Mimics/issues)
- 📧 Contact: toxes_foxes@outlook.com
