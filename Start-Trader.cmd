@echo off
setlocal
set "TRADER_ROOT=%~dp0."
pushd "%TRADER_ROOT%"
echo Preparing Trader launcher...
call scripts\dotnet.cmd build Trader.Launcher\Trader.Launcher.csproj -c Release --nologo --verbosity quiet
if errorlevel 1 (
  echo Launcher build failed. Install the .NET 10 SDK or review the error above.
  pause
  popd
  exit /b 1
)
if exist "%TRADER_ROOT%\.artifacts\dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%TRADER_ROOT%\.artifacts\dotnet"
  set "DOTNET_ROOT_X64=%TRADER_ROOT%\.artifacts\dotnet"
)
start "" "%TRADER_ROOT%\.artifacts\bin\Trader.Launcher\release\Trader.Launcher.exe" --root "%TRADER_ROOT%" %*
set "TRADER_EXIT=%ERRORLEVEL%"
popd
exit /b %TRADER_EXIT%
