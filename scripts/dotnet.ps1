param([Parameter(ValueFromRemainingArguments=$true)][string[]]$DotnetArguments)
$ErrorActionPreference = 'Stop'
$traderRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$traderLocalSdk = Join-Path $traderRoot '.artifacts\dotnet\dotnet.exe'
$traderDotnet = if (Test-Path -LiteralPath $traderLocalSdk) { $traderLocalSdk } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $traderRoot '.artifacts\dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $traderRoot
try { & $traderDotnet @DotnetArguments; exit $LASTEXITCODE }
finally { Pop-Location }
