# Migration — Dépôt `orkeon` (public)

> **Ce dossier (racine du monorepo, _tout sauf `project/`_) devient le dépôt
> [`Orkeon/orkeon`](https://github.com/Orkeon/orkeon)** — maintenu **privé**, basculé **public** à
> l'ouverture open-source.
> **Produit :** packages NuGet (libs), outil `orkeon` (dotnet tool), lib runner `Orkeon.Runners.Shared`.
>
> Ce `migration.md` pilote la migration de **ce** dépôt. Il peut être **supprimé** une fois la
> migration terminée (cf. Phase D).

**Ordre global des 3 dépôts** (dépendance de packages) :
**1. `orkeon` (ce dépôt, jusqu'à la publication des packages)** → 2. `experiments` (le consomme) →
3. `backstage` (indépendant). Voir `project/PLAN-DECOUPAGE-3-REPOS.md` pour le cadrage stratégique.

---

## 0. Prérequis (une fois)

| Outil | État | Action |
|---|---|---|
| `gh` (GitHub CLI) | ✅ présent | `gh auth status` → compte membre de l'org `Orkeon`, droit push |
| `gitleaks` | ❌ **absent** localement | installer avant tout push (voir ci-dessous) |
| `.NET SDK 10` | requis | `dotnet --version` ≥ `10.0.x` |

```bash
# gitleaks — au choix :
sudo apt-get update && sudo apt-get install -y gitleaks            # si dispo
GL=8.18.4 ; curl -sSL "https://github.com/gitleaks/gitleaks/releases/download/v${GL}/gitleaks_${GL}_linux_x64.tar.gz" | sudo tar -xz -C /usr/local/bin gitleaks
docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:latest detect --source /repo --no-git  # sans rien installer
```

---

## Phase A — Préparation dans le monorepo

> Modifs faites dans `/workspace`, puis **commitées** (la copie Phase B part de `HEAD`).

### A1. Branding NuGet — `src/Directory.Build.props`
- `Description` = *« C# port of Python Orkeon … »* → **« Build and orchestrate AI agent teams in .NET »**.
- **Corriger l'URL** : `RepositoryUrl` / `PackageProjectUrl` valent `https://github.com/orkeon-net/orkeon`
  → remplacer par **`https://github.com/Orkeon/orkeon`**.
- Vérifier `PackageLicenseExpression=MIT`, `Authors=Orkeon Contributors`. Option `Copyright (c) 2024` → `2024-2026`.
- Aligner `README.md` (*Acknowledgements*) / `CHANGELOG.md` sur « réimplémentation indépendante » (pas « port »).

### A2. Surface packageable
- **Lib runner** — `examples/runners/_shared/Orkeon.Examples.Shared.csproj` : ajouter
  ```xml
  <PropertyGroup>
    <PackageId>Orkeon.Runners.Shared</PackageId>
    <IsPackable>true</IsPackable>
    <Description>Shared host/bootstrap for Orkeon runners.</Description>
  </PropertyGroup>
  ```
- **Tests non packagés** : si absent, créer `tests/Directory.Build.props` avec `<IsPackable>false</IsPackable>`
  (sinon `dotnet pack Orkeon.sln` émet des packages de test parasites).
- **Déjà OK** : `Orkeon.ConsoleApp` (`IsPackable=false`), `Orkeon.Generators` & `Orkeon.Compliance.Vfs`
  (analyzers `DevelopmentDependency`), `Orkeon.Scripting.Cli` (`PackAsTool=true`, `ToolCommandName=orkeon`).

### A3. Étendre la publication des packages
`release.yml` **et** `ci.yml` ne packagent que **3** projets. Or `Orkeon.Infrastructure` référence
`Orkeon.Analysis`, `Orkeon.Analysis.Abstractions`, `Orkeon.Tools.Abstractions` **sans `PrivateAssets`**
→ ces packages **doivent** être publiés, sinon le `restore` du consommateur (`experiments`) casse.
```bash
dotnet pack Orkeon.sln -c Release -o artifacts /p:ContinuousIntegrationBuild=true   # src (libs + outil), tests exclus
dotnet pack examples/runners/_shared/Orkeon.Examples.Shared.csproj -c Release -o artifacts   # lib runner (hors Orkeon.sln)
```
> `Orkeon.sln` contient `src/` + `tests/` (pas `examples/`, solution séparée) → d'où le 2ᵉ `pack` explicite.

### A4. Réparer le build des exemples — `examples/runners/interactive/Program.cs` (~l.46)
- `opts.ParsedVariables` n'existe pas → `opts.ParseVariables()`
  (cf. `src/hosting/Orkeon.Hosting/RunnerOptionsBase.cs:90`) :
  ```csharp
  var vars = new Dictionary<string, string>(opts.ParseVariables(), StringComparer.Ordinal);
  ```
- Corriger **CA1826** (`.First()`/`.Last()` sur `IEnumerable`), **CA1849** (sync→async), **CA2016** (propager le `CancellationToken`).
- Vérifier : `dotnet build examples/Orkeon.Examples.sln -c Release /p:TreatWarningsAsErrors=true`.

### A5. `THIRD-PARTY-NOTICES.md` — ajouter **Jint** (BSD-2-Clause).
### A6. (Option, non bloquant) template `dotnet new orkeon-runner` (`src/templates/Orkeon.Templates/`).

### ✅ Vérification + commit
```bash
cd /workspace
dotnet build Orkeon.sln -c Release && dotnet test Orkeon.sln -c Release
dotnet pack Orkeon.sln -c Release -o /tmp/pkgcheck
dotnet pack examples/runners/_shared/Orkeon.Examples.Shared.csproj -c Release -o /tmp/pkgcheck
ls /tmp/pkgcheck/*.nupkg     # Domain, Application, Infrastructure, Analysis(.Abstractions), Hosting, Tools.*, Cli.*, Scripting(.Cli), Plugins, Orkeon.Runners.Shared — PAS de *.Tests
unzip -p /tmp/pkgcheck/Orkeon.Infrastructure.*.nupkg '*.nuspec' | grep -A30 '<dependencies>'
git add -A && git commit -m "chore: prep découpage orkeon (branding, packaging, exemples, notices)"
```

---

## Phase B — Copie propre (« historique neuf »)

> ⚠️ **Ne pas faire `cp -r`** du monorepo : tu embarquerais `.env.sonar`/`.env.sonarqube`
> (secrets), ~12 Mo de `build-audit-*.log`, `bin/ obj/ .sonarqube/ artifacts/ TestResults/`, etc.
> **`git archive HEAD` n'exporte que les fichiers SUIVIS** → propre par construction.

```bash
DEST=../orkeon
rm -rf "$DEST" && mkdir -p "$DEST"
git -C /workspace archive --format=tar HEAD | tar -x -C "$DEST"
cd "$DEST"

# B1. Retirer ce qui appartient aux 2 autres dépôts + parasites (ceinture-bretelles si commités)
rm -rf project analysis archive .claude sonarqube docs/audit
rm -f GO-NOGO-PUBLICATION-OPENSOURCE*.md d1a-ca1024.txt d1a-ca1716.txt build-audit-*.log build-*.log
# Décision CLAUDE.md : le GARDER (conventions de contrib, le README y renvoie) ou `rm -f CLAUDE.md`.
```

### B2. Vérifier qu'il ne reste que le produit
```bash
find . -maxdepth 1 | sort     # attendu : .config .github docs examples nuget scripts src tests tools + fichiers OSS racine + migration.md
grep -rIl -iE 'persona|positioning|fork history' . 2>/dev/null     # idéalement vide
find . -type f -size +1M -not -path './.git/*'                      # idéalement vide
```

### B3. Build/test de la copie + scan secrets
```bash
dotnet build Orkeon.sln -c Release
dotnet test  tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj -c Release
gitleaks detect --source . --no-git --config .gitleaks.toml          # → "no leaks found"
```

---

## Phase C — Push vers le dépôt **existant** & CI

```bash
git init -b main
git add -A
git commit -m "Initial public release — Orkeon 0.9.0-beta"
gitleaks detect --source . --log-opts=--all --config .gitleaks.toml   # scan historique (1 commit)
git ls-files | grep -iE 'project/|^analysis/|archive/|\.claude/|sonarqube/'   # → vide

# Dépôt Orkeon/orkeon DÉJÀ créé → rattacher le remote et pousser :
gh repo edit Orkeon/orkeon --visibility private --accept-visibility-change-consequences 2>/dev/null || true
git remote add origin https://github.com/Orkeon/orkeon.git
git push -u origin main      # ajouter --force si le dépôt distant a déjà un commit initial auto-généré
```

### C1. Secrets Actions (Settings → Secrets and variables → Actions)
| Secret | Usage |
|---|---|
| `NUGET_API_KEY` | Push NuGet.org (phase 2 OSS — `release.yml`) |
| `SONAR_TOKEN`, `SONAR_HOST_URL` | Quality Gate (`sonar.yml`) si conservé |
| `CODECOV_TOKEN` | Couverture (`ci.yml`) si conservé |
> Phase 1 (GitHub Packages) n'a **pas** besoin de secret : `GITHUB_TOKEN` est fourni aux workflows (`permissions: packages: write`).

### C2. Workflow `.github/workflows/publish.yml` (GitHub Packages, tag `v*`)
```yaml
name: Publish packages
on: { push: { tags: [ 'v*' ] } }
permissions: { contents: write, packages: write }
jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore Orkeon.sln
      - run: dotnet build Orkeon.sln -c Release --no-restore
      - run: dotnet test  Orkeon.sln -c Release --no-build
      - run: dotnet pack  Orkeon.sln -c Release -o artifacts /p:ContinuousIntegrationBuild=true
      - run: dotnet pack  examples/runners/_shared/Orkeon.Examples.Shared.csproj -c Release -o artifacts
      - run: >
          dotnet nuget push "artifacts/*.nupkg"
          --source "https://nuget.pkg.github.com/Orkeon/index.json"
          --api-key ${{ secrets.GITHUB_TOKEN }} --skip-duplicate
```

### C3. Réglages dépôt + premier tag
- Branch protection sur `main` (PR + checks `build`, `format-check`, `build-examples`, `gitleaks`, Sonar).
- GitHub Pages pour `docfx.yml` (source = GitHub Actions).
```bash
git tag v0.9.0-beta && git push origin v0.9.0-beta
# Onglet "Packages" de l'org Orkeon → vérifier tous les packages, dont Orkeon.Runners.Shared + Orkeon.Scripting.Cli.
```
✅ **Jalon bloquant pour `experiments`** : packages visibles sur GitHub Packages avant d'attaquer `project/experiments/migration.md`.

---

## Phase D — Bascule open-source (phase 2)

Après levée des bloquants du Go/No-Go (licence SmartComponents, marque résiduelle, contenu interne) :
1. **Supprimer ce `migration.md`** (artefact interne) + re-scan complet (`gitleaks`, grep branding).
2. `gh repo edit Orkeon/orkeon --visibility public --accept-visibility-change-consequences`.
3. Basculer la publication vers **NuGet.org** (réactiver `release.yml` + `NUGET_API_KEY`) → lecture
   anonyme, plus de PAT côté `experiments`.
4. Mettre à jour badges README (NuGet.org) + doc d'install.

---

## Checklist de sortie
- [ ] Phase A commitée (branding + URL `Orkeon/orkeon`, `Orkeon.Runners.Shared`, pack étendu, exemples verts, Jint).
- [ ] Copie via `git archive` (PAS `cp -r`) ; B2 vert ; aucun `.env.*`/log/`project/` embarqué.
- [ ] `gitleaks` : *no leaks found* (arbre + historique).
- [ ] Push vers `Orkeon/orkeon` (privé) ; CI verte.
- [ ] `publish.yml` ajouté ; tag `v0.9.0-beta` → packages visibles.
