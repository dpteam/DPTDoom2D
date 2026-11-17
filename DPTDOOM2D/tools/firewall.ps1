<#
.SYNOPSIS
    Opens the Windows Firewall for DPT.DOOM 2D's network ports.

.DESCRIPTION
    Creates inbound rules so another machine can reach this one:

        master   25145/tcp   session catalogue (master server)
        relay    25146/tcp   relay for players behind NAT
        game     5041/tcp    direct host: guest connects here

    The numbers are deliberately **different from DPT.DOOM's** (25135 / 25136 /
    5029), so both games can run on one machine and a router forward can point at
    the right one without guessing.

    Nothing here forwards ports on the router: that is the router's job. This only
    tells Windows' own firewall to let the traffic through.

    Run it from an elevated prompt, or let it relaunch itself: the script asks
    Windows for elevation and you confirm the UAC prompt.

.PARAMETER Remove
    Delete the rules instead of creating them.

.PARAMETER Status
    Show whether the rules exist, without changing anything.

.PARAMETER Port
    Override the game port. The master and relay ports are fixed, because a
    client finds them by number.

.EXAMPLE
    ./tools/firewall.ps1
    Adds all three rules (UAC prompt).

.EXAMPLE
    ./tools/firewall.ps1 -Status
    Shows the current state, no elevation needed.
#>
[CmdletBinding()]
param(
    [switch]$Remove,
    [switch]$Status,
    [int]$Port = 5041
)

$ErrorActionPreference = 'Stop'

# Consolas shows Cyrillic correctly only when the console's output encoding says
# so; without this the rules print as question marks on a Russian Windows.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$MasterPort = 25145
$RelayPort = 25146

# One prefix for every rule, so -Remove finds exactly ours and nothing else.
$rulePrefix = 'DPT.DOOM 2D'

$rules = @(
    @{ Name = "$rulePrefix - master"; Port = $MasterPort; What = 'каталог сессий' },
    @{ Name = "$rulePrefix - relay";  Port = $RelayPort;  What = 'релей для NAT' },
    @{ Name = "$rulePrefix - game";   Port = $Port;       What = 'прямая игра' }
)

# ---------------------------------------------------------------- elevation

function Test-Elevated
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $Status -and -not (Test-Elevated))
{
    Write-Host 'Нужны права администратора — открываю новое окно и прошу подтверждение.' -ForegroundColor Yellow

    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Remove) { $arguments += '-Remove' }
    if ($Port -ne 5041) { $arguments += @('-Port', $Port) }

    try
    {
        Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait
    }
    catch
    {
        Write-Host 'Отказано в повышении прав. Запусти скрипт из консоли администратора.' -ForegroundColor Red
        exit 1
    }

    exit 0
}

# ---------------------------------------------------------------- status

function Show-Status
{
    Write-Host 'Правила брандмауэра:' -ForegroundColor Cyan

    foreach ($rule in $rules)
    {
        $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue

        if ($existing)
        {
            $enabled = ($existing | Get-NetFirewallPortFilter | Select-Object -First 1)
            Write-Host ("  {0,-28} {1,5}/tcp   есть ({2})" -f $rule.Name, $rule.Port, $enabled.Protocol) -ForegroundColor Green
        }
        else
        {
            Write-Host ("  {0,-28} {1,5}/tcp   нет" -f $rule.Name, $rule.Port) -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    Write-Host "Порты DPT.DOOM (другой проект) — 25135 / 25136 / 5029: не пересекаются." -ForegroundColor DarkGray
    Write-Host "Проброс на роутере — отдельно: снаружи нужен только $MasterPort для каталога"
    Write-Host "и $RelayPort для релея. Игровой порт $Port нужен только при прямой игре."
}

if ($Status)
{
    Show-Status
    exit 0
}

# ---------------------------------------------------------------- apply

try
{
    foreach ($rule in $rules)
    {
        # Remove first: New-NetFirewallRule would otherwise add a duplicate with
        # the same name every time the script runs.
        Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue

        if ($Remove)
        {
            Write-Host ("  убрано: {0} ({1}/tcp)" -f $rule.Name, $rule.Port) -ForegroundColor Yellow
            continue
        }

        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Description ("DPT.DOOM 2D: " + $rule.What) `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $rule.Port `
            -Profile Any `
            -Enabled True | Out-Null

        Write-Host ("  открыто: {0} ({1}/tcp, {2})" -f $rule.Name, $rule.Port, $rule.What) -ForegroundColor Green
    }
}
catch
{
    Write-Host ('Не удалось изменить правила: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Проверь, что служба «Брандмауэр Защитника Windows» запущена.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Show-Status

if (-not $Remove)
{
    Write-Host ''
    Write-Host 'Дальше — на роутере:' -ForegroundColor Cyan
    Write-Host "  пробросить $MasterPort/tcp на этот компьютер (каталог сессий)"
    Write-Host "  пробросить $RelayPort/tcp на этот компьютер (релей, если играете через NAT)"
    Write-Host "  пробросить $Port/tcp, только если хостишь напрямую, без релея"
    Write-Host ''
    Write-Host "После проброса мастер будет виден как play.dartpower.xyz:$MasterPort" -ForegroundColor Green
    Write-Host "Гостю в лаунчере: Мастер = play.dartpower.xyz:$MasterPort" -ForegroundColor Green
    Write-Host 'Список сессий подтянется кнопкой «Обновить список».'
}
