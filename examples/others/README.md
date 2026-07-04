# Benchmark codebases TypeScript — `examples/others/`

Ce dossier contient **20 codebases TypeScript réputées difficiles**, clonées (tarball `HEAD`) pour servir de corpus de test aux pipelines Orkeon :

- `examples/06-engineering-devops/102-ts-codebase-documentation` — analyse + spec fonctionnelle + plan de migration
- `examples/06-engineering-devops/103-ts-codebase-with-fsm` — même pipeline avec FSM (`StateMachine<TState, TEvent>`) + circuit breaker

Toutes les applications retenues ont vocation à être **remplaçables par une équipe d'agents IA** : la valeur réside dans la logique métier/décisionnelle, pas dans l'UX pure. Elles couvrent les cinq familles qui stressent le plus un orchestrateur multi-agent : moteurs de workflow, state machines explicites, éditeurs canvas/CRDT, DSL type-level et plateformes agent-replaceable.

> Voir le plan de test complet, la fiche de chaque repo et les paramétrages suggérés dans `project/tasks/ts-benchmark-repos-for-102-103.md`.

---

## Matrice croisée taille × difficulté

Les 20 repos répartis sur deux axes : **taille** (LOC TS cœur, hors vendored / generated) et **difficulté** pour un pipeline d'analyse multi-agent (types, FSM, DSL, surface d'API).

| Taille ↓ \ Difficulté → | ★★★☆☆ *abordable mais vaste* | ★★★★☆ *complexe et structuré* | ★★★★★ *cas limite / stress-test* | **Total** |
|---|---|---|---|:---:|
| **S** — `< 50k LOC` | `inngest-js` | `restate-sdk` · `trpc` | `yjs` | **4** |
| **M** — `50–150k LOC` | — | `lexical` · `drizzle-orm` · `excalidraw` | `xstate` · `temporal-sdk` | **5** |
| **L** — `150–400k LOC` | `flowise` · `novu` | `trigger.dev` · `medusa` | `tldraw` · `blocksuite` | **6** |
| **XL** — `400–800k LOC` | — | `activepieces` · `cal.com` · `n8n` | `effect` | **4** |
| **XXL** — `> 800k LOC` | — | `backstage` | — | **1** |
| **Total** | **3** | **11** | **6** | **20** |

**Comment lire la matrice**

- **Diagonale bas-droite** (`backstage`, `effect`, `tldraw`, `blocksuite`) — territoire où **Orkeon 103** doit prouver son circuit breaker : volume élevé *et* complexité de types ou de FSM interne.
- **Diagonale haut-gauche** (`inngest-js`, `restate-sdk`, `trpc`, `yjs`) — coin où **Orkeon 102** doit être impeccable : petit, mais aucune excuse pour rater un corpus dense.
- **Colonne ★★★★★** (6 repos) — chacun exerce une facette distincte : CRDT brut (`yjs`), statecharts (`xstate`), déterminisme durable (`temporal-sdk`), FSM d'outils interne (`tldraw`), CRDT + block schema (`blocksuite`), effets typés récursifs (`effect`).
- **Ligne M** (5 repos) — zone d'équilibre idéale pour le warm-up du runner 103 : volume raisonnable, mais toutes les difficultés représentées.

**Légende difficulté**

- ★★★☆☆ — **abordable mais vaste** : preset `default` ou `strict`, pas d'ajustement particulier.
- ★★★★☆ — **complexe et structuré** : preset `default`, surveiller `maxToolCallsPerRound`.
- ★★★★★ — **cas limite** : preset `permissive`, `MaxIterations ≥ 200`, `maxToolCallsPerRound ≥ 20`, activer `useDegradedMode`.

---

## Groupe A — Moteurs de workflow / orchestration (cible idéale 103)

### `n8n/`
- **URL** : https://github.com/n8n-io/n8n
- **Taille / Difficulté** : XL / ★★★★☆
- Moteur visuel d'exécution de workflows avec 400+ intégrations. Un workflow n8n **EST** une FSM (pending → running → waiting → success/error). Cible directe de 103.

### `activepieces/`
- **URL** : https://github.com/activepieces/activepieces
- **Taille / Difficulté** : XL / ★★★★☆
- Alternative open-source type-safe à n8n/Zapier (NX monorepo, "pieces" framework). Pipeline d'exécution `pending → executing → succeeded/failed` explicite, idéal pour tester `TaskExecutionStateMachine`.

### `temporal-sdk-typescript/`
- **URL** : https://github.com/temporalio/sdk-typescript
- **Taille / Difficulté** : M / ★★★★★
- SDK du moteur d'exécution durable Temporal : workflows déterministes, sandboxing V8, replay, cancellation scopes. **Référence industrielle** des state machines durables — si Orkeon documente ça proprement, la FSM interne est validée.

### `trigger.dev/`
- **URL** : https://github.com/triggerdotdev/trigger.dev
- **Taille / Difficulté** : L / ★★★★☆
- Plateforme de background jobs v3 avec run engine complet. Les états (`queued, dequeued, executing, paused, completed, canceled, failed, crashed`) sont littéralement ceux que `TaskExecutionStateMachine` doit couvrir.

### `inngest-js/`
- **URL** : https://github.com/inngest/inngest-js
- **Taille / Difficulté** : S / ★★★☆☆
- SDK TS d'un event-driven durable executor. API `step.run()` / `step.waitForEvent()` / `step.sleep()` — chaque step devient un nœud de graphe. Bon test du pattern "step ≈ tool call".

### `novu/`
- **URL** : https://github.com/novuhq/novu
- **Taille / Difficulté** : L / ★★★☆☆
- Infrastructure de notifications multi-channel (NestJS + React). Workflow editor visuel → moteur de règles de routage, digest, delay, topics. Bonnes boucles de retry/backoff à cartographier.

---

## Groupe B — State machines explicites & scheduling

### `xstate/`
- **URL** : https://github.com/statelyai/xstate
- **Taille / Difficulté** : M / ★★★★★
- Implémentation de référence des statecharts de Harel (actor model). Le code **EST** la spec des concepts FSM. **Test critique** de cohérence : si Orkeon analyse xstate et produit une migration valide, le moteur FSM d'Orkeon comprend sa propre discipline.

### `cal.com/`
- **URL** : https://github.com/calcom/cal.com
- **Taille / Difficulté** : XL / ★★★★☆
- Plateforme de scheduling open-source (Next.js + Turborepo + Prisma + tRPC). Logique métier = cauchemar (fuseaux, disponibilités, round-robin, conflits, buffers, webhooks). Stress test de `functional_analyst`.

### `medusa/`
- **URL** : https://github.com/medusajs/medusa
- **Taille / Difficulté** : L/XL / ★★★★☆
- E-commerce modulaire TS. La v2 introduit un **Workflows SDK** transactionnel avec compensation (saga pattern) — test idéal de `useDegradedMode` et du circuit breaker.

### `restate-sdk-typescript/`
- **URL** : https://github.com/restatedev/sdk-typescript
- **Taille / Difficulté** : S / ★★★★☆
- SDK du durable execution engine Restate : virtual objects, stateful handlers, durable promises. Modèle différent de Temporal — bon pour tester la portabilité des concepts côté Orkeon.

---

## Groupe C — Éditeurs canvas, CRDT, éditeurs de texte

### `tldraw/`
- **URL** : https://github.com/tldraw/tldraw
- **Taille / Difficulté** : L / ★★★★★
- SDK canvas infini (monorepo `@tldraw/editor`, `@tldraw/tlschema`, `@tldraw/store`). Surface `Editor` énorme + moteur de "tools" interne (select, draw, eraser…) qui **est** une FSM. Test critique pour `dependency_mapper`.

### `excalidraw/`
- **URL** : https://github.com/excalidraw/excalidraw
- **Taille / Difficulté** : M / ★★★★☆
- Whiteboard TypeScript/React open-source. Hit-testing, resize handles, rough.js, collaboration temps réel, history. Beaucoup de règles pures, FSM plus implicite.

### `lexical/`
- **URL** : https://github.com/facebook/lexical
- **Taille / Difficulté** : M / ★★★★☆
- Framework d'éditeur extensible (Meta). Modèle immuable avec reconciler type VDOM, node registry, transforms. Architecture plugin stricte très documentable.

### `blocksuite/`
- **URL** : https://github.com/toeverything/blocksuite
- **Taille / Difficulté** : L / ★★★★★
- Base éditeur block-based d'AFFiNE avec Yjs (CRDT). CRDT + block schema + rendu, inter-blocs complexes. Les transitions d'édition collaborative peuvent être modélisées en FSM.

### `yjs/`
- **URL** : https://github.com/yjs/yjs
- **Taille / Difficulté** : S / ★★★★★
- Implémentation de référence du CRDT YATA. Algorithme dense sur peu de fichiers — excellent **stress test** pour `functional_analyst` et `dependency_mapper` sur code algorithmique pur.

---

## Groupe D — Type-DSL, ORM, frameworks lourds en types

### `effect/`
- **URL** : https://github.com/Effect-TS/effect
- **Taille / Difficulté** : XL / ★★★★★
- Système d'effets inspiré de Scala ZIO. Types dépendants, `Layer`/`Context`, `Fiber`, `Schedule`, `Stream` — compiler TS souvent saturé. **Gate stress** du circuit breaker : probablement `MaxIterations=200` + `maxToolCallsPerRound` strict pour éviter les boucles.

### `trpc/`
- **URL** : https://github.com/trpc/trpc
- **Taille / Difficulté** : S / ★★★★☆
- RPC end-to-end type-safe. Inférence récursive, procedure builders, middleware chain. Perf TS compiler notoirement lente — révélateur de récursion côté Orkeon.

### `drizzle-orm/`
- **URL** : https://github.com/drizzle-team/drizzle-orm
- **Taille / Difficulté** : M / ★★★★☆
- ORM SQL type-safe. DSL type-level qui reflète le schéma SQL dans le système de types, adapters par SGBD. Test direct pour `02_data_models.md` et génération ERD Mermaid.

---

## Groupe E — Plateformes « agent-replaceable » (motivation maximale)

### `flowise/`
- **URL** : https://github.com/FlowiseAI/Flowise
- **Taille / Difficulté** : L / ★★★☆☆
- Constructeur visuel de workflows LLM/agents. Flowise construit visuellement ce qu'Orkeon construit en YAML → le plan de migration a une valeur directe ("Flowise JSON → Orkeon YAML"). **Candidat phare** pour la démo publique d'Orkeon.

### `backstage/`
- **URL** : https://github.com/backstage/backstage
- **Taille / Difficulté** : XXL / ★★★★☆
- Portail développeur open-source de Spotify. Plugin system strict + scaffolder (qui est un workflow engine avec retry) + catalog model + software templates. Endurance finale du runner 102/103.

---

## Classement par taille croissante

| Bucket | Rôle dans le pipeline | Repos |
|---|---|---|
| **S** — `< 50k LOC` | Warm-up | `yjs` · `restate-sdk-typescript` · `trpc` · `inngest-js` |
| **M** — `50–150k LOC` | Validation | `xstate` · `temporal-sdk-typescript` · `lexical` · `drizzle-orm` · `excalidraw` |
| **L** — `150–400k LOC` | Charge | `flowise` · `novu` · `tldraw` · `trigger.dev` · `blocksuite` · `medusa` |
| **XL** — `400–800k LOC` | Stress-test | `activepieces` · `effect` · `cal.com` · `n8n` |
| **XXL** — `> 800k LOC` | Endurance | `backstage` |

---

## Ordre d'exécution optimal (mix taille × difficulté, gates bloquants)

1. `yjs` (S / ★★★★★) — baseline densité algorithmique
2. `inngest-js` (S / ★★★☆☆) — baseline simple
3. `trpc` (S / ★★★★☆) — premier test d'inférence
4. `xstate` (M / ★★★★★) — **gate FSM** : si ça rate, arrêter la suite
5. `temporal-sdk-typescript` (M / ★★★★★) — confirmation FSM
6. `drizzle-orm` (M / ★★★★☆) — data models / ERD
7. `tldraw` (L / ★★★★★) — surface API large
8. `medusa` (L / ★★★★☆) — saga / compensation
9. `flowise` (L / ★★★☆☆) — cas d'usage produit
10. `effect` (XL / ★★★★★) — **gate stress** : limite haute du circuit breaker
11. `n8n` (XL / ★★★★☆) — volume maximal réaliste
12. `backstage` (XXL / ★★★★☆) — endurance finale

Les 8 restants (`restate-sdk-typescript`, `lexical`, `excalidraw`, `blocksuite`, `trigger.dev`, `novu`, `activepieces`, `cal.com`) s'exécutent en parallèle dans une matrice CI nightly, hors du gate bloquant.

---

## Notes techniques

- **Téléchargement** : chaque repo a été récupéré en tarball `HEAD` (`codeload.github.com/.../tar.gz/HEAD`), sans `.git/` — cela évite les locks virtiofs sur Windows et les fichiers d'index corrompus
- **Dépendances** : aucun `node_modules/` n'est présent. Lancer `pnpm i` / `npm i` dans un repo *uniquement* si un test dynamique est requis — l'analyse statique Orkeon n'en a pas besoin
- **Git tracking** : ce dossier est **volontairement ignoré** par git (règle `/examples/others/*` dans le `.gitignore` racine), à **l'exception de ce `README.md`** (règle `!/examples/others/README.md`) qui documente le corpus. Les 20 codebases ne sont donc pas redistribuées avec Orkeon — chacun doit les re-télécharger pour reproduire les benchmarks
- **Sous-arbres ciblés** : pour un run sur un sous-package spécifique de `n8n`, `cal.com`, `medusa` ou `backstage`, monter uniquement le chemin concerné dans le runner 102/103 (ex. `examples/others/n8n/packages/core:/src:ro`) pour éviter de saturer `code_analyst`
- **Reproductibilité** : pour rejouer un benchmark à l'identique dans 6 mois, relever le SHA HEAD de chaque repo au moment du run (la version présente dans ce dossier est un snapshot non versionné)
