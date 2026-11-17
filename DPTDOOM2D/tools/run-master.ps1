<#
.SYNOPSIS
    Runs the DPT.DOOM 2D master server and relay so the internet can reach them.

.DESCRIPTION
    The master is a server: by default it listens on 127.0.0.1, which is safe but
    unreachable from outside. This script starts it on 0.0.0.0 with the project's
    ports, so a router forward and the Windows Firewall rule are the only two
    other things needed.

        master   25145/tcp
        relay    25146/tcp

    They are different from DPT.DOOM's 25135 / 25136 on purpose: both servers can
    run on one machine and a router forward points at the right one.

.PARAMETER Foreground
    Keep the console attached (default). Without it the server is started in its
    own window so the prompt comes back.

.PARAMETER Firewall
    Also add the Windows Firewall rules (asks for elevation).

.PARAMETER Log
    Where to write the master's output when running in the background.

.EXAMPLE
    ./tools/run-master.ps1
    Runs the master in this console on 0.0.0.0:25145.

.EXAMPLE
    ./tools/run-master.ps1 -Firewall
    Opens the firewall first (UAC), then runs the master.
#>
[CmdletBinding()]
param(
    [switch]$Foreground,
    [switch]$Firewall,
    [string]$Log
)

$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $PSScriptRoot
$master = Join-Path $root '..\DPTDOOM2D.Master\bin\Release\net40\dptdoom2d-master.exe'

if (-not (Test-Path $master))
{
    $master = Join-Path $root '..\DPTDOOM2D.Master\bin\Debug\net40\dptdoom2d-master.exe'
}

if (-not (Test-Path $master))
{
    Write-Host 'Мастер не собран. Сначала:' -ForegroundColor Red
    Write-Host '  dotnet build DPTDOOM2D.slnx -c Release'
    exit 1
}

$MasterPort = 25145
$RelayPort = 25146

# Refuse to start on a busy port: the master would exit with a socket error and
# the reason would be three lines up in a window that has already closed.
foreach ($check in @($MasterPort, $RelayPort))
{
    $inUse = Get-NetTCPConnection -LocalPort $check -State Listen -ErrorAction SilentlyContinue
    if ($inUse)
    {
        Write-Host "Порт $check уже занят (pid $($inUse[0].OwningProcess))." -ForegroundColor Red
        Write-Host 'Останови старый мастер или запусти этот на другом порту: --port / --relay-port.'
        exit 1
    }
}

if ($Firewall)
{
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'firewall.ps1')
}

$arguments = @('--bind', '0.0.0.0', '--port', $MasterPort, '--relay-port', $RelayPort)

Write-Host ''
Write-Host 'DPT.DOOM 2D master' -ForegroundColor Cyan
Write-Host "  каталог  0.0.0.0:$MasterPort" -ForegroundColor Green
Write-Host "  релей    0.0.0.0:$RelayPort" -ForegroundColor Green
Write-Host ''
Write-Host 'Снаружи это должно быть видно как:' -ForegroundColor Cyan
Write-Host "  play.dartpower.xyz:$MasterPort" -ForegroundColor Yellow
Write-Host ''
Write-Host 'Что нужно, чтобы это заработало:' -ForegroundColor Cyan
Write-Host "  1. правила брандмауэра:  ./tools/firewall.ps1"
Write-Host "  2. проброс на роутере:   $MasterPort/tcp и $RelayPort/tcp -> этот компьютер"
Write-Host "  3. DNS: play.dartpower.xyz -> внешний IP"
Write-Host ''
Write-Host 'Проверить снаружи:' -ForegroundColor Cyan
Write-Host "  dptdoom2d.exe -net join -master play.dartpower.xyz:$MasterPort -relay-session <id>"
Write-Host ''

if ($Foreground -or -not $Log)
{
    & $master @arguments
    exit $LASTEXITCODE
}

Write-Host "Запускаю в отдельном окне, лог: $Log" -ForegroundColor DarkGray
Start-Process -FilePath $master -ArgumentList $arguments `
    -RedirectStandardOutput $Log -RedirectStandardError "$Log.err"
