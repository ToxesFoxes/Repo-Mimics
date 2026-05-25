@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT_DIR=%~dp0"
set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

set "DEFAULT_DEPLOY_DIR=C:\Users\ToxesFoxes\AppData\Roaming\r2modmanPlus-local\REPO\profiles\REPO v4\BepInEx\plugins\ToxesFoxes-Mimics"
if "%DEPLOY_DIR%"=="" (
  set "DEPLOY_DIR=%DEFAULT_DEPLOY_DIR%"
)

echo [1/3] Building project (%CONFIGURATION%)...
pushd "%ROOT_DIR%"
dotnet build Mimics.csproj -c %CONFIGURATION%
if errorlevel 1 (
  popd
  exit /b 1
)

set "OUT_DIR=%ROOT_DIR%bin\%CONFIGURATION%"
set "DLL_PATH=%OUT_DIR%\TFS_Mimics.dll"
if not exist "%DLL_PATH%" (
  set "DLL_PATH=%OUT_DIR%\Mimics.dll"
)

if not exist "%DLL_PATH%" (
  echo Build finished, but no DLL found in: %OUT_DIR%
  popd
  exit /b 1
)

echo [2/3] Ensuring deploy folder exists...
if not exist "%DEPLOY_DIR%" mkdir "%DEPLOY_DIR%"

echo [3/3] Deploying %DLL_PATH% to: %DEPLOY_DIR%
copy /Y "%DLL_PATH%" "%DEPLOY_DIR%\" >nul
if errorlevel 1 (
  popd
  exit /b 1
)

popd
echo Done.
exit /b 0
