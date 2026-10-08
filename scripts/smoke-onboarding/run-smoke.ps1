<#
.SYNOPSIS
  Released-artefact smoke for the Windows CLI archive (WIN-06).

.DESCRIPTION
  Extracts orkeon-cli-<ver>-win-x64.zip, installs it with the bundled install.cmd,
  installs it a second time over the first with install.ps1, walks the whole
  onboarding chain on the installed binary, then uninstalls and checks the
  removal is clean. It is the Windows twin of run-smoke-deb.sh (LIN-02):
  same fixtures (fixtures\offline-crew.yaml, fixtures\rag-corpus,
  fixtures\rag-settings.json), same assertions, only the install/uninstall phase
  differs.

  What it exercises, end to end, on the *published* archive:
    1. the archive layout survived packaging (bin\orkeon.cmd, install.ps1,
       install.cmd, VERSION), and install.cmd is what cmd.exe wants: ASCII, CRLF;
    2. the payload survived it too -- esbuild.exe, the BGE-micro-v2 embedding model
       and the 7 whitelisted tree-sitter grammars (WIN-04 pruning);
   2b. the notices came with it: LICENSE.md, THIRD-PARTY-NOTICES.md and one
       licenses\<pack>\ per .NET runtime an application bundles -- asserted on
       the archive, then again on the tree install.ps1 made of it (GAP-52); the
       assertions live in lib\notices-windows.ps1, shared with the msi job and
       the two service smokes;
    3. install.cmd installs -- the launcher a double-click runs: Windows
       PowerShell, an execution policy that holds for that one command, its
       arguments passed through -- then install.ps1 registers an Add/Remove
       Programs entry and adds its bin\ folder to the user PATH (WIN-05);
   3b. installing again over that installation replaces it: the same tree, file
       for file, one PATH entry and not two, the same DisplayVersion. This
       second install runs install.ps1 in the PowerShell that runs this smoke,
       so both PowerShell editions go through it across the two CI steps;
    4. a fresh session resolves `orkeon` from that PATH entry alone;
   4b. Orkeon Studio shipped and starts: bin\orkeon-studio.cmd and
       libexec\orkeon-studio\Orkeon.Studio.exe are installed, and
       `orkeon-studio --smoke-exit` opens the WPF window, lets it render and
       exits 0 (STUDIO-08, spec section 8.4) -- the assertions live in
       lib\studio-windows.ps1, shared with the MSI job;
   4c. while a program of the installation is running (Orkeon Studio, left
       open), install.ps1 refuses to install and refuses to uninstall, names
       the process, and leaves the installation in place intact: the same
       tree, and `orkeon --version` still answers;
    5. `orkeon init --provider none --force` writes %APPDATA%\Orkeon (WIN-02);
    6. `orkeon doctor --json` reports no fail, and the three payload-backed checks
       are green rather than merely non-failing -- doctor only *warns* on a missing
       esbuild / model / grammar, so "no fail" alone would not catch a stripped
       archive (WIN-03);
   6b. `orkeon --version --verbose` names the channel the install came through,
       `zip`: the INSTALL-CHANNEL marker the packaging wrote reached the install
       directory;
    7. `orkeon run <offline crew>` exits 0 and prints the WIN-01 warning;
    8. `orkeon rag ingest` + `orkeon rag search` retrieve with citations and
       scores, fully offline;
    9. install.ps1 -Uninstall removes the directory, the ARP key and the PATH
       entry, and leaves %APPDATA%\Orkeon alone.

  Note on `orkeon --version` / `--help`: both exit 0 since D3-05, but
  `orkeon doctor` stays the liveness probe here: a version line proves the
  entry point started, doctor proves the payload shipped.

  Compatible with Windows PowerShell 5.1 and PowerShell 7: release.yml runs it
  under both (`shell: pwsh`, then `shell: powershell`). This file stays ASCII:
  Windows PowerShell reads a script without a byte-order mark in the ANSI code
  page, where a UTF-8 dash becomes three characters, one of them a quote.

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
# Windows PowerShell draws a progress bar per archive entry; on an archive of a
# few thousand files that bar is most of Expand-Archive's time.
$ProgressPreference = 'SilentlyContinue'

$fixtures = Join-Path $PSScriptRoot 'fixtures'

# Orkeon Studio assertions, shared with the msi job (see the file's header).
. (Join-Path $PSScriptRoot 'lib\studio-windows.ps1')
# Notices assertions, shared with the msi job and the two service smokes.
. (Join-Path $PSScriptRoot 'lib\notices-windows.ps1')

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

# The notices of one tree (lib\notices-windows.ps1), as a step of this smoke.
function Step-Notices([string]$Label, [string]$Root) {
    $notices = Invoke-OrkeonNoticesAssertions -Root $Root
    if ($notices.Problems.Count -gt 0) {
        Step-Fail $Label ($notices.Problems -join '; ')
    } else {
        Step-Pass $Label ($notices.Notes -join '; ')
    }
}

function Write-Tail([string]$Path, [int]$Lines = 10) {
    if (Test-Path -LiteralPath $Path) {
        Get-Content -LiteralPath $Path -Tail $Lines -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "    | $_" }
    }
}

# ---------------------------------------------------------------------------- #
# Native-command helpers
# ---------------------------------------------------------------------------- #
# One argument of a cmd.exe command line: quoted when it holds a space or a
# character cmd.exe gives a meaning to. No argument of this smoke holds a quote.
function Format-CmdArgument([string]$Value) {
    if ($Value -eq '' -or $Value -match '[\s&|<>^()%!,;=]') { return '"' + $Value + '"' }
    return $Value
}

# Runs a .cmd with its arguments through cmd.exe and captures the two streams,
# apart, into $script:LogDir\<Label>.out.txt and .err.txt.
# The process is started directly rather than with `& <cmd> 1> out 2> err`:
# Windows PowerShell 5.1 turns every stderr line of a redirected native command
# into an error record -- a terminating one under $ErrorActionPreference =
# 'Stop', and otherwise one it formats and wraps before writing it, which
# breaks a message read back from the file. Read as text from the two pipes,
# what the command wrote is the same under both PowerShell editions.
function Invoke-CmdCaptured {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$CommandPath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $outFile = Join-Path $script:LogDir "$Label.out.txt"
    $errFile = Join-Path $script:LogDir "$Label.err.txt"

    $commandLine = '"' + $CommandPath + '"'
    foreach ($argument in $Arguments) { $commandLine += ' ' + (Format-CmdArgument $argument) }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $env:ComSpec
    # /s: cmd.exe strips the first and the last quote and runs what is between
    # them as it stands, quoted path and quoted arguments included.
    $startInfo.Arguments = '/d /s /c "' + $commandLine + '"'
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $code = -1
    $stdout = ''
    $stderr = ''
    try {
        $process = [System.Diagnostics.Process]::Start($startInfo)
        # Both pipes are drained while the process runs: read one after the
        # other, a command that fills the second pipe's buffer never exits.
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = [string]$outTask.Result
        $stderr = [string]$errTask.Result
        $code = $process.ExitCode
        $process.Dispose()
    } catch {
        # cmd.exe itself could not be started: a normal non-zero result, so the
        # caller reports a FAIL instead of the script dying.
        $stderr = "could not launch $CommandPath -- $($_.Exception.Message)"
    }

    [System.IO.File]::WriteAllText($outFile, $stdout)
    [System.IO.File]::WriteAllText($errFile, $stderr)

    return [PSCustomObject]@{
        ExitCode = $code
        StdOut   = $stdout
        StdErr   = $stderr
        OutFile  = $outFile
        ErrFile  = $errFile
    }
}

# Runs the installed orkeon.cmd from the scratch working directory. Every one of
# these invocations may write to stderr (the WIN-01 warning is there by design).
function Invoke-Orkeon {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    return Invoke-CmdCaptured -Label $Label -CommandPath $script:OrkeonCmd -Arguments $Arguments -WorkingDirectory $script:RunDir
}

# The files of a tree, as sorted relative paths: what "the same installation"
# means when an install is replaced, or refused.
function Get-TreeListing([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    $prefix = $Root.TrimEnd('\') + '\'
    return @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($prefix.Length) } |
        Sort-Object)
}

# What differs between two listings, in a few words; '' when nothing does.
function Compare-TreeListing([string[]]$Before, [string[]]$After) {
    if ($Before.Count -eq 0) { return 'the tree was empty before' }
    if ($After.Count -eq 0) { return 'the tree is empty' }
    if (($Before -join "`n") -ceq ($After -join "`n")) { return '' }
    $difference = @(Compare-Object -ReferenceObject $Before -DifferenceObject $After)
    $gone = @($difference | Where-Object { $_.SideIndicator -eq '<=' } | ForEach-Object { $_.InputObject })
    $new = @($difference | Where-Object { $_.SideIndicator -eq '=>' } | ForEach-Object { $_.InputObject })
    $parts = @()
    if ($gone.Count -gt 0) { $parts += "$($gone.Count) file(s) gone (" + (($gone | Select-Object -First 3) -join ', ') + ')' }
    if ($new.Count -gt 0) { $parts += "$($new.Count) file(s) new (" + (($new | Select-Object -First 3) -join ', ') + ')' }
    return ($parts -join '; ')
}

# The processes whose executable lives under a directory -- the question
# install.ps1 asks before it deletes one.
function Get-ProcessesUnder([string]$Root) {
    $prefix = $Root.TrimEnd('\') + '\'
    $found = @()
    foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { $path = $null }
        if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $found += $process }
    }
    return $found
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
[void]$expected.Add((Join-Path $archiveRoot 'install.cmd'))
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

# install.cmd is read by cmd.exe: plain ASCII, and every line ended by CRLF --
# whatever the checkout the archive was packed from made of the file.
$installCmd = Join-Path $archiveRoot 'install.cmd'
if (Test-Path -LiteralPath $installCmd) {
    $launcherBytes = [System.IO.File]::ReadAllBytes($installCmd)
    $nonAscii = 0
    $bareLineFeeds = 0
    for ($i = 0; $i -lt $launcherBytes.Length; $i++) {
        $byte = $launcherBytes[$i]
        if ($byte -gt 126 -or ($byte -lt 32 -and $byte -ne 9 -and $byte -ne 10 -and $byte -ne 13)) { $nonAscii++ }
        if ($byte -eq 10 -and ($i -eq 0 -or $launcherBytes[$i - 1] -ne 13)) { $bareLineFeeds++ }
    }
    if ($launcherBytes.Length -eq 0) {
        Step-Fail 'launcher' 'install.cmd is empty'
    } elseif ($nonAscii -gt 0 -or $bareLineFeeds -gt 0) {
        Step-Fail 'launcher' "install.cmd holds $nonAscii non-ASCII byte(s) and $bareLineFeeds line(s) ended by LF alone (cmd.exe wants ASCII and CRLF)"
    } else {
        Step-Pass 'launcher' 'install.cmd is ASCII, CRLF throughout'
    }
}

# The notices package-installers.sh puts in the archive; the runtimes to expect
# are read from the applications themselves (GAP-52).
Step-Notices 'notices' $archiveRoot

# ---------------------------------------------------------------------------- #
# 3. install.cmd -- what a double-click runs
# ---------------------------------------------------------------------------- #
# The launcher starts install.ps1 on Windows PowerShell under an execution policy
# of its own, whatever PowerShell runs this smoke and whatever the machine's
# policy is; -InstallDir going through proves its arguments do.
Write-Section 'install.cmd'
$installPs1 = Join-Path $archiveRoot 'install.ps1'
$binDir = Join-Path $installDir 'bin'
$installFailed = $false
$launched = Invoke-CmdCaptured -Label 'install-cmd' -CommandPath $installCmd -Arguments @('-InstallDir', $installDir) -WorkingDirectory $archiveRoot
foreach ($line in @($launched.StdOut -split "`r?`n" | Where-Object { $_ })) { Write-Info $line }
if ($launched.ExitCode -ne 0) {
    Write-Tail $launched.ErrFile 20
    Step-Fail 'install' "install.cmd exited $($launched.ExitCode) (expected 0)"
    $installFailed = $true
} elseif (-not (Test-Path -LiteralPath (Join-Path $binDir 'orkeon.cmd'))) {
    Step-Fail 'install' "install.cmd exited 0 but $binDir\orkeon.cmd does not exist"
    $installFailed = $true
} else {
    Step-Pass 'install' "install.cmd installed to $installDir"
}

if ($installFailed) {
    Write-Host 'SMOKE FAILED (nothing installed, later steps are moot)' -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------- #
# 3b. install.ps1 again, over the installation in place
# ---------------------------------------------------------------------------- #
# A reinstall replaces: the same tree, one PATH entry, the same Add/Remove
# Programs entry. Run in this PowerShell -- the first install ran in the one
# install.cmd picks -- so that both editions execute install.ps1.
Write-Section 'install.ps1, over the installation in place'
$treeFirst = @(Get-TreeListing $installDir)
try {
    & $installPs1 -InstallDir $installDir
    $treeSecond = @(Get-TreeListing $installDir)
    $difference = Compare-TreeListing $treeFirst $treeSecond
    if ($difference) {
        Step-Fail 'reinstall' "the second install did not leave the tree of the first: $difference"
    } else {
        Step-Pass 'reinstall' "replaced in place, $($treeSecond.Count) files, the same as after the first install"
    }
} catch {
    Step-Fail 'reinstall' "install.ps1 threw over an existing install: $($_.Exception.Message)"
}

$pathEntriesForBin = @((Get-UserPathRaw) -split ';' | Where-Object { $_ -eq $binDir })
if ($pathEntriesForBin.Count -eq 1) {
    Step-Pass 'reinstall-path' "$binDir is on the user PATH once"
} else {
    Step-Fail 'reinstall-path' "$binDir is on the user PATH $($pathEntriesForBin.Count) time(s) after two installs (expected 1)"
}

foreach ($travelled in 'install.ps1', 'install.cmd') {
    if (-not (Test-Path -LiteralPath (Join-Path $installDir $travelled))) {
        Step-Fail 'reinstall-copy' "$travelled was not copied into $installDir"
    }
}

# install.ps1 copies the notices only when the archive carries them, without a
# word otherwise: asserted again on the installed tree, which is what the user keeps.
Step-Notices 'notices-installed' $installDir

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
# 4c. A program of the installation is running: the installer refuses, intact
# ---------------------------------------------------------------------------- #
# Windows does not delete a running executable: without install.ps1's guard the
# delete-and-replace stops half-way through the tree. Orkeon Studio left open is
# the case a user meets; started here without --smoke-exit, it stays open.
Write-Section 'install.ps1 while Orkeon Studio is running'
$studioExe = Join-Path $installDir 'libexec\orkeon-studio\Orkeon.Studio.exe'
if (-not (Test-Path -LiteralPath $studioExe)) {
    Step-Fail 'running-install' "nothing to leave running: $studioExe is missing"
} else {
    $treeBefore = @(Get-TreeListing $installDir)
    $studioProcess = Start-Process -FilePath $studioExe -PassThru
    try {
        # Past its startup: a process that exits at once would prove nothing.
        Start-Sleep -Seconds 5
        if ($studioProcess.HasExited) {
            Step-Fail 'running-install' 'Orkeon Studio exited before the installer could meet it'
        } else {
            $named = [regex]::Escape("Orkeon.Studio (PID $($studioProcess.Id))")
            foreach ($attempt in @(
                    @{ Label = 'running-install';   What = 'install';   Script = $installPs1;                             Extra = @{} },
                    @{ Label = 'running-uninstall'; What = 'uninstall'; Script = (Join-Path $installDir 'install.ps1'); Extra = @{ Uninstall = $true } })) {
                $refusal = $null
                $extra = $attempt.Extra
                try {
                    & $attempt.Script -InstallDir $installDir @extra
                } catch {
                    $refusal = $_.Exception.Message
                }
                $difference = Compare-TreeListing $treeBefore @(Get-TreeListing $installDir)
                if (-not $refusal) {
                    Step-Fail $attempt.Label "the $($attempt.What) went ahead while Orkeon Studio was running"
                } elseif ($refusal -notmatch $named) {
                    Step-Fail $attempt.Label "refused, but without naming the running process: $refusal"
                } elseif ($difference) {
                    Step-Fail $attempt.Label "refused, but the installation changed: $difference"
                } else {
                    Step-Pass $attempt.Label "refused, names Orkeon.Studio (PID $($studioProcess.Id)), $($treeBefore.Count) files untouched"
                }
            }
        }
    } finally {
        # Close what this step opened, and whatever it started in turn, before
        # the steps below -- the real uninstall among them -- meet it.
        foreach ($leftover in @(Get-ProcessesUnder $installDir)) {
            Stop-Process -Id $leftover.Id -Force -ErrorAction SilentlyContinue
        }
        $deadline = (Get-Date).AddSeconds(30)
        while (@(Get-ProcessesUnder $installDir).Count -gt 0 -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
        }
    }

    $stillThere = Invoke-Orkeon -Label 'version-after-refusal' -Arguments @('--version')
    if ($stillThere.ExitCode -eq 0 -and $stillThere.StdOut -match [regex]::Escape($version)) {
        Step-Pass 'running-intact' "orkeon --version still answers $version"
    } else {
        Write-Tail $stillThere.ErrFile
        Step-Fail 'running-intact' "orkeon --version exited $($stillThere.ExitCode) after the refusals: '$($stillThere.StdOut.Trim())'"
    }
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
        # Enumerated one by one: Windows PowerShell's ConvertFrom-Json hands a JSON
        # array down the pipeline as a single object, PowerShell 7 as its items.
        $parsed = $doctor.StdOut | ConvertFrom-Json
        $results = @($parsed | ForEach-Object { $_ })
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
# 6b. The install channel: the marker the packaging wrote, read back by the CLI
# ---------------------------------------------------------------------------- #
Write-Section 'orkeon --version --verbose'
$channel = Invoke-Orkeon -Label 'version-verbose' -Arguments @('--version', '--verbose')
$channelLines = @($channel.StdOut -split "`r?`n" | Where-Object { $_ -like 'channel: *' })
if ($channel.ExitCode -ne 0) {
    Write-Tail $channel.ErrFile
    Step-Fail 'install-channel' "exit $($channel.ExitCode) (expected 0)"
} elseif ($channelLines.Count -ne 1 -or $channelLines[0] -cne 'channel: zip') {
    Write-Tail $channel.OutFile 5
    Step-Fail 'install-channel' "expected 'channel: zip', got '$($channelLines -join ' | ')'"
} else {
    Step-Pass 'install-channel' 'the install names its channel: zip'
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
