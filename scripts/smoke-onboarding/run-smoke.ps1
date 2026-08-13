<#
.SYNOPSIS
  Released-artefact smoke for the Windows CLI archive (WIN-06).

.DESCRIPTION
  Extracts orkeon-cli-<ver>-win-x64.zip, installs it with the bundled install.ps1,
  walks the whole onboarding chain on the installed binary, then uninstalls and
  checks the removal is clean. It is the Windows twin of run-smoke-deb.sh (LIN-02):
  same fixtures (fixtures\offline-crew.yaml, fixtures\rag-corpus,
  fixtures\rag-settings.json), same assertions, only the install/uninstall phase
  differs.

  What it exercises, end to end, on the *published* archive:
    1. the archive layout survived packaging (bin\orkeon.cmd, install.ps1, VERSION);
    2. the payload survived it too — esbuild.exe, the BGE-micro-v2 embedding model
       and the 7 whitelisted tree-sitter grammars (WIN-04 pruning);
    3. install.ps1 installs, registers an Add/Remove Programs entry and adds its
       bin\ folder to the user PATH (WIN-05);
    4. a fresh session resolves `orkeon` from that PATH entry alone;
   4b. Orkeon Studio shipped and starts: bin\orkeon-studio.cmd and
       libexec\orkeon-studio\Orkeon.Studio.exe are installed, and
       `orkeon-studio --smoke-exit` opens the WPF window, lets it render and
       exits 0 (STUDIO-08, spec §8.4) -- the assertions live in
       lib\studio-windows.ps1, shared with the MSI job;
    5. `orkeon init --provider none --force` writes %APPDATA%\Orkeon (WIN-02);
    6. `orkeon doctor --json` reports no fail, and the three payload-backed checks
       are green rather than merely non-failing — doctor only *warns* on a missing
       esbuild / model / grammar, so "no fail" alone would not catch a stripped
       archive (WIN-03);
    7. `orkeon run <offline crew>` exits 0 and prints the WIN-01 warning;
    8. `orkeon rag ingest` + `orkeon rag search` retrieve with citations and
       scores, fully offline;
    9. install.ps1 -Uninstall removes the directory, the ARP key and the PATH
       entry, and leaves %APPDATA%\Orkeon alone.

  Note on `orkeon --version` / `--help`: both exit 1 (a pre-existing
  CommandLineParser behaviour), so `orkeon doctor` is the liveness probe here.

  Compatible with Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER ArchivePath
  The orkeon-cli-<ver>-win-x64.zip to smoke.

.PARAMETER WorkDir
  Scratch directory. Default: a new folder under the temp directory, removed on exit.

.PARAMETER Keep
  Keeps the scratch directory (and skips nothing else).

.EXAMPLE
  .\scripts\smoke-onboarding\run-smoke.ps1 -ArchivePath .\artifacts\installers\orkeon-cli-0.9.2-beta-win-x64.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,

    [string]$WorkDir,

    [switch]$Keep
)

$ErrorActionPreference = 'Stop'

$fixtures = Join-Path $PSScriptRoot 'fixtures'

# Orkeon Studio assertions, shared with the msi job (see the file's header).
. (Join-Path $PSScriptRoot 'lib\studio-windows.ps1')

# The grammars WIN-04's MSBuild pruning keeps (src\Directory.Build.targets,
# OrkeonTreeSitterKeptGrammars). Losing one must fail the smoke.
$keptGrammars = @(
    'tree-sitter',
    'tree-sitter-typescript',
    'tree-sitter-tsx',
    'tree-sitter-python',
    'tree-sitter-c-sharp',
    'tree-sitter-go',
    'tree-sitter-rust'
)

# doctor downgrades a missing esbuild / embedding model / grammar to a warning;
# these three must be green for the archive to be considered intact.
$strictChecks = @('esbuild', 'local-embeddings', 'tree-sitter')

$arpKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Orkeon'

# ---------------------------------------------------------------------------- #
# Bookkeeping
# ---------------------------------------------------------------------------- #
$script:Steps = New-Object System.Collections.ArrayList
$script:Failures = 0

function Write-Section([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-Info([string]$Text) {
    Write-Host "    $Text"
}

function Add-Step([string]$Status, [string]$Label, [string]$Note) {
    [void]$script:Steps.Add([PSCustomObject]@{ Status = $Status; Label = $Label; Note = $Note })
}

function Step-Pass([string]$Label, [string]$Note) {
    Add-Step 'PASS' $Label $Note
    Write-Host "    PASS $Label $Note" -ForegroundColor Green
}

function Step-Fail([string]$Label, [string]$Note) {
    Add-Step 'FAIL' $Label $Note
    $script:Failures++
    Write-Host "    FAIL $Label $Note" -ForegroundColor Red
}

function Write-Tail([string]$Path, [int]$Lines = 10) {
    if (Test-Path -LiteralPath $Path) {
        Get-Content -LiteralPath $Path -Tail $Lines -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "    | $_" }
    }
}

# ---------------------------------------------------------------------------- #
# Native-command helper
# ---------------------------------------------------------------------------- #
# Runs the installed orkeon.cmd from the scratch working directory, capturing the
# two streams into separate files. $ErrorActionPreference is relaxed for the call:
# in Windows PowerShell 5.1, a native command that writes to stderr under a `2>`
# redirection raises NativeCommandError when the preference is 'Stop' -- which
# every one of these invocations does (the WIN-01 warning is on stderr by design).
function Invoke-Orkeon {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $outFile = Join-Path $script:LogDir "$Label.out.txt"
    $errFile = Join-Path $script:LogDir "$Label.err.txt"

    $code = -1
    $launchError = $null
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        Push-Location -LiteralPath $script:RunDir
        try {
            & $script:OrkeonCmd @Arguments 1> $outFile 2> $errFile
            $code = $LASTEXITCODE
        } catch {
            # A missing/unlaunchable orkeon.cmd throws CommandNotFoundException, which
            # stays terminating whatever the preference is. Turn it into a normal
            # non-zero result so the caller reports a FAIL instead of the script dying.
            $launchError = $_.Exception.Message
        } finally {
            Pop-Location
        }
    } finally {
        $ErrorActionPreference = $previous
    }

    $stdout = ''
    if (Test-Path -LiteralPath $outFile) {
        $raw = Get-Content -LiteralPath $outFile -Raw -ErrorAction SilentlyContinue
        if ($raw) { $stdout = $raw }
    }
    $stderr = ''
    if (Test-Path -LiteralPath $errFile) {
        $raw = Get-Content -LiteralPath $errFile -Raw -ErrorAction SilentlyContinue
        if ($raw) { $stderr = $raw }
    }
    if ($launchError) {
        $line = "could not launch $script:OrkeonCmd -- $launchError"
        Add-Content -LiteralPath $errFile -Value $line -ErrorAction SilentlyContinue
        $stderr = $line + "`n" + $stderr
    }

    return [PSCustomObject]@{
        ExitCode = $code
        StdOut   = $stdout
        StdErr   = $stderr
        OutFile  = $outFile
        ErrFile  = $errFile
    }
}

# Reads the user PATH the way install.ps1 writes it: raw (unexpanded) from the
# registry, so a REG_EXPAND_SZ entry is compared as it was stored.
function Get-UserPathRaw {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $false)
    if (-not $key) { return '' }
    try {
        $raw = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -eq $raw) { return '' }
        return [string]$raw
    } finally {
        $key.Close()
    }
}

# ---------------------------------------------------------------------------- #
# Scratch directories
# ---------------------------------------------------------------------------- #
$createdWorkDir = $false
if (-not $WorkDir) {
    $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("orkeon-smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $createdWorkDir = $true
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path

$script:LogDir = Join-Path $WorkDir 'logs'
$script:RunDir = Join-Path $WorkDir 'run'
$extractDir = Join-Path $WorkDir 'extract'
$installDir = Join-Path $WorkDir 'install'
New-Item -ItemType Directory -Force -Path $script:LogDir, $script:RunDir, $extractDir | Out-Null

Write-Section "Orkeon Windows CLI smoke -- work dir: $WorkDir"

# ---------------------------------------------------------------------------- #
# 1. Expand the archive
# ---------------------------------------------------------------------------- #
Write-Section 'Expand-Archive'
if (-not (Test-Path -LiteralPath $ArchivePath)) {
    Step-Fail 'expand' "archive not found: $ArchivePath"
    Write-Host 'SMOKE FAILED' -ForegroundColor Red
    exit 1
}
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path

Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extractDir -Force

$roots = @(Get-ChildItem -LiteralPath $extractDir -Directory)
if ($roots.Count -ne 1) {
    Step-Fail 'expand' "expected exactly one top-level folder in the archive, found $($roots.Count)"
    Write-Host 'SMOKE FAILED' -ForegroundColor Red
    exit 1
}
$archiveRoot = $roots[0].FullName
$version = 'unknown'
$versionFile = Join-Path $archiveRoot 'VERSION'
if (Test-Path -LiteralPath $versionFile) {
    $version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
}
Step-Pass 'expand' "$($roots[0].Name) (VERSION $version)"

# ---------------------------------------------------------------------------- #
# 2. Archive layout and payload
# ---------------------------------------------------------------------------- #
Write-Section 'Archive payload'
$appDir = Join-Path (Join-Path $archiveRoot 'libexec') 'orkeon'
$modelDir = Join-Path (Join-Path $appDir 'LocalEmbeddingsModel') 'default'
$esbuildDir = Join-Path (Join-Path $archiveRoot 'libexec') 'esbuild-bin'

$expected = New-Object System.Collections.ArrayList
[void]$expected.Add((Join-Path (Join-Path $archiveRoot 'bin') 'orkeon.cmd'))
[void]$expected.Add((Join-Path $archiveRoot 'install.ps1'))
[void]$expected.Add((Join-Path $archiveRoot 'VERSION'))
[void]$expected.Add((Join-Path $appDir 'orkeon.exe'))
# Orkeon Studio rides in the same win-x64 cli staging tree (STUDIO-07): its
# launcher and its self-contained WPF apphost are part of the archive contract.
[void]$expected.Add((Join-Path (Join-Path $archiveRoot 'bin') 'orkeon-studio.cmd'))
[void]$expected.Add((Join-Path (Join-Path (Join-Path $archiveRoot 'libexec') 'orkeon-studio') 'Orkeon.Studio.exe'))
[void]$expected.Add((Join-Path $esbuildDir 'esbuild.exe'))
[void]$expected.Add((Join-Path $modelDir 'model.onnx'))
[void]$expected.Add((Join-Path $modelDir 'vocab.txt'))
foreach ($grammar in $keptGrammars) {
    [void]$expected.Add((Join-Path $appDir "$grammar.dll"))
}

$missing = @($expected | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) {
    $relative = $missing | ForEach-Object { $_.Substring($archiveRoot.Length).TrimStart('\') }
    Step-Fail 'payload' ("missing from the archive: " + ($relative -join ', '))
} else {
    Step-Pass 'payload' "launcher + esbuild.exe + BGE-micro-v2 model + $($keptGrammars.Count) tree-sitter libraries present"
}

# ---------------------------------------------------------------------------- #
# 3. install.ps1
# ---------------------------------------------------------------------------- #
Write-Section 'install.ps1'
$installPs1 = Join-Path $archiveRoot 'install.ps1'
$binDir = Join-Path $installDir 'bin'
$installFailed = $false
try {
    & $installPs1 -InstallDir $installDir
    Step-Pass 'install' "installed to $installDir"
} catch {
    Step-Fail 'install' "install.ps1 threw: $($_.Exception.Message)"
    $installFailed = $true
}

if ($installFailed) {
    Write-Host 'SMOKE FAILED (nothing installed, later steps are moot)' -ForegroundColor Red
    exit 1
}

# Add/Remove Programs entry (WIN-05).
if (Test-Path -LiteralPath $arpKeyPath) {
    $arp = Get-ItemProperty -LiteralPath $arpKeyPath
    if ($arp.DisplayVersion -eq $version -and $arp.InstallLocation -eq $installDir) {
        Step-Pass 'arp' "Add/Remove Programs entry registered (version $($arp.DisplayVersion))"
    } else {
        Step-Fail 'arp' "entry present but stale: DisplayVersion='$($arp.DisplayVersion)' InstallLocation='$($arp.InstallLocation)'"
    }
    # The UninstallString must carry -InstallDir: without it, uninstalling a
    # custom-directory install from Add/Remove Programs would target the
    # default directory and orphan the real one.
    if ($arp.UninstallString -match [regex]::Escape($installDir)) {
        Step-Pass 'arp-uninstallstring' "UninstallString targets the actual install directory"
    } else {
        Step-Fail 'arp-uninstallstring' "UninstallString does not reference $installDir : '$($arp.UninstallString)'"
    }
} else {
    Step-Fail 'arp' "no Add/Remove Programs entry at $arpKeyPath"
}

# ---------------------------------------------------------------------------- #
# 4. Fresh session: PATH reloaded from the registry
# ---------------------------------------------------------------------------- #
Write-Section 'New session (PATH reloaded from the registry)'
$userPathRaw = Get-UserPathRaw
$userPathEntries = @($userPathRaw -split ';' | Where-Object { $_ })
if ($userPathEntries -contains $binDir) {
    Step-Pass 'path' "$binDir is on the user PATH"
} else {
    Step-Fail 'path' "$binDir is not on the user PATH (raw value: $userPathRaw)"
}

# Rebuild the process PATH the way a brand-new terminal would: machine PATH first,
# then the user PATH -- both expanded. `orkeon` must then resolve on its own.
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$env:PATH = @($machinePath, [Environment]::ExpandEnvironmentVariables($userPathRaw)) -join ';'

$resolved = Get-Command 'orkeon' -ErrorAction SilentlyContinue
if ($resolved -and $resolved.Source -and $resolved.Source.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase)) {
    $script:OrkeonCmd = $resolved.Source
    Step-Pass 'resolve' "orkeon resolves to $($resolved.Source)"
} else {
    $where = 'nothing'
    if ($resolved) { $where = $resolved.Source }
    Step-Fail 'resolve' "orkeon did not resolve to the install directory (got: $where)"
    # Fall back to the known path so the remaining steps still produce signal.
    $script:OrkeonCmd = Join-Path $binDir 'orkeon.cmd'
}

# ---------------------------------------------------------------------------- #
# 4b. Orkeon Studio -- installed, and it actually starts (STUDIO-08)
# ---------------------------------------------------------------------------- #
# The ZIP channel has no Start-menu shortcut (that is the MSI's own addition), so
# only the payload and the start/exit smoke are asserted here.
Write-Section 'orkeon-studio --smoke-exit'
$studio = Invoke-OrkeonStudioWindowsSmoke -InstallRoot $installDir -LogDir $script:LogDir
foreach ($note in $studio.Notes) { Write-Info $note }
if ($studio.Problems.Count -gt 0) {
    Step-Fail 'studio' ($studio.Problems -join '; ')
} else {
    Step-Pass 'studio' 'orkeon-studio.cmd + Orkeon.Studio.exe installed; window opened and closed, exit 0'
}

# ---------------------------------------------------------------------------- #
# 5. orkeon init (WIN-02)
# ---------------------------------------------------------------------------- #
Write-Section 'orkeon init --provider none --force'
$configDir = Join-Path $env:APPDATA 'Orkeon'
$configFile = Join-Path $configDir 'appsettings.json'
$configBackup = $null
if (Test-Path -LiteralPath $configFile) {
    $configBackup = Join-Path $WorkDir 'appsettings.json.pre-smoke'
    Copy-Item -LiteralPath $configFile -Destination $configBackup -Force
    Write-Info "existing user config backed up to $configBackup"
}

$init = Invoke-Orkeon -Label 'init' -Arguments @('init', '--provider', 'none', '--force')
if ($init.ExitCode -ne 0) {
    Write-Tail $init.ErrFile
    Step-Fail 'init' "exit $($init.ExitCode) (expected 0)"
} elseif (Test-Path -LiteralPath $configFile) {
    Step-Pass 'init' "wrote $configFile"
} else {
    Step-Fail 'init' "exit 0 but no config at $configFile"
}

# ---------------------------------------------------------------------------- #
# 6. orkeon doctor --json (WIN-03)
# ---------------------------------------------------------------------------- #
Write-Section 'orkeon doctor --json'
$doctor = Invoke-Orkeon -Label 'doctor' -Arguments @('doctor', '--json')
if ($doctor.ExitCode -gt 1) {
    Write-Tail $doctor.ErrFile
    Step-Fail 'doctor' "exit $($doctor.ExitCode) (expected 0 or 1)"
} else {
    $results = $null
    try {
        $results = @($doctor.StdOut | ConvertFrom-Json)
    } catch {
        $results = $null
    }

    if (-not $results -or $results.Count -eq 0) {
        Write-Tail $doctor.OutFile 20
        Step-Fail 'doctor' 'doctor --json did not produce a parsable, non-empty JSON array'
    } else {
        $problems = New-Object System.Collections.ArrayList
        foreach ($entry in $results) {
            if ($entry.status -eq 'fail') {
                [void]$problems.Add("$($entry.check): $($entry.detail)")
            }
        }
        foreach ($name in $strictChecks) {
            $entry = $results | Where-Object { $_.check -eq $name } | Select-Object -First 1
            if (-not $entry) {
                [void]$problems.Add("${name}: check absent from the report")
            } elseif ($entry.status -ne 'ok') {
                [void]$problems.Add("${name}: expected ok, got $($entry.status) ($($entry.detail))")
            }
        }

        if ($problems.Count -gt 0) {
            Write-Tail $doctor.OutFile 20
            Step-Fail 'doctor' ($problems -join '; ')
        } else {
            $warned = @($results | Where-Object { $_.status -eq 'warn' } | ForEach-Object { $_.check })
            $note = "$($results.Count) checks, no failure, strict checks green"
            if ($warned.Count -gt 0) { $note += " (warnings tolerated: $($warned -join ', '))" }
            Step-Pass 'doctor' $note
        }
    }
}

# ---------------------------------------------------------------------------- #
# 7. orkeon run -- offline crew, echo fallback (WIN-01)
# ---------------------------------------------------------------------------- #
Write-Section 'orkeon run (offline crew, echo fallback)'
Copy-Item -LiteralPath (Join-Path $fixtures 'offline-crew.yaml') -Destination (Join-Path $script:RunDir 'offline-crew.yaml') -Force

$run = Invoke-Orkeon -Label 'run' -Arguments @('run', 'offline-crew.yaml')
if ($run.ExitCode -ne 0) {
    Write-Tail $run.ErrFile
    Step-Fail 'run' "exit $($run.ExitCode) (expected 0 -- the echo fallback makes the run deterministic)"
} elseif ($run.StdErr -match 'orkeon init') {
    Step-Pass 'run' 'exit 0 and the WIN-01 warning points at `orkeon init`'
} else {
    Write-Tail $run.ErrFile
    Step-Fail 'run' 'exit 0 but no `orkeon init` warning on stderr'
}

# ---------------------------------------------------------------------------- #
# 8. orkeon rag ingest + search -- offline retrieval
# ---------------------------------------------------------------------------- #
Write-Section 'orkeon rag ingest / search (offline)'
Copy-Item -LiteralPath (Join-Path $fixtures 'rag-corpus') -Destination (Join-Path $script:RunDir 'rag-corpus') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $fixtures 'rag-settings.json') -Destination (Join-Path $script:RunDir 'rag-settings.json') -Force

$ingest = Invoke-Orkeon -Label 'rag-ingest' -Arguments @(
    'rag', 'ingest', '--settings', 'rag-settings.json', '--collection', 'smoke', '--source', 'rag-corpus/*.md')
if ($ingest.ExitCode -ne 0) {
    Write-Tail $ingest.ErrFile
    Step-Fail 'rag-ingest' "exit $($ingest.ExitCode) (expected 0)"
} elseif ($ingest.StdOut -match 'Chunks: [1-9]\d* created') {
    Step-Pass 'rag-ingest' $Matches[0]
} else {
    Write-Tail $ingest.OutFile
    Step-Fail 'rag-ingest' 'exit 0 but no chunk was created'
}

$search = Invoke-Orkeon -Label 'rag-search' -Arguments @(
    'rag', 'search', 'What does orkeon doctor do?', '--settings', 'rag-settings.json', '--collection', 'smoke')
if ($search.ExitCode -ne 0) {
    Write-Tail $search.ErrFile
    Step-Fail 'rag-search' "exit $($search.ExitCode) (expected 0)"
} else {
    # The answer text is empty without an LLM; the citations and their scores are
    # the retrieval evidence, and that is all this step asserts.
    $citations = @([regex]::Matches($search.StdOut, '(?m)^- \[\d+\] .*\(score: [0-9.]+\)'))
    if ($search.StdOut -match '(?m)^Sources:' -and $citations.Count -gt 0) {
        Step-Pass 'rag-search' "$($citations.Count) citation(s) with scores"
    } else {
        Write-Tail $search.OutFile
        Step-Fail 'rag-search' 'exit 0 but no citation with a score in the output'
    }
}

# ---------------------------------------------------------------------------- #
# 9. install.ps1 -Uninstall
# ---------------------------------------------------------------------------- #
Write-Section 'install.ps1 -Uninstall'
# The archive copy is gone-proof: install.ps1 copied itself into the install dir
# and the ARP UninstallString points at that copy, which is what a user would run.
$installedPs1 = Join-Path $installDir 'install.ps1'
$uninstallScript = $installedPs1
if (-not (Test-Path -LiteralPath $installedPs1)) {
    Step-Fail 'uninstall-copy' "install.ps1 was not copied into $installDir (ARP UninstallString would dangle)"
    $uninstallScript = $installPs1
} else {
    Step-Pass 'uninstall-copy' 'install.ps1 travelled into the install directory'
}

try {
    & $uninstallScript -InstallDir $installDir -Uninstall

    $leftovers = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath $installDir) { [void]$leftovers.Add("$installDir still exists") }
    if (Test-Path -LiteralPath $arpKeyPath) { [void]$leftovers.Add("$arpKeyPath still exists") }
    $pathAfter = @((Get-UserPathRaw) -split ';' | Where-Object { $_ })
    if ($pathAfter -contains $binDir) { [void]$leftovers.Add("$binDir still on the user PATH") }

    if ($leftovers.Count -eq 0) {
        Step-Pass 'uninstall' 'install dir, ARP entry and PATH entry all removed'
    } else {
        Step-Fail 'uninstall' ($leftovers -join '; ')
    }
} catch {
    Step-Fail 'uninstall' "install.ps1 -Uninstall threw: $($_.Exception.Message)"
}

if (Test-Path -LiteralPath $configFile) {
    Step-Pass 'config-preserved' "$configFile survived the uninstall"
} else {
    Step-Fail 'config-preserved' "$configFile was removed -- user configuration must never be touched"
}

# Leave the machine as we found it: restore a config we shadowed, or drop the one
# we created. Done after the assertions above, which need it in place.
if ($configBackup) {
    Copy-Item -LiteralPath $configBackup -Destination $configFile -Force
    Write-Info 'restored the pre-existing user config'
} else {
    Remove-Item -LiteralPath $configFile -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $configDir) -and -not (Get-ChildItem -LiteralPath $configDir -Force)) {
        Remove-Item -LiteralPath $configDir -Force -ErrorAction SilentlyContinue
    }
    Write-Info 'removed the config this smoke created'
}

# ---------------------------------------------------------------------------- #
# Summary
# ---------------------------------------------------------------------------- #
Write-Section 'Summary'
foreach ($step in $script:Steps) {
    $line = '    {0,-6} {1,-18} {2}' -f $step.Status, $step.Label, $step.Note
    if ($step.Status -eq 'FAIL') { Write-Host $line -ForegroundColor Red } else { Write-Host $line -ForegroundColor Green }
}

# A failed run keeps its scratch directory: the per-step .out/.err captures under
# logs\ are the only forensics available once the runner is gone.
if ($createdWorkDir -and -not $Keep -and $script:Failures -eq 0) {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Info "logs kept in $script:LogDir"
}

Write-Host ''
if ($script:Failures -eq 0) {
    Write-Host 'WINDOWS SMOKE PASSED' -ForegroundColor Green
    exit 0
}
Write-Host "WINDOWS SMOKE FAILED ($($script:Failures) step(s))" -ForegroundColor Red
exit 1
