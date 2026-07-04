# Prompt : Test automatise des 101 exemples Orkeon

> Copie ce prompt dans Claude Code (container Docker).
> Pre-requis : Docker Desktop Models avec `ai/granite-4.0-h-tiny:latest` actif.

---

## Prompt

```
Tu travailles dans le repo Orkeon (.NET 10). Tu dois tester les 101 exemples dans examples/.

## Contexte

Le projet utilise un systeme de configuration centralise :
- `examples/_shared/appsettings.json` : config par defaut (localhost)
- `examples/_shared/appsettings.docker.json` : config Docker (host.docker.internal)
- `examples/_shared/appsettings.openai.json` : config OpenAI
- Les runners resolvent automatiquement : --settings > local appsettings.json > _shared/appsettings.json

Tu es dans un container Docker, donc il faut utiliser `appsettings.docker.json` via --settings.

## Phase 1 : Setup environnement

1. Verifie le .NET 10 SDK :
   ```bash
   dotnet --version
   ```
   Si absent :
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
   export PATH="$HOME/.dotnet:$PATH"
   ```

2. Verifie la connectivite au LLM :
   ```bash
   curl -sf http://host.docker.internal:12434/engines/llama.cpp/v1/models | head -20
   ```
   Tu dois voir une reponse JSON. Si ca echoue, essaie localhost:12434.

3. Determine quel endpoint fonctionne et note-le (DOCKER_HOST ou LOCALHOST).

## Phase 2 : Build

```bash
dotnet restore examples/Orkeon.Examples.sln
dotnet build examples/Orkeon.Examples.sln --configuration Release --verbosity quiet
```

Si le build echoue, corrige et documente.

## Phase 3 : Test des 101 exemples (mode load)

Cree un script bash qui teste les 101 exemples. Pour chaque exemple :

```bash
# Determine le runner
RUNNER="examples/runners/standard"
if [[ "$example" == 03-finance-trading/* ]]; then
  RUNNER="examples/runners/trading"
fi

# Lance avec --settings pour pointer vers le bon appsettings
timeout 60s dotnet run --project "$RUNNER" \
  --no-build --configuration Release \
  -- --config "examples/$example/config.yaml" \
     --settings "examples/_shared/appsettings.docker.json" \
  2>&1
```

### Criteres de succes (mode load)

- **PASS** : la sortie contient "Successfully created crew"
- **FAIL** : exit code != 0 sans "Successfully created crew"
- **TIMEOUT** : process tue apres 60s

Les warnings "Tool ... not found in registry" sont attendus (bug DI connu) : note-les mais ne les compte pas comme erreurs.

## Phase 4 : Test de 5 exemples en mode run (optionnel)

Si Phase 3 > 90% PASS, teste 5 exemples en execution complete (timeout 300s) :
1. `01-enterprise/01-research-assistant`
2. `01-enterprise/05-customer-support`
3. `05-education/56-adaptive-tutor`
4. `07-creative-media/76-narrative-studio`
5. `09-experimental/96-self-adaptive-crew`

Un PASS = exit code 0 et "Crew Output" dans la sortie.

## Phase 5 : Rapport

Genere `examples/test-reports/test-report-$(date +%Y-%m-%d_%H%M%S).md` :

```markdown
# Orkeon Examples Test Report

- **Date**: {date}
- **Environnement**: Docker container -> Docker Desktop Models
- **Modele**: ai/granite-4.0-h-tiny
- **Settings**: appsettings.docker.json
- **SDK**: .NET {version}

## Resume

| Metrique | Valeur |
|----------|--------|
| Total | 101 |
| PASS (load) | {n} |
| FAIL (load) | {n} |
| TIMEOUT | {n} |
| PASS (run) | {n}/5 |

## Par categorie

| Categorie | Pass | Fail | Total |
|-----------|------|------|-------|
| 01-enterprise | ... | ... | 15 |
| ... |

## Detail des echecs

Pour chaque FAIL : nom, stderr (20 lignes max), cause probable.

## Tools manquants

| Tool | Nb exemples affectes |
|------|---------------------|
| web_scrape | ... |

## Recommandations

Problemes systemiques et corrections a apporter.
```

## Regles

- Ne modifie PAS les fichiers .cs ni les config.yaml
- Si un exemple crash (DI, timeout...), note-le et continue
- Traite les 101 exemples sequentiellement
- Sauvegarde les sorties brutes dans examples/test-reports/raw/{example-slug}.log
```
