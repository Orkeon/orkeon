> 🇬🇧 [English version](../../reference/examples-catalog.md)

> **Voir aussi** : [Retour à l'index](../INDEX.md)

# Catalogue des exemples

Tout ce qui vit sous `examples/` tourne sur le même moteur ; cette page est la carte
éditoriale. L'**inventaire faisant foi** — chaque exemple numéroté avec son type de
process, ses comptes agents/tâches et ses outils — est le
[`examples/INDEX.md`](https://github.com/Orkeon/orkeon/blob/main/examples/INDEX.md) généré : il est produit par
`scripts/generate_examples_index.py` et la CI échoue dès qu'il dérive des dossiers
sur disque, si bien qu'aucun compte n'est maintenu à la main ici.

## Les neuf catégories métier

Les crews YAML numérotés vivent dans neuf dossiers thématiques :

| Catégorie | Dossier | Focus |
|-----------|---------|-------|
| **01 — Entreprise** | `01-enterprise/` | CRM, due diligence, conformité, RH, supply chain |
| **02 — Science & Recherche** | `02-science-research/` | Analyse de littérature, biologie computationnelle, science ouverte |
| **03 — Finance & Trading** | `03-finance-trading/` | Trading algorithmique, détection de fraude, consensus de portefeuille |
| **04 — Santé & Bien-être** | `04-health-wellness/` | Aide au diagnostic, essais cliniques, plans personnalisés |
| **05 — Éducation** | `05-education/` | Conception de cours, évaluation, tutorat, apprentissage adaptatif |
| **06 — Ingénierie & DevOps** | `06-engineering-devops/` | Automatisation CI/CD, infrastructure, revue de code, tests |
| **07 — Créatif & Médias** | `07-creative-media/` | Génération de contenu, scénarisation, workflows de design |
| **08 — IoT & Systèmes intelligents** | `08-iot-smart-systems/` | Supervision, maintenance prédictive, détection d'anomalies |
| **09 — Expérimental** | `09-experimental/` | Prototypes, recherche d'orchestration avancée |

## Au-delà des crews numérotés

| Dossier | Ce qu'il montre |
|---------|-----------------|
| `rag/` | Le sous-système RAG : `basic-ingestion/`, `hybrid-retrieval/`, `custom-reranker/`, `crew-yaml/`, plus le jeu d'évaluation offline sous `eval/` |
| `raggable-tree/` | Analyse sémantique de code : `basic-indexing/`, `crew-yaml/`, `custom-adapter/` |
| `scripting/` | Le DSL TypeScript (`.ork.ts`) : hello world → spawn dynamique, littéraux FSM/graphe, outils custom, RAG (`08-rag.ork.ts`) |
| `cli-ts-commands/` | Commandes REPL interactives en `*.cmd.ts`, chargées sans recompilation .NET |
| `local-embeddings/` | Embeddings locaux (sans clé API) branchés sur la mémoire et la sélection d'agents |
| `crew-multifile/` | Un crew décrit comme un dossier (`orkeon run <dir>`) |
| `runners/` | Les projets runners qui exécutent les exemples numérotés, dont deux tools dotnet interactifs |

## Exemples notables

- **Due diligence avec audit NIST** — `01-enterprise/07-due-diligence-nist/` :
  process hiérarchique, outillage conformité, mémoire long terme.
- **Trading algorithmique multi-stratégies** — `03-finance-trading/31-algo-trading/` :
  spécialistes en parallèle avec agrégation par consensus.
- **De l'ingestion RAG aux réponses citées** — `rag/basic-ingestion/` : ingérer,
  récupérer, générer avec citations — puis passer à `hybrid-retrieval/` et au
  profil `corrective`.
- **Indexer une base de code, puis l'interroger** — `raggable-tree/basic-indexing/` :
  indexation Tree-sitter plus les 15 outils d'analyse.

## Exécuter un exemple

```bash
orkeon run examples/crew-multifile/          # un dossier de crew
orkeon run examples/scripting/01-hello-world.ork.ts
./examples/run-example.sh 7                  # exemple numéroté, depuis les sources
docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners
# puis dans le conteneur : orkeon-example list && orkeon-example run 7
```

Depuis C# (via DI) :

```csharp
// crewFactory : ICrewFactory, orchestrator : ICrewOrchestrationService
var crew = await crewFactory.CreateFromDirectoryAsync(
    "examples/01-enterprise/07-due-diligence-nist/", ct);
var result = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty(), ct);
```

Les exemples livrent de la configuration, pas des jeux de données — voir la
[politique de données des exemples](./example-data-policy.md) pour monter vos
propres entrées.
