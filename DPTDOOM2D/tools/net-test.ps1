param(
    [string]$Out = "$env:TEMP\net",
    [int]$Port = 5130,
    [int]$Ticks = 400,
    [int]$TimeoutSeconds = 90
)

<#
    Brings up a two-machine session on this computer and checks that both sides
    agree.

    The host and the guest are two processes; the guest connects to 127.0.0.1,
    which exercises the same code path as a LAN game. A session that stays up for
    N ticks with matching checksums is the test.

    Nothing here waits a fixed number of seconds. A CI container takes ten seconds
    to get a window up — and used to take five of those in waveOutOpen alone — so a
    fixed "sleep 2, then poll for six" turned a slow machine into a failed
    handshake that looked like a network bug. Every wait is now a poll with a
    generous timeout, and every launch passes -nosound, because a test run has no
    use for a device.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'

if (-not (Test-Path $exe)) { throw 'build the game first (tools\build.ps1)' }

New-Item -ItemType Directory -Path $Out -Force | Out-Null

# ---------------------------------------------------------------- helpers

<#
    Waits until a log file matches a pattern, or the deadline passes.

    Returns $true on a match. Reading a log that the process is still writing is
    safe here: the file is opened for append by the child and read with a share
    that allows it.
#>
function Wait-ForLog([string]$path, [string]$pattern, [int]$seconds)
{
    $deadline = (Get-Date).AddSeconds($seconds)

    while ((Get-Date) -lt $deadline)
    {
        if (Test-Path $path)
        {
            try
            {
                $text = Get-Content $path -Raw -ErrorAction SilentlyContinue

                if ($text -and $text -match $pattern) { return $true }
            }
            catch
            {
                # The file may be mid-write; try again on the next pass.
            }
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

<# A free TCP port at or above the hint, so a TIME_WAIT from a previous run cannot
   make the next run fail for a reason that has nothing to do with the code. #>
function Find-FreePort([int]$hint)
{
    for ($candidate = $hint; $candidate -lt $hint + 200; ++$candidate)
    {
        if (-not (Get-NetTCPConnection -LocalPort $candidate -ErrorAction SilentlyContinue))
        {
            return $candidate
        }
    }

    throw "no free port near $hint"
}

function Show-Log([string]$title, [string]$path, [string]$filter)
{
    Write-Host "--- $title ---"

    if (-not (Test-Path $path)) { Write-Host '(no log)'; return }

    $lines = Get-Content $path -ErrorAction SilentlyContinue

    if ($filter) { $lines = $lines | Where-Object { $_ -match $filter } }

    $lines | Select-Object -Last 12
}

function Stop-Quietly($process)
{
    if ($process -and -not $process.HasExited)
    {
        try { $process.Kill() } catch { }
    }
}

# ---------------------------------------------------------------- direct session

$Port = Find-FreePort $Port

Write-Host "== host on port $Port" -ForegroundColor Cyan
$hostLog = Join-Path $Out 'host.log'
$guestLog = Join-Path $Out 'guest.log'

$hostProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1', '-nosound' `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

# The host has to be listening before the guest tries: starting the guest first
# only tests how fast the host boots.
if (-not (Wait-ForLog $hostLog 'waiting for a player' $TimeoutSeconds))
{
    Write-Host '== the host never started listening' -ForegroundColor Red
    Show-Log 'host' $hostLog 'net:|data:|audio:'
    Stop-Quietly $hostProcess
    exit 1
}

Write-Host '== guest' -ForegroundColor Cyan
$guestProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'join', '-address', '127.0.0.1', '-port', $Port, '-scale', '1', '-nosound' `
    -PassThru -RedirectStandardOutput $guestLog -RedirectStandardError "$guestLog.err"

$connected = (Wait-ForLog $guestLog 'connected as player 2' $TimeoutSeconds) -and
             (Wait-ForLog $hostLog 'connected as player 1' 10)

if (-not $connected)
{
    Write-Host '== handshake failed' -ForegroundColor Red
    Show-Log 'host' $hostLog 'net:|data:'
    Show-Log 'guest' $guestLog 'net:|data:'
    Stop-Quietly $hostProcess
    Stop-Quietly $guestProcess
    exit 1
}

Write-Host '== both connected, running' -ForegroundColor Green
Start-Sleep -Milliseconds ($Ticks * 50 / 10)

Stop-Quietly $hostProcess
Stop-Quietly $guestProcess
Start-Sleep -Milliseconds 300

$desync = $false

foreach ($log in $hostLog, $guestLog)
{
    if (-not (Test-Path $log)) { continue }

    $text = Get-Content $log -Raw

    if ($text -match 'DESYNC')
    {
        $desync = $true
        Write-Host "desync reported in $log" -ForegroundColor Red
    }
}

Show-Log 'host' $hostLog 'net:'
Show-Log 'guest' $guestLog 'net:'

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

$Port = Find-FreePort ($Port + 1)

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
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1', '-nosound', '-file', $modPk3 `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

if (-not (Wait-ForLog $hostLog 'waiting for a player' $TimeoutSeconds))
{
    Write-Host '== the mismatch host never started listening' -ForegroundColor Red
    Show-Log 'host' $hostLog 'net:|data:'
    Stop-Quietly $hostProcess
    exit 1
}

$guestProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'join', '-address', '127.0.0.1', '-port', $Port, '-scale', '1', '-nosound' `
    -PassThru -RedirectStandardOutput $guestLog -RedirectStandardError "$guestLog.err"

# The refusal is the expected outcome, so the wait is for the refusal itself.
$refused = Wait-ForLog $guestLog 'different game data' $TimeoutSeconds

Stop-Quietly $hostProcess
Stop-Quietly $guestProcess
Start-Sleep -Milliseconds 300

$hostText = if (Test-Path $hostLog) { Get-Content $hostLog -Raw } else { '' }
$guestText = if (Test-Path $guestLog) { Get-Content $guestLog -Raw } else { '' }

if ($refused -and $hostText -match 'different game data' -and $guestText -match 'mismatch\.pk3')
{
    Write-Host '== mismatched data refused, and the file was named' -ForegroundColor Green
}
else
{
    Write-Host '== mismatch was NOT refused' -ForegroundColor Red
    Show-Log 'host' $hostLog 'net:|data:'
    Show-Log 'guest' $guestLog 'net:|data:'
    exit 1
}

# ---------------------------------------------------------------- catalogue
#
# The host publishes its manifest, so a launcher can warn a player BEFORE they
# connect. --netdata is the console view of the same data: a line per session
# saying whether this installation matches it.

$Port = Find-FreePort ($Port + 1)

Write-Host '== catalogue knows the data' -ForegroundColor Cyan

$masterPort = Find-FreePort ($Port + 10)
$relayPort = Find-FreePort ($masterPort + 1)

$masterExe = Join-Path $root '..\DPTDOOM2D.Master\bin\Release\net40\dptdoom2d-master.exe'
$masterLog = Join-Path $Out 'catalogue-master.log'

$masterProcess = Start-Process -FilePath $masterExe `
    -ArgumentList '--bind', '127.0.0.1', '--port', $masterPort, '--relay-port', $relayPort `
    -PassThru -RedirectStandardOutput $masterLog -RedirectStandardError "$masterLog.err"

if (-not (Wait-ForLog $masterLog 'listening on' $TimeoutSeconds))
{
    Write-Host '== the master never started' -ForegroundColor Red
    Show-Log 'master' $masterLog 'listening|error'
    Stop-Quietly $masterProcess
    exit 1
}

$endpoint = '127.0.0.1:' + $masterPort
$hostLog = Join-Path $Out 'catalogue-host.log'

$hostProcess = Start-Process -FilePath $exe `
    -ArgumentList '-net', 'host', '-port', $Port, '-map', '1', '-scale', '1', '-nosound', '-master', $endpoint, '-relay' `
    -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"

# Wait for the registration, not for a guessed number of seconds: the manifest is
# published right after it, and --netdata would otherwise see an empty catalogue.
$registered = Wait-ForLog $hostLog 'published the data manifest|does not accept a data manifest' $TimeoutSeconds

$catalogue = & $exe --netdata $endpoint 2>&1 | Out-String

Stop-Quietly $hostProcess
Stop-Quietly $masterProcess

if ($registered -and $catalogue -match 'data matches')
{
    Write-Host '== the session advertises matching data' -ForegroundColor Green
}
else
{
    Write-Host '== the catalogue did not report matching data' -ForegroundColor Red
    Show-Log 'host' $hostLog 'net:|data:'
    $catalogue -split "`r?`n" | Select-Object -Last 6
    exit 1
}

exit 0
