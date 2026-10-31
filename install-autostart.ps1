$ErrorActionPreference = 'Stop'
$taskName = 'DotNetChannel'
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) { throw 'Task DotNetChannel already exists. Inspect it before replacing.' }
$account = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$launcher = Join-Path $PSScriptRoot 'src\Channel.Launcher\bin\Release\net10.0\Channel.Launcher.exe'
if (-not (Test-Path -LiteralPath $launcher)) { throw 'Build Release first.' }
$action = New-ScheduledTaskAction -Execute $launcher -WorkingDirectory $PSScriptRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $account
$trigger.Delay = 'PT30S'
$principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Description 'Telegram channel .NET editor: local worker, starts after Windows sign-in.' -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
Write-Output 'Autostart installed. Start: Start-ScheduledTask -TaskName DotNetChannel'
Write-Output 'Stop: Stop-ScheduledTask -TaskName DotNetChannel'
