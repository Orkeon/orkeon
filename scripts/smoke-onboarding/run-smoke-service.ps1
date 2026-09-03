<#
.SYNOPSIS
  Released-artefact smoke for the Windows service channel of the full archive (WINSVC-02).

.DESCRIPTION
  Extracts orkeon-<ver>-win-x64.zip (the FULL archive — the CLI zip has no service
  host), lays it out under Program Files exactly as an administrator following
  docs/architecture/service-host.md would, registers the service with the bundled
  deploy\windows\install-service.ps1, and proves the whole WINSVC-01 contract on a
  real SCM:

    1. the archive carries the daemon and its deployment assets —
       libexec\orkeon-host\orkeon-host.exe, deploy\windows\install-service.ps1,
       and the bin\orkeon-host.cmd terminal wrapper (which is never registered);
    2. registration lands on the virtual account NT SERVICE\Orkeon (never
       LocalSystem), with a quoted ImagePath carrying --settings and
       --working-dir, the Environment REG_MULTI_SZ value, and Modify for the
       account on the data directory;
    3. the service starts and stays up on an offline crew declared with a
       RELATIVE path — the end-to-end proof that --working-dir moves the daemon
       where the operator's paths are true (a Windows service is born in
       System32) — then stops cleanly;
    4. a refused configuration (crew path that does not exist) leaves the
       service Stopped without an SCM restart loop, and the refusal lands in
       the Application event log, the only place a service operator reads;
    5. install-service.ps1 -Uninstall removes the service and leaves the
       operator's ProgramData\Orkeon untouched.

  The assertions live in lib\service-windows.ps1, shared with the MSI service
  channel so the two cannot drift. Requires administrator rights (the GitHub
  windows-latest runners run elevated). Compatible with Windows PowerShell 5.1
  and PowerShell 7.

.PARAMETER ArchivePath
  The orkeon-<ver>-win-x64.zip (full archive) to smoke.

.PARAMETER WorkDir
  Scratch directory for extraction and logs. Default: a new folder under the temp
  directory, removed on exit.

.PARAMETER Keep
  Keeps the scratch directory.

.EXAMPLE
  .\scripts\smoke-onboarding\run-smoke-service.ps1 -ArchivePath .\artifacts\installers\orkeon-1.0.0-rc.3-win-x64.zip
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,

    [string]$WorkDir,

    [switch]$Keep
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\service-windows.ps1')

$serviceName = 'Orkeon'
$installRoot = Join-Path $env:ProgramFiles 'Orkeon'
$dataDir = Join-Path $env:ProgramData 'Orkeon'
$fixtures = Join-Path $PSScriptRoot 'fixtures'

if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) ("orkeon-smoke-service-" + [Guid]::NewGuid().ToString('N')) }
$logDir = Join-Path $WorkDir 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$problems = @()
$transcript = Join-Path $logDir 'smoke-service.log'
Start-Transcript -Path $transcript | Out-Null

function Add-Result([string]$step, [pscustomobject]$result) {
    foreach ($n in $result.Notes) { Write-Host "  [$step] $n" }
    foreach ($p in $result.Problems) { Write-Host "  [$step] PROBLEM: $p"; $script:problems += "${step}: $p" }
}

try {
    if ((Test-Path $installRoot) -or (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        throw "This smoke needs a machine without '$installRoot' or a '$serviceName' service — it installs to the real defaults. Run it on a disposable runner."
    }

    # -- 1. Extract and assert the archive layout -------------------------------
    Write-Host "== Extracting $ArchivePath"
    $extract = Join-Path $WorkDir 'extract'
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extract -Force
    $inner = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
    if (-not $inner) { throw "The archive extracted to no directory." }
    $tree = $inner.FullName

    foreach ($required in @(
        'libexec\orkeon-host\orkeon-host.exe',
        'deploy\windows\install-service.ps1',
        'deploy\systemd\orkeon-host.service',
        'bin\orkeon-host.cmd'
    )) {
        if (-not (Test-Path (Join-Path $tree $required))) { $problems += "archive layout: missing $required" }
    }
    if ($problems.Count -gt 0) { throw "Archive layout assertions failed: $($problems -join '; ')" }
    Write-Host "  layout OK (exe, deploy assets, terminal wrapper)"

    # -- 2. Lay out Program Files + ProgramData like the docs say ---------------
    Write-Host "== Installing under $installRoot, data under $dataDir"
    Copy-Item -Recurse -LiteralPath $tree -Destination $installRoot
    New-Item -ItemType Directory -Force -Path (Join-Path $dataDir 'crews\smoke') | Out-Null
    Copy-Item (Join-Path $fixtures 'offline-crew.yaml') (Join-Path $dataDir 'crews\smoke\config.yaml')
    # The crew path is deliberately RELATIVE: it only resolves because --working-dir
    # moved the service out of System32. This is the end-to-end proof of WINSVC-02 P2.
    Set-Content -LiteralPath (Join-Path $dataDir 'appsettings.json') -Value @'
{ "Orkeon": { "Host": { "Crews": [ { "Name": "smoke", "Path": "crews/smoke" } ] } } }
'@

    # -- 3. Register through the bundled script, with a secret ------------------
    Write-Host "== Registering the service (bundled install-service.ps1)"
    & (Join-Path $installRoot 'deploy\windows\install-service.ps1') `
        -EnvironmentSecrets @{ ORKEON_SMOKE = '1' } *>&1 | Tee-Object -FilePath (Join-Path $logDir 'install-service.log')

    Add-Result 'registration' (Invoke-OrkeonServiceRegistrationAssertions `
        -ServiceName $serviceName `
        -ExecutablePath (Join-Path $installRoot 'libexec\orkeon-host\orkeon-host.exe') `
        -ExpectEnvironment)
    Add-Result 'acl' (Invoke-OrkeonServiceDataAclAssertions -ServiceName $serviceName -DataDir $dataDir)

    # -- 4. Start / stability / stop on the offline crew ------------------------
    Write-Host "== Start / stability / stop (relative crew path through --working-dir)"
    Add-Result 'start-stop' (Invoke-OrkeonServiceStartStopAssertions -ServiceName $serviceName -StabilitySeconds 10)

    # -- 5. Refused configuration: Stopped, no loop, event-log entry ------------
    Write-Host "== Refused configuration (crew path that does not exist)"
    Set-Content -LiteralPath (Join-Path $dataDir 'appsettings.json') -Value @'
{ "Orkeon": { "Host": { "Crews": [ { "Name": "smoke", "Path": "crews/definitely-not-there" } ] } } }
'@
    Add-Result 'broken-config' (Invoke-OrkeonServiceBrokenConfigAssertions -ServiceName $serviceName -ObservationSeconds 20)

    # -- 6. Uninstall leaves the operator's data alone --------------------------
    Write-Host "== Uninstall (-Uninstall)"
    & (Join-Path $installRoot 'deploy\windows\install-service.ps1') -Uninstall *>&1 |
        Tee-Object -FilePath (Join-Path $logDir 'uninstall-service.log')
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { $problems += 'uninstall: service still registered' }
    if (-not (Test-Path (Join-Path $dataDir 'appsettings.json'))) { $problems += 'uninstall: ProgramData\Orkeon was deleted — the operator config must survive' }

    if ($problems.Count -gt 0) {
        Write-Host "`nFAIL — $($problems.Count) problem(s):"
        $problems | ForEach-Object { Write-Host "  - $_" }
        exit 1
    }
    Write-Host "`nPASS — the Windows service channel holds the WINSVC-01 contract."
    exit 0
}
finally {
    Stop-Transcript | Out-Null
    # Best-effort teardown so a failed run does not poison the next one.
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $serviceName | Out-Null
    }
    if (Test-Path $installRoot) { Remove-Item -Recurse -Force -LiteralPath $installRoot -ErrorAction SilentlyContinue }
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force -LiteralPath $dataDir -ErrorAction SilentlyContinue }
    if (-not $Keep -and (Test-Path $WorkDir)) { Remove-Item -Recurse -Force -LiteralPath $WorkDir -ErrorAction SilentlyContinue }
}
