<#
.SYNOPSIS
  Install / run / uninstall smoke for the per-machine Orkeon Service Host MSI
  (WINSVC-02 P3).

.DESCRIPTION
  Installs orkeon-host-<ver>-win-x64.msi silently, then proves the same
  WINSVC-01 contract the ZIP channel proves — with the assertions of
  lib\service-windows.ps1, shared so the two channels cannot drift:

    1. silent install succeeds (0 or 3010), the exe lands at the one canonical
       path (Program Files\Orkeon\libexec\orkeon-host\orkeon-host.exe), the ARP
       entry "Orkeon Service Host" exists;
    2. the service is registered under NT SERVICE\Orkeon, Auto, with a quoted
       ImagePath carrying --settings and --working-dir pointing at
       ProgramData\Orkeon; the data directory exists with Modify for the
       account (no Environment value: the MSI carries no secrets, ever);
    3. with an offline crew (relative path) it starts, stays up, stops;
    4. reinstalling the SAME MSI silently succeeds — the proof that
       FindRelatedProducts is resequenced before LaunchConditions, without
       which every silent upgrade would refuse itself over its own service;
    5. uninstall removes the service, the payload and the ARP entry, and
       leaves ProgramData\Orkeon and its appsettings.json to the operator.

  Requires administrator rights. Compatible with Windows PowerShell 5.1 and
  PowerShell 7.

.PARAMETER MsiPath
  The orkeon-host-<ver>-win-x64.msi to smoke.

.PARAMETER WorkDir
  Scratch directory for logs. Default: a new folder under the temp directory,
  removed on exit.

.PARAMETER Keep
  Keeps the scratch directory.

.EXAMPLE
  .\scripts\smoke-onboarding\run-smoke-service-msi.ps1 -MsiPath .\artifacts\installers\orkeon-host-1.0.0-rc.3-win-x64.msi
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,

    [string]$WorkDir,

    [switch]$Keep
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\service-windows.ps1')

$serviceName = 'Orkeon'
$installRoot = Join-Path $env:ProgramFiles 'Orkeon'
$exePath = Join-Path $installRoot 'libexec\orkeon-host\orkeon-host.exe'
$dataDir = Join-Path $env:ProgramData 'Orkeon'
$fixtures = Join-Path $PSScriptRoot 'fixtures'

if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) ("orkeon-smoke-service-msi-" + [Guid]::NewGuid().ToString('N')) }
$logDir = Join-Path $WorkDir 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$problems = @()

function Add-Result([string]$step, [pscustomobject]$result) {
    foreach ($n in $result.Notes) { Write-Host "  [$step] $n" }
    foreach ($p in $result.Problems) { Write-Host "  [$step] PROBLEM: $p"; $script:problems += "${step}: $p" }
}

function Invoke-Msiexec([string[]]$MsiArgs, [string]$LogName) {
    $log = Join-Path $logDir $LogName
    $proc = Start-Process msiexec.exe -ArgumentList ($MsiArgs + @('/qn', '/l*v', "`"$log`"")) -Wait -PassThru
    return $proc.ExitCode
}

function Get-ServiceHostArpEntry {
    Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } |
        Where-Object { $_.DisplayName -eq 'Orkeon Service Host' } |
        Select-Object -First 1
}

try {
    if ((Test-Path $installRoot) -or (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        throw "This smoke needs a machine without '$installRoot' or a '$serviceName' service. Run it on a disposable runner."
    }
    $MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path

    # -- 1. Silent install -------------------------------------------------------
    Write-Host "== msiexec /i (silent)"
    $code = Invoke-Msiexec @('/i', "`"$MsiPath`"") 'msi-install.log'
    if ($code -ne 0 -and $code -ne 3010) { $problems += "install: msiexec exited $code (see msi-install.log)" }
    if (-not (Test-Path $exePath)) { $problems += "install: $exePath is missing" }
    if (-not (Get-ServiceHostArpEntry)) { $problems += "install: no 'Orkeon Service Host' ARP entry" }
    if (-not (Test-Path $dataDir)) { $problems += "install: $dataDir was not created" }
    if ($problems.Count -gt 0) { throw "Install assertions failed: $($problems -join '; ')" }
    Write-Host "  installed (exe, ARP entry, data dir)"

    # -- 2. Registration assertions (shared with the ZIP channel) ----------------
    Add-Result 'registration' (Invoke-OrkeonServiceRegistrationAssertions `
        -ServiceName $serviceName -ExecutablePath $exePath)
    Add-Result 'acl' (Invoke-OrkeonServiceDataAclAssertions -ServiceName $serviceName -DataDir $dataDir)

    # -- 3. Offline crew: start / stability / stop -------------------------------
    Write-Host "== Start / stability / stop (relative crew path through --working-dir)"
    New-Item -ItemType Directory -Force -Path (Join-Path $dataDir 'crews\smoke') | Out-Null
    Copy-Item (Join-Path $fixtures 'offline-crew.yaml') (Join-Path $dataDir 'crews\smoke\config.yaml')
    Set-Content -LiteralPath (Join-Path $dataDir 'appsettings.json') -Value @'
{ "Orkeon": { "Host": { "Crews": [ { "Name": "smoke", "Path": "crews/smoke" } ] } } }
'@
    Add-Result 'start-stop' (Invoke-OrkeonServiceStartStopAssertions -ServiceName $serviceName -StabilitySeconds 10)

    # -- 4. Silent reinstall of the same MSI (FindRelatedProducts resequencing) --
    Write-Host "== msiexec /i again (silent same-version upgrade must pass its own guard)"
    $code = Invoke-Msiexec @('/i', "`"$MsiPath`"") 'msi-reinstall.log'
    if ($code -ne 0 -and $code -ne 3010) {
        $problems += "reinstall: msiexec exited $code — the ORKEONSERVICEPRESENT guard is blocking our own silent upgrade (see msi-reinstall.log)"
    } else {
        Write-Host "  reinstall passed the guard"
    }

    # -- 5. Uninstall: service + payload + ARP gone, operator data survives ------
    Write-Host "== msiexec /x (silent)"
    $code = Invoke-Msiexec @('/x', "`"$MsiPath`"") 'msi-uninstall.log'
    if ($code -ne 0 -and $code -ne 3010) { $problems += "uninstall: msiexec exited $code" }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { $problems += 'uninstall: service still registered' }
    if (Test-Path $exePath) { $problems += "uninstall: $exePath still present" }
    if (Get-ServiceHostArpEntry) { $problems += "uninstall: ARP entry still present" }
    if (-not (Test-Path (Join-Path $dataDir 'appsettings.json'))) {
        $problems += 'uninstall: ProgramData\Orkeon\appsettings.json was deleted — the operator config must survive'
    }

    if ($problems.Count -gt 0) {
        Write-Host "`nFAIL — $($problems.Count) problem(s):"
        $problems | ForEach-Object { Write-Host "  - $_" }
        exit 1
    }
    Write-Host "`nPASS — the service MSI holds the WINSVC-01 contract."
    exit 0
}
finally {
    # Best-effort teardown so a failed run does not poison the next one.
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        Start-Process msiexec.exe -ArgumentList @('/x', "`"$MsiPath`"", '/qn') -Wait -ErrorAction SilentlyContinue
        sc.exe delete $serviceName 2>$null | Out-Null
    }
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force -LiteralPath $dataDir -ErrorAction SilentlyContinue }
    if (-not $Keep -and (Test-Path $WorkDir)) { Remove-Item -Recurse -Force -LiteralPath $WorkDir -ErrorAction SilentlyContinue }
}
