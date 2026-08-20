<#
.SYNOPSIS
    Registers the Orkeon service host with the Windows Service Control Manager (GATE-05).

.DESCRIPTION
    The same binary that runs in a terminal runs as a service: `UseWindowsService()` is inert
    outside the SCM, so nothing is built differently. This script only registers it.

    It deliberately does not take a bot token or an API key. Secrets are named by environment
    variable in the configuration and set on the machine or the service account, so this script
    can be read, copied and committed without leaking anything.

.PARAMETER ExecutablePath
    Full path to orkeon-host.exe.

.PARAMETER SettingsPath
    Full path to the appsettings.json the host reads.

.PARAMETER ServiceName
    Service name. Defaults to Orkeon, which is what the host reports to the SCM.

.EXAMPLE
    .\install-service.ps1 -ExecutablePath C:\Orkeon\orkeon-host.exe -SettingsPath C:\Orkeon\appsettings.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExecutablePath,
    [Parameter(Mandatory = $true)][string] $SettingsPath,
    [string] $ServiceName = 'Orkeon'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Executable not found: $ExecutablePath"
}

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    # Registering a service that will fail on its first start is worse than refusing now: the
    # failure would land in the event log, where nobody is looking yet.
    throw "Settings file not found: $SettingsPath"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binaryPath = '"{0}" --settings "{1}"' -f $ExecutablePath, $SettingsPath

New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -DisplayName 'Orkeon service host' `
    -Description 'Hosts Orkeon crews and the chat gateway.' `
    -StartupType Automatic | Out-Null

# Restart on failure, twice, then leave it alone: a host that keeps failing is telling the
# operator something, and an infinite restart loop buries the message.
sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/none/0 | Out-Null

Write-Host "Registered '$ServiceName'. Start it with: Start-Service -Name $ServiceName"
Write-Host "Secrets go in the machine or service-account environment, named by the configuration."
