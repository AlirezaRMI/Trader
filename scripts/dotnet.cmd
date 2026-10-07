@echo off
setlocal
set "TRADER_ROOT=%~dp0.."
set "DOTNET_CLI_HOME=%TRADER_ROOT%\.artifacts\dotnet-home"
set "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
pushd "%TRADER_ROOT%"
if exist "%TRADER_ROOT%\.artifacts\dotnet\dotnet.exe" (
  "%TRADER_ROOT%\.artifacts\dotnet\dotnet.exe" %*
) else (
  dotnet %*
)
set "TRADER_EXIT=%ERRORLEVEL%"
popd
exit /b %TRADER_EXIT%
