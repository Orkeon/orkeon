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
      refusal message lands in the Application event log;
    - with A2A on and its URL reserved, the agent card answers (GAP-35).

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
#
#   -ExpectInEventLog     a literal text the refusal entry must also carry — the netsh command
#                         an A2A listener refused by HTTP.sys names (GAP-35).
function Invoke-OrkeonServiceBrokenConfigAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [int]$ObservationSeconds = 20,
        [string]$ExpectInEventLog
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
        if ($ExpectInEventLog -and -not $entry.Message.Contains($ExpectInEventLog)) {
            $problems += "the refusal in the event log does not carry '$ExpectInEventLog'"
        }
    }

    return [pscustomobject]@{ Problems = $problems; Notes = $notes }
}

# Invoke-OrkeonServiceA2ACardAssertions — starts the service with Orkeon:Host:A2A on, requires
# GET /.well-known/agent.json to answer with the expected skill within the window, then stops it
# (GAP-35: the listener runs as NT SERVICE\<name>, on the URL install-service.ps1 -A2AUrlPrefix
# reserved).
#
#   -ServiceName          the SCM name (Orkeon)
#   -CardUrl              the card's address, such as http://localhost:5002/.well-known/agent.json
#   -SkillId              the skill id the card must list (the exposed crew's name)
function Invoke-OrkeonServiceA2ACardAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][string]$CardUrl,
        [Parameter(Mandatory = $true)][string]$SkillId,
        [int]$WaitSeconds = 30
    )

    $problems = @()
    $notes = @()

    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
    } catch {
        return [pscustomobject]@{ Problems = @("Start-Service failed: $($_.Exception.Message)"); Notes = @() }
    }

    $card = $null
    $lastError = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not $card) {
        Start-Sleep -Seconds 1
        if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
        try {
            $card = Invoke-RestMethod -Uri $CardUrl -TimeoutSec 5
        } catch {
            $lastError = $_.Exception.Message
        }
    }

    if (-not $card) {
        $status = (Get-Service -Name $ServiceName).Status
        $problems += "the agent card at $CardUrl never answered (service $status; last error: $lastError)"
    } elseif (-not ($card.skills | Where-Object { $_.id -eq $SkillId })) {
        $problems += "the agent card at $CardUrl lists no skill '$SkillId'"
    } else {
        $notes += "the agent card at $CardUrl lists the skill '$SkillId'"
    }

    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))

    return [pscustomobject]@{ Problems = $problems; Notes = $notes }
}

# Test-OrkeonUrlReservation — whether HTTP.sys holds a reservation of $Url for $Account
# (netsh http show urlacl lists the user of each reservation it shows; matched literally, so a
# localized netsh still answers).
function Test-OrkeonUrlReservation {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Account
    )

    $shown = (& netsh.exe http show urlacl url=$Url) -join ' / '
    return $shown.Contains($Account)
}
