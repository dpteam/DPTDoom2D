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

# ---------------------------------------------------------------- different data
#
# The other half of the contract: when the data does NOT match, the session must
# be refused before the first tick, and the refusal has to name the file. A guest
# with a mod the host does not have is the everyday case.

foreach ($candidate in $Port..($Port + 40))
{
    if (-not (Get-NetTCPConnection -LocalPort $candidate -ErrorAction SilentlyContinue))
    {
        $Port = $candidate
        break
    }
}

$modSrc = Join-Path $Out 'mismatch'
Remove-Item $modSrc -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $modSrc -Force | Out-Null
Set-Content (Join-Path $modSrc 'actors.txt') 'actor Imp { health 40 }' -Encoding ASCII

$modPk3 = Join-Path $Out 'mismatch.pk3'
Remove-Item $modPk3 -Force -ErrorAction SilentlyContinue
& $exe --modbuild $modPk3 $modSrc 2>&1 | Out-Null
if (-not (Test-Path $modPk3)) { Write-Host '== could not build the mismatch mod' -ForegroundColor Red; exit 1 }

Write-Host '== different data on purpose' -ForegroundColor Cyan
$hostLog = Join-Path $Out 'mismatch-host.log'
$guestLog = Join-Path $Out 'mismatch-guest.log'

$hostProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1', '-file', $modPk3 `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

Start-Sleep -Seconds 2

$guestProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'join', '-address', '127.0.0.1', '-port', $Port, '-scale', '1' `
    -PassThru -RedirectStandardOutput $guestLog -RedirectStandardError "$guestLog.err"

Start-Sleep -Seconds 3

foreach ($p in $hostProcess, $guestProcess) { if ($p -and -not $p.HasExited) { $p.Kill() } }
Start-Sleep -Milliseconds 300

$hostText = if (Test-Path $hostLog) { Get-Content $hostLog -Raw } else { '' }
$guestText = if (Test-Path $guestLog) { Get-Content $guestLog -Raw } else { '' }

if ($hostText -match 'different game data' -and $guestText -match 'mismatch\.pk3')
{
    Write-Host '== mismatched data refused, and the file was named' -ForegroundColor Green
}
else
{
    Write-Host '== mismatch was NOT refused' -ForegroundColor Red
    Write-Host '--- host ---'; $hostText -split "?
" | Where-Object { $_ -match 'net:|data:' } | Select-Object -Last 5
    Write-Host '--- guest ---'; $guestText -split "?
" | Where-Object { $_ -match 'net:|data:' } | Select-Object -Last 5
    exit 1
}

# ---------------------------------------------------------------- catalogue
#
# The host publishes its manifest, so a launcher can warn a player BEFORE they
# connect. --netdata is the console view of the same data: a line per session
# saying whether this installation matches it.

foreach ($candidate in $Port..($Port + 40))
{
    if (-not (Get-NetTCPConnection -LocalPort $candidate -ErrorAction SilentlyContinue))
    {
        $Port = $candidate
        break
    }
}

Write-Host '== catalogue knows the data' -ForegroundColor Cyan

$masterPort = $Port + 10
while (Get-NetTCPConnection -LocalPort $masterPort -ErrorAction SilentlyContinue) { $masterPort += 10 }
$relayPort = $masterPort + 1

$masterExe = Join-Path $root '..\DPTDOOM2D.Master\bin\Release\net40\dptdoom2d-master.exe'
$masterLog = Join-Path $Out 'catalogue-master.log'

$masterProcess = Start-Process -FilePath $masterExe `
    -ArgumentList '--bind', '127.0.0.1', '--port', $masterPort, '--relay-port', $relayPort `
    -PassThru -RedirectStandardOutput $masterLog -RedirectStandardError "$masterLog.err"

Start-Sleep -Seconds 2

$endpoint = '127.0.0.1:' + $masterPort
$hostLog = Join-Path $Out 'catalogue-host.log'

$hostProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1', '-master', $endpoint, '-relay' `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

Start-Sleep -Seconds 4

$catalogue = & $exe --netdata $endpoint 2>&1 | Out-String

foreach ($p in $hostProcess, $masterProcess) { if ($p -and -not $p.HasExited) { $p.Kill() } }

if ($catalogue -match 'data matches')
{
    Write-Host '== the session advertises matching data' -ForegroundColor Green
}
else
{
    Write-Host '== the catalogue did not report matching data' -ForegroundColor Red
    $catalogue -split "?
" | Select-Object -Last 6
    exit 1
}

exit 0
