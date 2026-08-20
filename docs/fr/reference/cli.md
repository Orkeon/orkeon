> 🇬🇧 [English version](../../reference/cli.md)

# Référence du CLI `orkeon`

L'outil en ligne de commande `orkeon` est le point d'entrée principal du framework : il exécute les crews YAML et les scripts TypeScript (`.ork.ts`), génère une configuration, sonde les fournisseurs LLM, pilote le sous-système RAG et diagnostique une installation. Il est construit depuis `src/scripting/Orkeon.Scripting.Cli` et se packe comme dotnet tool `orkeon` :

```bash
dotnet tool install --global Orkeon.Scripting.Cli --prerelease
orkeon doctor
```

Les binaires de release et les installeurs (zip/MSI Windows, paquet Debian, tarballs macOS) embarquent le même CLI, autonome — aucun SDK .NET requis. Voir [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md).

**Codes de sortie** (stables) : `0` OK · `1` erreur de script/config (fichier manquant, script invalide, échec de validation) · `2` erreur runtime inattendue · `130` annulé par Ctrl+C.

## `orkeon run`

```bash
orkeon run <crew.ork.ts | crew.yaml | répertoire-crew/> [options]
```

Exécute une définition de crew et imprime son résultat sur stdout. Le dispatch dépend de la cible : `.ork.ts`/`.js` part vers l'hôte de scripting (transpilation esbuild + Jint) ; `.yaml`/`.yml` — ou un répertoire contenant un crew YAML multi-fichiers (`config.yaml` + `agents/` + `tasks/`, ou le triplet plat `crew.yaml`/`agents.yaml`/`tasks.yaml`) — part vers le runner YAML one-shot partagé.

| Option | Description |
|---|---|
| `-s, --settings <chemin>` | Chemin vers `appsettings.json`. Sans elle, une chaîne de repli s'applique (ci-dessous). |
| `-m, --mount <spec>` | Montage VFS, format Docker `<physique>:<virtuel>:<droits>[;sous-chemin:droits]`. Répétable. |
| `--allow-external-mounts` | Autorise les montages hors de la racine du workspace (ou `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`). |
| `-v, --verbose <0-2>` | `0` silencieux, `1` échanges LLM & outils, `2` debug complet. |
| `--llm-log` / `--llm-log-path <rép>` | Journalise les échanges LLM complets en JSONL (répertoire par défaut `./llm-logs`). |
| `--inputs <json>` / `--inputs-file <chemin>` | Entrées structurées pour les **scripts** (variable globale `inputs`). |
| `-V, --var CLE=VALEUR` | Variable pour le `CrewInput` d'un **crew YAML** (gabarits de tâche `{CLE}`). Répétable. |
| `--initial-context <texte>` | Contexte initial passé au `CrewInput` d'un **crew YAML**. |
| `--memory-limit-mb <n>` | Plafond mémoire Jint pour cette exécution (`0` le désactive). |
| `--validate` | Dry run : résout les settings, construit l'hôte, charge le crew avec résolution stricte des outils — aucun appel LLM, aucun kickoff. Imprime `VALIDATION OK/FAILED: …`. |
| `--list-tools` | Construit l'hôte, imprime le registre trié des outils runtime, puis sort. Aucun chemin de crew requis. |

```bash
orkeon run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount ./out:/output:rw -v 1
```

**Résolution des settings** — sans `--settings`, le CLI suit une chaîne de repli : `appsettings.json` à côté du fichier de crew, puis un `appsettings/appsettings.json` trouvé en remontant les répertoires parents, puis le fichier global par utilisateur écrit par `orkeon init`, puis les variables d'environnement `ORKEON_*` seules. Détails et matrice de profils prêts à l'emploi : [Exécuter votre premier exemple](../getting-started/run-your-first-example.md).

## `orkeon forge`

```bash
orkeon forge "résumer chaque matin les nouvelles offres de mon fournisseur"   # partir d'un besoin
orkeon forge                                   # ouvrir sur l'entretien
orkeon forge list                              # lister les sessions du workspace
orkeon forge resume <slug>                     # reprendre une session exactement là où elle s'est arrêtée
orkeon forge promote <slug> --to <dir>         # sortir une session prête en dossier ordinaire
```

L'Atelier : un parcours guidé du besoin en langage naturel à l'équipe déployable. Un assistant vous interroge et capte un brief structuré — objectif, entrées, **critères d'acceptation**, un exemple d'entrée — puis propose un plan d'équipe, le rend, le valide, **l'essaie en bac à sable sur votre exemple**, et juge le résultat **contre vos propres critères**. Non conforme ? Le diagnostic alimente une boucle de correction, bornée par un budget dur (itérations, jetons, temps). Chaque session vit sous `.orkeon/forge/<slug>/` — reprenable, diffable entre tentatives, auditable.

Démarrer ou reprendre un cycle exige un LLM configuré (`orkeon init`) : la forge refuse d'ouvrir l'entretien sans lui (`FORGE-LLM-UNAVAILABLE`) plutôt que de dégrader en silence. `list` et `promote` sont entièrement hors ligne.

| Option | Description |
|---|---|
| `--format yaml\|script` | Format rendu (défaut `yaml`). `script` rend un `crew.ork.ts` éditable et exige esbuild — absent, une nouvelle session retombe sur YAML avec `FORGE-ESBUILD-MISSING`. Le format d'une session ne change jamais en reprise. |
| `--events jsonl` | Émet le protocole d'événements versionné sur stdout au lieu du rendu terminal ; les réponses descendent sur stdin (c'est ainsi qu'Orkeon Studio pilote la forge). |
| `--auto` | Arbitre les verdicts non conformes sans humain, dans les limites du budget. |
| `--dry` | S'arrête après la validation — génère et valide, n'exécute jamais. Reprenez sans `--dry` pour essayer. |
| `--max-iterations <n>` / `--max-tokens <n>` / `--max-seconds <n>` | Le budget (défaut 3 itérations ; `0` = jetons/temps illimités). Une reprise peut le relever ; la consommation est toujours reportée. |
| `-s, --settings <path>` | Mêmes sémantiques qu'`orkeon run`. |
| `--pack <dir>` | Surcharge le pack de prompts embarqué. |
| `--to <dir>` | *(promote)* Dossier de destination ; doit être inexistant ou vide. |
| `--schedule daily@HH:mm\|hourly` | *(promote)* Génère les artefacts de planification sous `schedule/` — XML de tâche Windows, timer systemd, ligne cron. La commande d'installation est **affichée, jamais exécutée** : Orkeon n'a pas d'ordonnanceur. |
| `--with-settings` | *(promote)* Copie le fichier de settings résolu dans le dossier. Off par défaut — un settings porte souvent des clés API et le dossier est fait pour être partagé. |

Le bac à sable : l'essai tourne in-process avec les écritures confinées au montage `/output` de la session, et `shell_command`/`code_interpreter` retirés du catalogue d'outils — le plan d'équipe ne peut nommer que des outils que la validation acceptera.

Le dossier promu est ordinaire : `crew/` (ou `crew/crew.ork.ts`), `run.sh`/`run.cmd` composés contre la grammaire d'`orkeon run` avec vos entrées d'exemple pré-remplies, et `FORGE.md` — la carte d'identité de l'équipe (objectif, critères d'acceptation, verdict, version), écrite dans la langue de l'entretien. `orkeon run <dir>/crew` le lance ; le lanceur Studio le détecte.

## `orkeon init`

Assistant de configuration. Génère un `appsettings.json` valide au chemin global par utilisateur (`%APPDATA%\Orkeon\appsettings.json` sous Windows, `~/.config/Orkeon/appsettings.json` sous Linux/macOS) via un assistant interactif à 5 choix — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` — ou en mode non interactif par flags, puis sonde l'endpoint (sauf `--no-probe`).

| Option | Description |
|---|---|
| `-p, --provider <preset>` | `ollama` \| `docker-model-runner` \| `openai` \| `custom` \| `none`. |
| `-u, --base-url <url>` / `-m, --model <id>` | Endpoint et modèle. Requis pour `custom` ; les presets ont leurs défauts. |
| `-k, --api-key-env <nom>` / `--api-key <valeur>` | Variable d'environnement portant la clé, ou clé à stocker. |
| `--path <fichier>` | Écrire ailleurs qu'au chemin global par utilisateur. |
| `-f, --force` | Écraser un fichier existant. |
| `--no-probe` | Sauter la sonde de l'endpoint. |

```bash
orkeon init --provider ollama --model llama3.2 --no-probe
```

## `orkeon llm`

Deux verbes contre un endpoint fournisseur réel.

**`orkeon llm probe`** — déroule le protocole de test LLM contre un fournisseur et archive éventuellement la trace de campagne. Options clés : `-p, --provider` (requis : `openai | anthropic | ollama | azure | groq | together | qwen | deepseek | kimi | mistral | huggingface | zai | gemini`), `-m, --model`, `-u, --base-url` (requis pour Azure), `--api-version` (mode déploiement Azure), `-k, --api-key-env` (défaut `ORKEON_LLM_API_KEY` — la clé elle-même n'est jamais acceptée sur la ligne de commande), `--modes` (séparés par des virgules, ex. `M1,M2,M8` ; défaut : tous), `--archive <rép>`, `--format md|json`, `--commit`, `--timeout` (secondes, défaut 180), `--temperature` (défaut 0).

**`orkeon llm models`** — liste les modèles servis par un fournisseur. Options : `-p, --provider` (requis), `-u, --base-url`, `-k, --api-key-env`, `-f, --filter` (glob shell), `--json`.

```bash
ORKEON_LLM_API_KEY=... orkeon llm probe -p deepseek --modes M1,M2 --format json
orkeon llm models -p ollama --filter 'llama*'
```

## `orkeon rag`

Trois verbes sur le sous-système RAG (`ingest`, `search`, `eval`). Tous partagent les options d'hôte de `run` : `-s/--settings`, `-m/--mount`, `--allow-external-mounts`, `-v/--verbose`. Les sources relatives se résolvent contre un montage automatique `{cwd} → /workspace:ro` ; l'état atterrit dans `{cwd}/.orkeon → /output:rw`.

**`orkeon rag ingest`** — ingestion incrémentale (les sources inchangées sont sautées) : `-c, --collection` (requis), `--source <chemin|glob>` (requis, répétable), `--chunking recursive|sentence|structural|semantic`, `--reindex` (réindexation complète — seule issue après un changement de modèle/dimension d'embedding).

**`orkeon rag search`** — pose une question, imprime la réponse fondée avec citations et scores : `<question>` positionnelle, `-c, --collection` (requis), `--top-n` (défaut 5).

**`orkeon rag eval`** — évalue une collection contre un jeu de données de référence (recall@k, MRR, groundedness) et écrit des rapports markdown/JSON : `-d, --dataset` (requis), `-c, --collection`, `--profile` ou `--compare fast,balanced,…`, `-k` (défaut 5), `--llm-judge`, `--offline` (zéro réseau : stub extractif déterministe, aucune clé LLM nécessaire), `--no-ingest`, `--reindex`, `--min-recall` / `--min-mrr` (portes anti-régression, sortie 1 sous le seuil), `--output` (défaut `/output/rag/eval`).

```bash
orkeon rag eval --dataset examples/rag/eval/golden.yaml \
  --compare fast,balanced,quality,corrective,adaptive --offline
```

## `orkeon doctor`

Diagnostic d'installation : dit en moins de 15 secondes ce qui fonctionne et ce qui manque, en table ✅/⚠️/❌ ou en `--json` (schéma stable `{check, status, detail}` pour la CI). Neuf vérifications : `dotnet-runtime`, `appsettings`, `llm-config`, `llm-reachability`, `esbuild`, `local-embeddings`, `onnx-reranker`, `tree-sitter`, `workspace-write`. Codes de sortie : `0` tout vert ou avertissements seuls, `1` au moins une vérification en échec.

```bash
orkeon doctor --json
```

## `orkeon-repl` — la console interactive séparée

`orkeon-repl` est un **outil distinct** construit depuis `src/apps/Orkeon.ConsoleApp` (commande dotnet tool `orkeon-repl`) : un REPL interactif complet pour piloter agents, crews et outils depuis une console Terminal.Gui à deux volets (logs + REPL), avec toute la pile du framework câblée — outils intégrés, RAG, analyse de code, embeddings locaux — et des commandes scriptées en TypeScript. Il ne partage volontairement pas le nom d'assembly `orkeon`. Voir [Commandes CLI en TypeScript](../architecture/cli-ts-commands.md).

---

> **Voir aussi** : [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Exécuter votre premier exemple](../getting-started/run-your-first-example.md) ·
> [Retour à l'index](../INDEX.md)
