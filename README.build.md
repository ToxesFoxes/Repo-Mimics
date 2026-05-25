# Mimics build instructions

## 1) Set game path
Open Mimics.local.props and set both GameDir and BepInExDir.

Expected folders:
- GameDir\\REPO_Data\\Managed
- BepInExDir\\core

## 2) Build
Run one of these commands in this folder:

```powershell
dotnet build .Mimics.csproj -c Release
```

## 3) Output
The DLL will be generated at:

- bin\\Release\\TFS_Mimics.dll

## 4) Deploy
Copy TFS_Mimics.dll to your BepInEx plugins folder.

## 5) Build and deploy scripts
If you want one command to build and copy the DLL to your active profile:

Before running the build script change DEFAULT_DEPLOY_DIR in build-and-deploy.sh/cmd to your active BepInEx profile plugins folder.

### Powershell path:

`C:\Users\ToxesFoxes\AppData\Roaming\r2modmanPlus-local\REPO\profiles\REPO v4\BepInEx\plugins\ToxesFoxes-Mimics`

### Bash path:

`/c/Users/ToxesFoxes/AppData/Roaming/r2modmanPlus-local/REPO/profiles/REPO v4/BepInEx/plugins/ToxesFoxes-Mimics`

Then run, script PowerShell/CMD:

```cmd
build-and-deploy.cmd
```

Git Bash:

```bash
./build-and-deploy.sh
```

Optional:
- pass build configuration (`Debug` or `Release`): `build-and-deploy.cmd Debug` or `./build-and-deploy.sh Debug`
- override target folder via env var `DEPLOY_DIR` (e.g. `set DEPLOY_DIR=C:\BepInEx\plugins\ToxesFoxes-Mimics` or `export DEPLOY_DIR=/c/BepInEx/plugins/ToxesFoxes-Mimics`)
