<#
.SYNOPSIS
  Service-host assertions for the two Windows service channels (WINSVC-02).

.DESCRIPTION
  Dot-sourced, never executed. The ZIP channel (run-smoke-service.ps1) and the MSI
  channel (run-smoke-service-msi.ps1) install to the same layout and register the
  same service — the assertions must not be written twice and allowed to drift,
  same doctrine as lib\studio-windows.ps1.

  What both channels must prove about a registered 'Orkeon' service:
    - it runs under the virtual account NT SERVICE\Orkeon, not LocalSystem;
    - its ImagePath is quoted and carries --settings and --working-dir;
    - the service account holds Modify on the data directory;
    - it starts, stays up, and stops cleanly with a valid offline configuration;
    - a refused configuration leaves it Stopped — no SCM restart loop — and the
      refusal message lands in the Application event log.

  The callers decide what to do with the result; nothing here writes to the
  console or exits. Compatible with Windows PowerShell 5.1 and PowerShell 7.
#>

# Invoke-OrkeonServiceRegistrationAssertions — asserts how the service is registered.
#
#   -ServiceName          the SCM name (Orkeon)
#   -ExecutablePath       the exe the ImagePath must point at
#   -ExpectEnvironment    also require the Environment REG_MULTI_SZ value on the key
#
# Returns [pscustomobject]@{ Problems = @(...); Notes = @(...) }.
function Invoke-OrkeonServiceRegistrationAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [switch]$ExpectEnvironment
    )

    $problems = @()
    $notes = @()

    $svc = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if (-not $svc) {
        return [pscustomobject]@{ Problems = @("service '$ServiceName' is not registered"); Notes = @() }
    }

    $expectedAccount = "NT SERVICE\$ServiceName"
    if ($svc.StartName -ne $expectedAccount) {
        $problems += "service account is '$($svc.StartName)', expected '$expectedAccount'"
    } else {
        $notes += "service account: $($svc.StartName)"
    }

    if ($svc.StartMode -ne 'Auto') {
        $problems += "start mode is '$($svc.StartMode)', expected 'Auto'"
    }

    $imagePath = $svc.PathName
    if ($imagePath -notlike "`"$ExecutablePath`"*") {
        $problems += "ImagePath does not start with the quoted executable: $imagePath"
    }
    if ($imagePath -notmatch '--settings') { $problems += "ImagePath carries no --settings: $imagePath" }
    if ($imagePath -notmatch '--working-dir') { $problems += "ImagePath carries no --working-dir: $imagePath" }
    if ($problems.Count -eq 0) { $notes += "ImagePath: $imagePath" }

    if ($ExpectEnvironment) {
        $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        $value = (Get-ItemProperty -Path $key -Name Environment -ErrorAction SilentlyContinue).Environment
        if (-not $value) {
            $problems += "no Environment REG_MULTI_SZ value under $key"
        } else {
            $notes += "Environment value: $($value.Count) entr$(if ($value.Count -eq 1) { 'y' } else { 'ies' })"
        }
    }

    return [pscustomobject]@{ Problems = $problems; Notes = $notes }
}

# Invoke-OrkeonServiceDataAclAssertions — the service account must hold Modify on
# the data directory it starts in (and nothing here checks Program Files: Users
# already read there, which is all the account needs).
function Invoke-OrkeonServiceDataAclAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][string]$DataDir
    )

    $problems = @()
    $acl = icacls $DataDir 2>&1 | Out-String
    if ($acl -notmatch [regex]::Escape("NT SERVICE\$ServiceName") -or $acl -notmatch '\(M\)') {
        $problems += "icacls of '$DataDir' shows no Modify grant for NT SERVICE\${ServiceName}: $($acl.Trim())"
    }
    return [pscustomobject]@{ Problems = $problems; Notes = @() }
}

# Invoke-OrkeonServiceStartStopAssertions — starts the service, requires it to stay
# Running for the whole stability window, then stops it cleanly.
function Invoke-OrkeonServiceStartStopAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [int]$StabilitySeconds = 10
    )

    $problems = @()
    $notes = @()

    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
    } catch {
        return [pscustomobject]@{ Problems = @("Start-Service failed: $($_.Exception.Message)"); Notes = @() }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($StabilitySeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 1
        $status = (Get-Service -Name $ServiceName).Status
        if ($status -ne 'Running' -and $status -ne 'StartPending') {
            $problems += "service left Running during the stability window (status: $status)"
            break
        }
    }
    if ($problems.Count -eq 0) {
        $notes += "stayed Running for ${StabilitySeconds}s"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $stopped = (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        if ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
            $problems += "service did not reach Stopped after Stop-Service"
        }
    }

    return [pscustomobject]@{ Problems = $problems; Notes = $notes }
}

# Invoke-OrkeonServiceBrokenConfigAssertions — with a refused configuration the
# service must end Stopped and STAY Stopped (crash-only recovery never fires on an
# orderly configuration refusal), and the refusal must land in the Application
# event log — stderr goes nowhere under the SCM, the log is where the operator reads.
function Invoke-OrkeonServiceBrokenConfigAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [int]$ObservationSeconds = 20
    )

    $problems = @()
    $notes = @()
    $since = [DateTime]::Now.AddMinutes(-1)

    $startFailed = $false
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
    } catch {
        $startFailed = $true
        $notes += "Start-Service refused: $($_.Exception.Message)"
    }

    $sawRunningTwice = 0
    $deadline = [DateTime]::UtcNow.AddSeconds($ObservationSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 2
        if ((Get-Service -Name $ServiceName).Status -eq 'Running') { $sawRunningTwice++ }
    }

    $final = (Get-Service -Name $ServiceName).Status
    if ($final -ne 'Stopped') {
        $problems += "service is '$final' after a refused configuration, expected Stopped"
    }
    if ($sawRunningTwice -gt 1) {
        $problems += "service flapped (seen Running $sawRunningTwice times) — the SCM is looping on a configuration refusal"
    }
    if (-not $startFailed -and $final -eq 'Stopped') {
        $notes += 'start was accepted by the SCM, then the host refused the configuration and stopped'
    }

    $entry = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = $since } -MaxEvents 50 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('Orkeon', '.NET Runtime') -and $_.Message -match 'orkeon-host' } |
        Select-Object -First 1
    if (-not $entry) {
        $problems += "no Application event-log Error from 'Orkeon' (or '.NET Runtime') mentioning orkeon-host — the refusal is invisible to the operator"
    } else {
        $notes += "event log ($($entry.ProviderName)): $(($entry.Message -split "`n")[0])"
    }

    return [pscustomobject]@{ Problems = $problems; Notes = $notes }
}
