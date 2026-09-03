<#
.SYNOPSIS
    Registers the Orkeon service host with the Windows Service Control Manager (GATE-05/WINSVC-02).

.DESCRIPTION
    The same binary that runs in a terminal runs as a service: `UseWindowsService()` is inert
    outside the SCM, so nothing is built differently. This script registers it under the
    virtual account `NT SERVICE\<ServiceName>` (no password to manage, its own SID), grants
    that account Modify on the working directory, and points the service at its settings and
    working directory. Defaults mirror the systemd unit: binaries under Program Files, the
    configuration and state under ProgramData.

    Secrets never appear on the registration command line and are never baked into a package.
    Pass them with -EnvironmentSecrets: they land in the service's own Environment value
    (REG_MULTI_SZ under the service key), which only the SCM reads at start and only
    administrators can open — the closest Windows mirror of systemd's EnvironmentFile.
    Restart the service after changing them, same contract as systemd.

.PARAMETER ExecutablePath
    Full path to orkeon-host.exe. Point it at the real executable under libexec\, never at
    the bin\orkeon-host.cmd terminal wrapper — a .cmd cannot be registered with the SCM.

.PARAMETER SettingsPath
    Full path to the appsettings.json the host reads.

.PARAMETER WorkingDirectory
    Directory the service starts in (passed as --working-dir). Relative paths in the settings
    file resolve against it. Created if missing; the service account gets Modify on it.

.PARAMETER EnvironmentSecrets
    Hashtable of NAME = value pairs written to the service's Environment value (REG_MULTI_SZ).
    Names are the environment variables your configuration references (e.g. ORKEON_DISCORD_TOKEN).

.PARAMETER ServiceName
    Service name. Defaults to Orkeon, which is what the host reports to the SCM. The virtual
    account is derived from it (NT SERVICE\<ServiceName>).

.PARAMETER Uninstall
    Stops and deletes the service, then exits. Leaves the working directory, the settings
    file and the event-log source in place — the configuration belongs to the operator,
    mirror of /etc/orkeon surviving a package removal.

.EXAMPLE
    .\install-service.ps1
    # Registers with the defaults: exe under Program Files\Orkeon\libexec\orkeon-host,
    # settings and working directory under ProgramData\Orkeon.

.EXAMPLE
    .\install-service.ps1 -EnvironmentSecrets @{ ORKEON_DISCORD_TOKEN = '...' }
    Start-Service -Name Orkeon

.EXAMPLE
    .\install-service.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string] $ExecutablePath = "$env:ProgramFiles\Orkeon\libexec\orkeon-host\orkeon-host.exe",
    [string] $SettingsPath = "$env:ProgramData\Orkeon\appsettings.json",
    [string] $WorkingDirectory = "$env:ProgramData\Orkeon",
    [hashtable] $EnvironmentSecrets,
    [string] $ServiceName = 'Orkeon',
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service '$ServiceName' removed. '$WorkingDirectory' and its settings are untouched."
    } else {
        Write-Host "Service '$ServiceName' is not registered; nothing to remove."
    }
    return
}

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

New-Item -ItemType Directory -Force -Path $WorkingDirectory | Out-Null

$binaryPath = '"{0}" --settings "{1}" --working-dir "{2}"' -f $ExecutablePath, $SettingsPath, $WorkingDirectory

New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -DisplayName 'Orkeon service host' `
    -Description 'Hosts Orkeon crews and the chat gateway.' `
    -StartupType Automatic | Out-Null

# Everything below only exists once the service does: the account swap needs the service,
# the Environment value lives under the service key, and the ACL targets the account.

# Virtual account (mirror of the unit's User=orkeon). New-Service cannot do this: it passes
# an empty credential where CreateService wants NULL, so the swap goes through sc.exe.
sc.exe config $ServiceName obj= "NT SERVICE\$ServiceName" password= "" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed to set the service account (exit $LASTEXITCODE)." }
# Future hardening (not done here, and not something the MSI channel can express):
#   sc.exe sidtype $ServiceName restricted

# The account needs to write its state where it starts; Program Files stays read-only for it
# (Users already read there — the mirror of ProtectSystem=strict needs no extra ACE).
icacls $WorkingDirectory /grant "NT SERVICE\${ServiceName}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed to grant the service account on '$WorkingDirectory' (exit $LASTEXITCODE)." }

# Event-log source, created now by the administrator: the virtual account may not create it
# on first write, and a configuration refusal is reported there (source 'Orkeon').
if (-not [System.Diagnostics.EventLog]::SourceExists('Orkeon')) {
    [System.Diagnostics.EventLog]::CreateEventSource('Orkeon', 'Application')
}

if ($EnvironmentSecrets -and $EnvironmentSecrets.Count -gt 0) {
    $pairs = @($EnvironmentSecrets.Keys | Sort-Object | ForEach-Object { '{0}={1}' -f $_, $EnvironmentSecrets[$_] })
    New-ItemProperty `
        -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" `
        -Name Environment -PropertyType MultiString -Value $pairs -Force | Out-Null
}

# Restart on failure, twice, then leave it alone: a host that keeps failing is telling the
# operator something, and an infinite restart loop buries the message. No failureflag — the
# SCM cannot filter exit codes, and a refused configuration ends in an orderly stop anyway.
sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/none/0 | Out-Null

Write-Host "Registered '$ServiceName' as NT SERVICE\$ServiceName. Start it with: Start-Service -Name $ServiceName"
Write-Host "Secrets live in the service's Environment value (-EnvironmentSecrets); restart the service after changing them."
