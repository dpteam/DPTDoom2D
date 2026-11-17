param(
    [string]$Out = "$env:TEMP\netrelay",
    [int]$MasterPort = 25140,
    [int]$RelayPort = 25141,
    [int]$GamePort = 5150
)

<#
    End-to-end relay test: master + relay + host + guest.

    The host registers on the master and waits on the relay; the guest joins by
    session id, exactly as a player behind NAT would. Success means both sides
    connected and the simulation stayed in sync.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$game = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'
$master = Join-Path $root '..\DPTDOOM2D.Master\bin\Release\net40\dptdoom2d-master.exe'

if (-not (Test-Path $game)) { throw 'build the game first' }
if (-not (Test-Path $master)) { throw 'build the master first (dotnet build DPTDOOM2D.slnx)' }

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$masterLog = Join-Path $Out 'master.log'
$hostLog = Join-Path $Out 'host.log'
$guestLog = Join-Path $Out 'guest.log'

$processes = @()

function Stop-All
{
    foreach ($p in $processes) { if ($p -and -not $p.HasExited) { try { $p.Kill() } catch { } } }
}

<#
    Reuses a port only once the previous run's sockets have left TIME_WAIT.

    A failed run leaves the listener's port in TIME_WAIT for up to two minutes on
    Windows, and the next run then binds the master but never gets a relay
    connection - the check reported "the host did not answer" when the real
    problem was the previous run. Asking for another port is simpler and faster
    than waiting for the kernel.
#>
function Get-FreePort([int]$start)
{
    for ($port = $start; $port -lt $start + 40; ++$port)
    {
        $inUse = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
        if (-not $inUse) { return $port }
    }

    return $start
}

$MasterPort = Get-FreePort $MasterPort
$RelayPort = Get-FreePort ($MasterPort + 1)
$GamePort = Get-FreePort $GamePort

try
{
    Write-Host "== master on $MasterPort (relay $RelayPort)" -ForegroundColor Cyan
    $processes += Start-Process -FilePath $master `
        -ArgumentList '--port', $MasterPort, '--relay-port', $RelayPort `
        -PassThru -RedirectStandardOutput $masterLog -RedirectStandardError "$masterLog.err"

    Start-Sleep -Seconds 2

    Write-Host '== host registering through the master' -ForegroundColor Cyan
    $hostProcess = Start-Process -FilePath $game `
        -ArgumentList '-net', 'host', '-port', $GamePort, '-map', '1', '-scale', '1',
                      '-master', "127.0.0.1:$MasterPort", '-relay' `
        -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"
    $processes += $hostProcess

    # Wait for the host to publish its session id.
    $sessionId = $null
    foreach ($i in 1..30)
    {
        Start-Sleep -Milliseconds 300
        if (Test-Path $hostLog)
        {
            $text = Get-Content $hostLog -Raw
            if ($text -match 'session id ([0-9a-f]{32})') { $sessionId = $Matches[1]; break }
        }
        if ($hostProcess.HasExited) { break }
    }

    if (-not $sessionId)
    {
        Write-Host '== the host never registered' -ForegroundColor Red
        Write-Host '--- host ---'; if (Test-Path $hostLog) { Get-Content $hostLog | Select-Object -Last 20 }
        Write-Host '--- master ---'; if (Test-Path $masterLog) { Get-Content $masterLog | Select-Object -Last 20 }
        Stop-All
        exit 1
    }

    Write-Host "== session id $sessionId" -ForegroundColor Green
    Write-Host '== guest joining through the relay' -ForegroundColor Cyan

    $guestProcess = Start-Process -FilePath $game `
        -ArgumentList '-net', 'join', '-master', "127.0.0.1:$MasterPort", '-relay-session', $sessionId,
                      '-scale', '1' `
        -PassThru -RedirectStandardOutput $guestLog -RedirectStandardError "$guestLog.err"
    $processes += $guestProcess

    $connected = $false
    foreach ($i in 1..30)
    {
        Start-Sleep -Milliseconds 300
        $hostText = if (Test-Path $hostLog) { Get-Content $hostLog -Raw } else { '' }
        $guestText = if (Test-Path $guestLog) { Get-Content $guestLog -Raw } else { '' }
        if ($hostText -match 'connected as player 1' -and $guestText -match 'connected as player 2')
        {
            $connected = $true
            break
        }
        if ($guestProcess.HasExited) { break }
    }

    if (-not $connected)
    {
        Write-Host '== relay handshake failed' -ForegroundColor Red
        Write-Host '--- host ---'; if (Test-Path $hostLog) { Get-Content $hostLog | Select-Object -Last 20 }
        Write-Host '--- guest ---'; if (Test-Path $guestLog) { Get-Content $guestLog | Select-Object -Last 20 }
        Stop-All
        exit 1
    }

    Write-Host '== relayed session running' -ForegroundColor Green
    Start-Sleep -Seconds 5

    Stop-All
    Start-Sleep -Milliseconds 500

    $desync = $false
    foreach ($log in $hostLog, $guestLog)
    {
        if ((Test-Path $log) -and (Get-Content $log -Raw) -match 'DESYNC') { $desync = $true }
    }

    if ($desync)
    {
        Write-Host '== session DESYNCED through the relay' -ForegroundColor Red
        exit 1
    }

    Write-Host '== relay session ran without desync' -ForegroundColor Green
    exit 0
}
finally
{
    Stop-All
}
