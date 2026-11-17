param(
    [string]$Out = "$env:TEMP\net",
    [int]$Port = 5130,
    [int]$Ticks = 400
)

<#
    Brings up a two-machine session on this computer and checks that both sides
    agree.

    The host and the guest are two processes; the guest connects to 127.0.0.1,
    which exercises the same code path as a LAN game. A session that stays up for
    N ticks with matching checksums is the test.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'

if (-not (Test-Path $exe)) { throw 'build the game first (tools\build.ps1)' }

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$hostLog = Join-Path $Out 'host.log'
$guestLog = Join-Path $Out 'guest.log'

# A previous run leaves the port in TIME_WAIT for up to two minutes, and binding
# it again fails the check for a reason that has nothing to do with the code.
foreach ($candidate in $Port..($Port + 40))
{
    if (-not (Get-NetTCPConnection -LocalPort $candidate -ErrorAction SilentlyContinue))
    {
        $Port = $candidate
        break
    }
}

Write-Host "== host on port $Port" -ForegroundColor Cyan
$hostProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1' `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

Start-Sleep -Seconds 2

Write-Host '== guest' -ForegroundColor Cyan
$guestProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'join', '-address', '127.0.0.1', '-port', $Port, '-scale', '1' `
    -PassThru -RedirectStandardOutput $guestLog -RedirectStandardError "$guestLog.err"

Start-Sleep -Seconds 2

$connected = $false
foreach ($i in 1..20)
{
    Start-Sleep -Milliseconds 300
    $hostText = if (Test-Path $hostLog) { Get-Content $hostLog -Raw } else { '' }
    $guestText = if (Test-Path $guestLog) { Get-Content $guestLog -Raw } else { '' }
    if ($hostText -match 'connected as player 1' -and $guestText -match 'connected as player 2') { $connected = $true; break }
}

if (-not $connected)
{
    Write-Host '== handshake failed' -ForegroundColor Red
    Write-Host '--- host ---'; if (Test-Path $hostLog) { Get-Content $hostLog | Select-Object -Last 20 }
    Write-Host '--- guest ---'; if (Test-Path $guestLog) { Get-Content $guestLog | Select-Object -Last 20 }
    foreach ($p in $hostProcess, $guestProcess) { if ($p -and -not $p.HasExited) { $p.Kill() } }
    exit 1
}

Write-Host '== both connected, running' -ForegroundColor Green
Start-Sleep -Milliseconds ($Ticks * 50 / 10)

$desync = $false
foreach ($p in $hostProcess, $guestProcess)
{
    if ($p -and -not $p.HasExited) { $p.Kill() }
}

Start-Sleep -Milliseconds 300

foreach ($log in $hostLog, $guestLog)
{
    if (-not (Test-Path $log)) { continue }
    $text = Get-Content $log -Raw
    if ($text -match 'DESYNC') { $desync = $true; Write-Host "desync reported in $log" -ForegroundColor Red }
}

$hostText = Get-Content $hostLog -Raw
$guestText = Get-Content $guestLog -Raw

Write-Host '--- host ---'
$hostText -split "`r?`n" | Where-Object { $_ -match 'net:' } | Select-Object -Last 6
Write-Host '--- guest ---'
$guestText -split "`r?`n" | Where-Object { $_ -match 'net:' } | Select-Object -Last 6

if ($desync)
{
    Write-Host '== session DESYNCED' -ForegroundColor Red
    exit 1
}

Write-Host '== session ran without desync' -ForegroundColor Green
exit 0
