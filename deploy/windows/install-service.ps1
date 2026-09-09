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

    No password is ever passed for that account, and that is a requirement rather than a
    convenience: the SCM demands a NULL password for a virtual account, which is also how
    the MSI channel registers the very same service.

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
#
# sc.exe's own message is kept: an exit code alone does not say whether the SCM refused the
# name, the password, or the service it derives from — and this step failed once in CI with
# nothing but "exit 1057" to go on, which cost a whole release cycle to learn nothing.
$account = "NT SERVICE\$ServiceName"

# The service SID type. This is NOT what makes the account resolvable — installers/msi/
# PackageService.wxs registers the same account through CreateService with no SID-type step
# at all, so the principal exists without it. It is set here because the ZIP channel can, and
# because a service SID present in the token is what keeps the icacls grant below meaningful
# under any future account model. It stays one round longer than its justification: it was
# added on a first reading of 1057 that the MSI path disproves, and removing it in the same
# commit as the real fix would leave the next log unable to say which change mattered.
# `unrestricted` is the minimum; `restricted` is the hardening step, and a behaviour change
# the host has not been tested against.
$sidOutput = & sc.exe sidtype $ServiceName unrestricted
if ($LASTEXITCODE -ne 0) {
    $said = ($sidOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join ' / '
    throw ("sc.exe sidtype failed to set the service SID type (exit $LASTEXITCODE). " +
           "The account swap below has not been attempted. sc.exe said: " +
           "$(if ($said) { $said } else { '(no output)' })")
}

# There is no password argument, and that is the whole fix. CreateService and
# ChangeServiceConfig both spell out the same rule: when the start name is a virtual account,
# lpPassword MUST be NULL. Omitting `password=` is how sc.exe passes NULL. `password= ""`
# passes an EMPTY STRING, which is a different value — the SCM then validates the name down
# the ordinary-account path and answers 1057, "the account name is invalid or does not
# exist", about a name that is perfectly valid. PowerShell compounds it by rendering empty
# native arguments differently across versions, so no spelling of `password= ""` is portable
# here. The MSI channel never met this: its ServiceInstall table leaves the password null.
#
# No 2>&1: sc.exe writes "[SC] ChangeServiceConfig FAILED …" to stdout, and redirecting a
# native stderr into the success stream under $ErrorActionPreference = 'Stop' can throw
# before the check below ever runs — which is the failure mode this block exists to fix.
$scOutput = & sc.exe config $ServiceName obj= $account
if ($LASTEXITCODE -ne 0) {
    $code = $LASTEXITCODE
    $said = ($scOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join ' / '
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    $state = if ($svc) { "exists, status $($svc.Status)" } else { 'DOES NOT EXIST' }
    # Two facts a bare exit code has never carried, both cheap to establish here: whether the
    # LSA resolves the name at all, and what the SID type actually ended up as.
    $resolution = 'not probed'
    try {
        $sid = ([System.Security.Principal.NTAccount] $account).Translate([System.Security.Principal.SecurityIdentifier])
        $resolution = "resolves to $($sid.Value)"
    } catch {
        $resolution = "DOES NOT RESOLVE ($($_.Exception.Message))"
    }
    $qsid = ((& sc.exe qsidtype $ServiceName) | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join ' / '
    $hint = switch ($code) {
        1057 { "1057 is ERROR_INVALID_SERVICE_ACCOUNT: the SCM refused '$account'. No password is passed — a virtual account requires a NULL one — so the name is what to check: its suffix must equal the service name exactly." }
        1072 { '1072 is ERROR_SERVICE_MARKED_FOR_DELETE: a previous instance is still being torn down.' }
        default { '' }
    }
    throw ("sc.exe config failed to set the service account (exit $code). " +
           "Requested account: '$account' — $resolution. Service '$ServiceName': $state. " +
           "sc.exe qsidtype said: $(if ($qsid) { $qsid } else { '(no output)' }). " +
           "sc.exe config said: $(if ($said) { $said } else { '(no output)' }). $hint")
}
# Future hardening (not done here, and not something the MSI channel can express):
# `restricted` adds the write-restricted token, a behaviour change the service has not been
# tested against — the next step, not a free one:
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
