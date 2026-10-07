$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pidFile = Join-Path $projectRoot 'data\local-processes.json'
if (-not (Test-Path -LiteralPath $pidFile)) { Write-Host 'No recorded services.'; exit 0 }
$records = @(Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json)
foreach ($record in $records) {
    $expectedProject = [IO.Path]::GetFullPath([string]$record.Project)
    if (-not $expectedProject.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe recorded project path' }
    $serviceProcess = Get-Process -Id ([int]$record.Id) -ErrorAction SilentlyContinue
    if ($null -eq $serviceProcess) { continue }
    $sameStart = $serviceProcess.StartTime.ToUniversalTime().ToString('O') -eq [string]$record.Started
    if ($serviceProcess.ProcessName -ne 'dotnet' -or -not $sameStart) { Write-Warning 'Process identity changed; not stopping it.'; continue }
    & taskkill.exe /PID ([int]$record.Id) /T /F | Out-Null
}
Remove-Item -LiteralPath $pidFile
Write-Host 'Recorded services stopped. Broker protection remains with MT4.'
