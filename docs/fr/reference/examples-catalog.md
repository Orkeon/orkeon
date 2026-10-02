> 🇬🇧 [English version](../../reference/examples-catalog.md)

> **Voir aussi** : [Retour à l'index](../INDEX.md)

# Catalogue des exemples

Tout ce qui vit sous `examples/` tourne sur le même moteur ; cette page est la carte
éditoriale. L'**inventaire faisant foi** — chaque exemple numéroté avec son type de
process, ses comptes agents/tâches et ses outils — est le
[`examples/INDEX.md`](https://github.com/Orkeon/orkeon/blob/main/examples/INDEX.md) généré : il est produit par
`scripts/generate_examples_index.py` et la CI échoue dès qu'il dérive des dossiers
sur disque, si bien qu'aucun compte n'est maintenu à la main ici.

Le même générateur écrit son jumeau lisible par machine,
[`examples/usecases.json`](https://github.com/Orkeon/orkeon/blob/main/examples/usecases.json) :
une entrée par exemple numéroté, qui joint ce que déclare son crew (process, outils,
s'ils vont sur Internet ou exigent une clé tierce) à sa fiche `usecase.yaml` rédigée à
la main (titre et formulation du problème en cinq langues, tags, montages, possibilité
de l'importer comme équipe). La CI échoue aussi quand il dérive ; le format de la fiche
est décrit dans [`examples/README.md`](https://github.com/Orkeon/orkeon/blob/main/examples/README.md#use-case-sheet-usecaseyaml).

## Les neuf catégories métier

Les crews numérotés vivent dans neuf dossiers thématiques. La plupart sont des crews YAML
(`config.yaml`) ; les quinze de `03-finance-trading/` sont des crews TypeScript
(`main.ork.ts`, lancés par le même `orkeon run`) qui partagent le module `_tools/` de
cette catégorie. La numérotation est historique et non contiguë.

| Catégorie | Dossier | Focus |
|-----------|---------|-------|
| **01 — Entreprise** | `01-enterprise/` | Recherche, revue de code, e-mail, rapports, support, due diligence, onboarding, Q&R interactif |
| **02 — Science & Recherche** | `02-science-research/` | Méta-analyse, débat scientifique, génomique, rédaction de subventions, graphes de connaissances |
| **03 — Finance & Trading** | `03-finance-trading/` | Trading algorithmique, détection de fraude, KYC/AML, consensus de portefeuille, ESG, stress tests |
| **04 — Santé & Bien-être** | `04-health-wellness/` | Aide au diagnostic, nutrition, essais cliniques, pharmacovigilance, télémédecine |
| **05 — Éducation** | `05-education/` | Tutorat adaptatif, génération d'examens, notation, gamification, mentorat |
| **06 — Ingénierie & DevOps** | `06-engineering-devops/` | CI/CD, réponse à incident, migration de base de données, chaos engineering, crews sur des bases de code TypeScript |
| **07 — Créatif & Médias** | `07-creative-media/` | Narration, podcast, musique, direction artistique, worldbuilding, newsletters |
| **08 — IoT & Systèmes intelligents** | `08-iot-smart-systems/` | Maison connectée, flotte, agriculture de précision, énergie, maintenance prédictive |
| **09 — Expérimental** | `09-experimental/` | Crews auto-adaptatifs, négociation, jury éthique, crew de crews, orchestration en graphe |

## Au-delà des crews numérotés

| Dossier | Ce qu'il montre |
|---------|-----------------|
| `rag/` | Le sous-système RAG : `basic-ingestion/`, `hybrid-retrieval/`, `custom-reranker/`, `crew-yaml/`, plus le jeu d'évaluation offline sous `eval/` |
| `raggable-tree/` | Analyse sémantique de code : `basic-indexing/`, `crew-yaml/`, `custom-adapter/` |
| `quickstart/` | Le crew en deux minutes du README : un agent, un `file_write`, un modèle Ollama local, un montage en écriture |
| `scripting/` | Le DSL TypeScript (`.ork.ts`), de `01-hello-world` à `13-email-triage` : spawn dynamique, littéraux FSM/graphe, outils et hooks custom, RAG (`08-rag.ork.ts`), entrées et mémoire, événements ; `crew-review-desk/` est un crew complet écrit en TypeScript avec son propre module d'outils |
| `cli-ts-commands/` | Commandes REPL interactives en `*.cmd.ts`, chargées sans recompilation .NET |
| `local-embeddings/` | Embeddings BGE-micro-v2 locaux (sans réseau, sans clé API) : trois extraits classés face à une requête par similarité cosinus |
| `crew-multifile/` | Un crew décrit comme un dossier (`orkeon run <dir>`) |
| `forge/promote-demo/` | La moitié hors ligne de l'Atelier : une session `orkeon forge` prête, livrée comme workspace — `forge list`, puis `forge promote` vers un dossier ordinaire (aucun LLM requis) |
| `service-host/` | Le matériel d'exemple du daemon `orkeon-host` : un README et `appsettings.host.json` |
| `run-events/` | Le protocole `orkeon run --events jsonl` : README, `sample-stream.jsonl`, `watch-run.py` |
| `aspire/AppHost/` | Un AppHost .NET Aspire (`Orkeon.Hosting.Aspire`) qui lance le crew du quickstart comme ressource, ses spans, métriques et logs dans le tableau de bord |
| `interop/agent-framework/` | `Orkeon.Interop.AgentFramework` dans les deux sens : un crew enveloppé en `AIAgent` Microsoft Agent Framework, et un agent MAF confié à un agent Orkeon comme outil |
| `09-experimental/llm-response-format/`, `09-experimental/streaming-demo/` | Deux démos non numérotées dans la catégorie expérimentale : la sortie structurée (`response_format`) et un run de crew diffusé au fil de l'eau (`KickoffStreamingAsync`) |
| `appsettings/` | La matrice de profils de settings partagée : un `appsettings.json` plus un `*.local.json.example` par fournisseur |
| `others/` | Le README d'un corpus de benchmark de vingt bases de code TypeScript pour la crew `102` (les bases de code elles-mêmes ne sont pas versionnées) |

**Vous cherchez l'e-mail ?** Deux exemples, de deux natures différentes :

- [`scripting/13-email-triage.ork.ts`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.ork.ts) travaille sur une **vraie boîte mail** : un compte Gmail joint par mot de passe d'application (déclaré dans `13-email-triage.appsettings.json`), il classe le courrier non lu par nature et laisse chaque réponse en brouillon — rien n'est envoyé. Pas à pas : [Donner une boîte aux lettres à vos agents](../getting-started/give-your-agents-a-mailbox.md).
- [`01-enterprise/03-email-pipeline`](https://github.com/orkeon/orkeon/blob/main/examples/01-enterprise/03-email-pipeline) ne se connecte à **aucune boîte** : ses agents lisent des fichiers d'e-mails avec `email_parser` et écrivent leur tri et leurs brouillons de réponse dans des fichiers.

## Exemples notables

- **Due diligence avec audit NIST** — `01-enterprise/07-due-diligence-nist/` :
  process hiérarchique — un analyste en chef coordonne quatre spécialistes (juridique,
  financier, réputation, conformité) — mémoire du crew activée, constats classés par gravité.
- **Trading algorithmique multi-stratégies** — `03-finance-trading/31-algo-trading/` :
  un crew TypeScript, hiérarchique : le CIO coordonne huit agents entre analyse, risque,
  exécution et conformité, avec le module `_tools/` partagé de la catégorie.
- **De l'ingestion RAG aux réponses citées** — `rag/basic-ingestion/` : ingérer,
  récupérer, générer avec citations — puis passer à `hybrid-retrieval/` et au
  profil `corrective`.
- **Indexer une base de code, puis l'interroger** — `raggable-tree/basic-indexing/` :
  indexation Tree-sitter depuis un simple programme console (sans LLM) — puis
  `raggable-tree/crew-yaml/` donne à un crew les outils d'analyse (`index_codebase`,
  `codebase_map`, `symbol_detail`, …).

## Exécuter un exemple

```bash
orkeon run examples/crew-multifile/          # un dossier de crew
orkeon run examples/scripting/01-hello-world.ork.ts
./examples/run-example.sh 01-enterprise/01-research-assistant   # depuis les sources (config.yaml, main.ork.ts ou un .csproj)
docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners
# puis dans le conteneur : orkeon-example list && orkeon-example run 7
```

Depuis C# (via DI) :

```csharp
// crewFactory : ICrewFactory, orchestrator : ICrewOrchestrationService.
// La fabrique lit à travers le VFS : le chemin est virtuel, sous un montage déclaré
// (Orkeon:FileSystem:Mounts, p. ex. "examples/01-enterprise/07-due-diligence-nist:/crews:ro").
var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml", ct);
var result = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty(), ct);
```

`CreateFromDirectoryAsync` sert à un crew découpé en fichiers — `config.yaml` avec des
dossiers `agents/` et `tasks/`, comme `crew-multifile/`, ou le triplet plat `crew.yaml` +
`agents.yaml` + `tasks.yaml` — pas à un dossier qui ne contient qu'un `config.yaml`.

Les exemples livrent de la configuration, pas des jeux de données — voir la
[politique de données des exemples](./example-data-policy.md) pour monter vos
propres entrées.
