param([int]$Port = 5080)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localSdk = Join-Path $projectRoot '.artifacts\dotnet\dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.artifacts\dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid HTTP port' }
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'Trader.sln'))) { throw 'Project root not found' }
$runDirectory = Join-Path $projectRoot 'data'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$pidFile = Join-Path $runDirectory 'local-processes.json'
if (Test-Path -LiteralPath $pidFile) { throw 'Run stop-local.ps1 before starting another instance.' }
Push-Location $projectRoot
try {
    & $dotnetExecutable build Trader.sln --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; no services started.' }
    $apiProject = Join-Path $projectRoot 'Api\Api.csproj'
    $bridgeProject = Join-Path $projectRoot 'Bridge\Bridge.csproj'
    $backend = Start-Process -FilePath $dotnetExecutable -ArgumentList @('run','--project',('"' + $apiProject + '"'),'--no-build','--no-restore','--no-launch-profile','--','--environment','Development','--urls',("http://127.0.0.1:" + $Port)) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'api.stdout.log') -RedirectStandardError (Join-Path $runDirectory 'api.stderr.log')
    $record = @(@{ Id=$backend.Id; Started=$backend.StartTime.ToUniversalTime().ToString('O'); Project=$apiProject })
    $record | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding UTF8
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        if ($backend.HasExited) { throw 'Backend exited. Check data/api.stderr.log.' }
        try { Invoke-RestMethod ("http://127.0.0.1:" + $Port + '/health/live') -TimeoutSec 1 | Out-Null; $ready=$true; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Backend did not become ready. Use stop-local.ps1 and check logs.' }
    $agent = Start-Process -FilePath $dotnetExecutable -ArgumentList @('run','--project',('"' + $bridgeProject + '"'),'--no-build','--no-restore','--no-launch-profile','--',('--Bridge:BackendUrl=ws://127.0.0.1:' + $Port + '/bridge/ws')) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'bridge.stdout.log') -RedirectStandardError (Join-Path $runDirectory 'bridge.stderr.log')
    $record += @{ Id=$agent.Id; Started=$agent.StartTime.ToUniversalTime().ToString('O'); Project=$bridgeProject }
    $record | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding UTF8
    Write-Host ("Dashboard: http://127.0.0.1:" + $Port)
    Write-Host 'Services started. New entries remain paused. Install TraderBridgeEA in MT4.'
} finally { Pop-Location }
