param([ValidateSet('run', 'preview', 'setup', 'check', 'draft-latest', 'status')][string]$Mode = 'run')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($Mode -eq 'run') { dotnet run --project src/Channel.Worker }
    else { dotnet run --project src/Channel.Worker -- "--$Mode" }
    if ($LASTEXITCODE -ne 0) { throw "Сервис завершился с кодом $LASTEXITCODE" }
} finally { Pop-Location }
