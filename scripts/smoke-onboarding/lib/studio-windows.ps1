<#
.SYNOPSIS
  Orkeon Studio assertions for the two Windows channels (STUDIO-08, spec §8.4).

.DESCRIPTION
  Dot-sourced, never executed. It is the PowerShell counterpart of
  lib\smoke-common.sh's smoke_step_studio_* helpers, and the single place the ZIP
  channel (run-smoke.ps1) and the MSI channel (the `msi` job in
  .github\workflows\release.yml) share their Studio checks — the two install to
  different roots but lay out the same tree underneath, so the assertions must
  not be written twice and allowed to drift.

  Both channels install:
    <root>\bin\orkeon-studio.cmd                  the launcher wrapper
    <root>\libexec\orkeon-studio\Orkeon.Studio.exe the self-contained WPF apphost

  and the check that matters is that the app actually *starts*: a WPF binary can
  be perfectly present and still fail to resolve its XAML, its runtime or its
  ViewModels. `orkeon-studio --smoke-exit` opens the window, waits for the first
  render and shuts down with code 0 (App.xaml.cs), which is what turns "the file
  is there" into "the app runs on a real Windows box".

  The caller decides what to do with the result; nothing here writes to the
  console or exits.
#>

# Invoke-OrkeonStudioWindowsSmoke — asserts the Studio payload of an installed
# tree and runs the headless start/exit smoke on it.
#
#   -InstallRoot              the install directory (the one holding bin\ and libexec\)
#   -LogDir                   where the --smoke-exit stdout/stderr captures go
#   -TimeoutSeconds           how long the window may take to open and close again
#   -ExpectStartMenuShortcut  also require the "Orkeon Studio" Start-menu shortcut
#                             (the MSI channel adds one; the ZIP channel does not)
#
# Returns [pscustomobject]@{ Problems = @(...); Notes = @(...) } — an empty
# Problems array is the pass.
function Invoke-OrkeonStudioWindowsSmoke {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$LogDir,
        [int]$TimeoutSeconds = 120,
        [switch]$ExpectStartMenuShortcut
    )

    $problems = New-Object System.Collections.ArrayList
    $notes = New-Object System.Collections.ArrayList

    $studioCmd = Join-Path $InstallRoot 'bin\orkeon-studio.cmd'
    $studioExe = Join-Path $InstallRoot 'libexec\orkeon-studio\Orkeon.Studio.exe'

    foreach ($path in @($studioCmd, $studioExe)) {
        if (-not (Test-Path -LiteralPath $path)) {
            [void]$problems.Add("missing: $path")
        }
    }

    if ($ExpectStartMenuShortcut) {
        # Per-user non-advertised shortcut in the ProgramMenuFolder root
        # (installers\msi\Package.wxs, StudioShortcutComponent).
        $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Orkeon Studio.lnk'
        if (Test-Path -LiteralPath $shortcut) {
            [void]$notes.Add("Start-menu shortcut registered: $shortcut")
        } else {
            [void]$problems.Add("no Start-menu shortcut at $shortcut")
        }
    }

    if ($problems.Count -gt 0) {
        # Nothing to launch, or launching would only produce a second, derived
        # failure. Report what is actually wrong.
        return [pscustomobject]@{ Problems = @($problems); Notes = @($notes) }
    }

    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $outFile = Join-Path $LogDir 'studio-smoke-exit.out.txt'
    $errFile = Join-Path $LogDir 'studio-smoke-exit.err.txt'

    # Run it in a background job purely for the timeout: a GUI app that hangs on
    # startup would otherwise eat the whole job's budget and surface as an
    # unattributable workflow timeout instead of a named failing step. The
    # invocation itself is the same `& <launcher>` form run-smoke.ps1 already uses
    # for orkeon.cmd, so the .cmd wrapper is exercised, not bypassed.
    $job = Start-Job -ScriptBlock {
        param($Cmd, $Out, $Err)
        $ErrorActionPreference = 'Continue'
        & $Cmd '--smoke-exit' 1> $Out 2> $Err
        $LASTEXITCODE
    } -ArgumentList $studioCmd, $outFile, $errFile

    $completed = Wait-Job -Job $job -Timeout $TimeoutSeconds
    if (-not $completed) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        [void]$problems.Add("orkeon-studio --smoke-exit did not exit within $TimeoutSeconds s (window never closed?)")
        return [pscustomobject]@{ Problems = @($problems); Notes = @($notes) }
    }

    $exitCode = @(Receive-Job -Job $job -ErrorAction SilentlyContinue) | Select-Object -Last 1
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue

    if ($null -eq $exitCode -or $exitCode -ne 0) {
        $tail = ''
        if (Test-Path -LiteralPath $errFile) {
            $tail = (Get-Content -LiteralPath $errFile -Tail 5 -ErrorAction SilentlyContinue) -join ' / '
        }
        # A null exit code means the launcher never ran at all (the job died
        # before reaching $LASTEXITCODE) -- distinguish it from a real non-zero.
        if ($null -eq $exitCode) {
            $message = 'orkeon-studio --smoke-exit produced no exit code (launcher never ran?)'
        } else {
            $message = "orkeon-studio --smoke-exit exited $exitCode (expected 0)"
        }
        if ($tail) { $message = "$message -- $tail" }
        [void]$problems.Add($message)
    } else {
        [void]$notes.Add('orkeon-studio --smoke-exit opened and closed the window, exit 0')
    }

    return [pscustomobject]@{ Problems = @($problems); Notes = @($notes) }
}
