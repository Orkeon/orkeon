<#
.SYNOPSIS
    Auditeur pérenne du catalogue « 101 Cas d'Utilisation » (équivalent de
    validate-use-cases.sh / validate_use_cases.py).

.DESCRIPTION
    Vérifie que chaque identifiant back-tické des champs Outils / Process / Mémoire
    existe dans src/**/*.cs (type déclaré ou Name d'outil) ou est marqué 🔮, que tout
    Process cité appartient aux 6 ProcessType réels (+ mécanismes FlowEngine/A2A), et
    qu'aucune ligne Outils ne contient d'interface (I[A-Z]…). Code retour ≠ 0 si KO.

.EXAMPLE
    pwsh scripts/validate-use-cases.ps1
    pwsh scripts/validate-use-cases.ps1 path/to/101-USE-CASES.md src
#>
param(
    [string]$File = "project/marketing/content-strategy/101-USE-CASES.md",
    [string]$Src  = "src"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
    Write-Error "Catalogue introuvable : $File"; exit 2
}
if (-not (Test-Path -LiteralPath $Src -PathType Container)) {
    Write-Error "Dossier source introuvable : $Src"; exit 2
}

$AllowedProcess = @('Sequential','Hierarchical','Consensual','Parallel','Graph','Autonomous','FlowEngine','A2A')
$AllowedMemory  = @('InMemory','Redis','SQLite','EncryptedRedis','EncryptedSQLite','Composite')

# Pré-indexation de src : types déclarés + Name d'outils ("snake_case").
$index = [System.Collections.Generic.HashSet[string]]::new()
$typeRe = [regex]'(?:class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)'
$nameRe = [regex]'"([a-z_][a-z0-9_]+)"'
foreach ($cs in Get-ChildItem -LiteralPath $Src -Recurse -Filter *.cs -File) {
    $text = Get-Content -LiteralPath $cs.FullName -Raw
    foreach ($m in $typeRe.Matches($text)) { [void]$index.Add($m.Groups[1].Value) }
    foreach ($m in $nameRe.Matches($text)) { [void]$index.Add($m.Groups[1].Value) }
}

$fieldRe = [regex]'^- \*\*(Outils|Process|Mémoire)\*\*\s*:\s*(.*)$'
$tokenRe = [regex]'`([^`]+)`(\s*🔮)?'

$errors  = 0
$checked = 0

foreach ($line in Get-Content -LiteralPath $File) {
    $fm = $fieldRe.Match($line)
    if (-not $fm.Success) { continue }
    $field = $fm.Groups[1].Value
    $rest  = $fm.Groups[2].Value
    foreach ($tm in $tokenRe.Matches($rest)) {
        $tok     = $tm.Groups[1].Value
        $planned = $tm.Groups[2].Success
        $checked++
        switch ($field) {
            'Process' {
                if ($tok.EndsWith('Agent')) { break }
                if ($AllowedProcess -notcontains $tok) {
                    Write-Output "❌ Process invalide (hors 6 ProcessType + mécanismes) : `$tok`"
                    $errors++
                }
            }
            'Mémoire' {
                if ($planned) { break }
                if ($AllowedMemory -notcontains $tok) {
                    Write-Output "❌ Mémoire invalide : `$tok`"
                    $errors++
                }
            }
            'Outils' {
                if ($tok -match '^I[A-Z]') {
                    Write-Output "❌ Interface dans le champ Outils (doit aller en Features) : `$tok`"
                    $errors++; break
                }
                if ($planned) { break }
                $base = $tok.Split('<')[0]
                if (-not $index.Contains($base)) {
                    Write-Output "❌ Outil introuvable dans $Src : `$tok`"
                    $errors++
                }
            }
        }
    }
}

Write-Output "—"
Write-Output "Identifiants contrôlés (Outils/Process/Mémoire) : $checked"
if ($errors -eq 0) {
    Write-Output "✅ Catalogue cohérent avec le code source."
    exit 0
} else {
    Write-Output "❌ $errors incohérence(s) détectée(s)."
    exit 1
}
